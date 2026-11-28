using System.Globalization;
using System.Text.Json;
using AnimalTown;
using Npgsql;

record GuestPlacementOperation(Guid Id, string Kind, Guid Placement = default, int Version = 0,
    int Pocket = -1, long PlayerRevision = -1, int X = 0, int Z = 0, int Rotation = 0, int Room = 0);
record PlacementReviewOperation(Guid Id, Guid Placement, int Version, string Kind);
record PendingPlacement(Guid Id, int Item, int X, int Z, int Rotation, int Room, int Version);

static class PlacementReviewRoutes
{
    static DevicePrincipal Device(HttpContext context) => (DevicePrincipal)context.Items["device"]!;

    internal static void MapPlacementReview(this WebApplication app, GameDatabase db)
    {
        // The online mutation path needs card inventory reservation and
        // reconciliation before it can be exposed on a production server.
        if (!app.Configuration.GetValue<bool>("Multiplayer:EnablePlacements")&&!app.Configuration.GetValue<bool>("OnlineGameplay:EnablePlay")) return;

        app.MapPost("/v1/review", (Delegate)((HttpContext c) =>
            db.BeginPlacementReview(Device(c)))).WithMetadata(ApiAccess.DeviceOnly);
        app.MapPost("/v1/review/{lease:guid}/heartbeat", (HttpContext c, Guid lease) =>
            db.HeartbeatPlacementReview(Device(c), lease)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapDelete("/v1/review/{lease:guid}", (HttpContext c, Guid lease) =>
            db.EndPlacementReview(Device(c), lease)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapGet("/v1/review/{lease:guid}/placements", (HttpContext c, Guid lease, Guid? after) =>
            db.ListPlacementReview(Device(c), lease, after)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapPost("/v1/review/{lease:guid}/placements/ops", (HttpContext c, Guid lease, PlacementReviewOperation op) =>
            db.DecidePlacementReview(Device(c), lease, op)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapGet("/v1/review/{lease:guid}/residents", (HttpContext c, Guid lease) =>
            db.ListResidentReview(Device(c),lease)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapPost("/v1/review/{lease:guid}/residents/ops", (HttpContext c, Guid lease, ResidentReviewOperation op) =>
            db.DecideResidentReview(Device(c),lease,op)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapPost("/v1/review/{lease:guid}/residents/complete", (HttpContext c, Guid lease, ResidentReviewCompletion op) =>
            db.CompleteResidentReview(Device(c),lease,op)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapPost("/v1/review/{lease:guid}/scene", (HttpContext c, Guid lease) =>
            db.PrepareReviewScene(Device(c),lease)).WithMetadata(ApiAccess.DeviceOnly);
        app.MapGet("/v1/review/{lease:guid}/scene", async (HttpContext c, Guid lease, string sha256, int offset, int count) =>
            Results.Bytes(await db.ReadReviewScene(Device(c),lease,sha256,offset,count),"application/octet-stream")).WithMetadata(ApiAccess.DeviceOnly);
    }
}

sealed partial class GameDatabase
{
    // Responses and the live placement overlay have independently bounded sizes.
    const int PlacementLimit = 64, PlacementPageSize = 16, ReviewLeaseSeconds = 10;
    static readonly JsonSerializerOptions PlacementJson = new(JsonSerializerDefaults.Web);

    record GuestPlacement(Guid Id, Guid Guest, string Villager, int Item, int X, int Z,
        int Rotation, int Room, int OwnerSlot, int Version)
    {
        public void Project(Placement placement)
        {
            placement.Item = Item;
            placement.X = X;
            placement.Z = Z;
            placement.Rotation = Rotation;
            placement.Room = Room;
            placement.Owner = OwnerSlot;
        }

        public PendingPlacement View() => new(Id, Item, X, Z, Rotation, Room, Version);
    }

    static string ReviewVillager(GameState state)
    {
        RequireLocalPlayer(state);
        var identity = state.Town.Jobs.Player(state.ActiveProfile);
        if (!identity.IsValid) throw new GameError(409, "villager-identity-required");
        return identity.Town.ToString("X16", CultureInfo.InvariantCulture) + "/" +
            identity.Serial.ToString("X16", CultureInfo.InvariantCulture);
    }

    static string ReviewDeviceVillager(GameState state, DevicePrincipal device)
    {
        if (!string.Equals(state.Town.Jobs.TownId.ToString("X16", CultureInfo.InvariantCulture),
            device.Town, StringComparison.Ordinal))
            throw new GameError(409, "device-town-mismatch");
        return ReviewVillager(state);
    }

    static async Task<bool> ReviewActive(NpgsqlConnection c, NpgsqlTransaction? t, Guid host)
    {
        await using var command = Cmd(c, t,
            "SELECT EXISTS (SELECT 1 FROM town_review_leases WHERE host=$1 AND expires>clock_timestamp())", host);
        return await command.ExecuteScalarAsync() is true;
    }

    internal async Task<bool> IsReviewActive(Guid host)
    {
        await using var c = await source.OpenConnectionAsync();
        return await ReviewActive(c, null, host);
    }

    static async Task RequireNoHostReview(NpgsqlConnection c, NpgsqlTransaction t, Guid host)
    {
        if (await ReviewActive(c, t, host))
            throw new GameError(409, "town-in-review");
    }

    static async Task<bool> IsImportedPlacementTown(NpgsqlConnection c, NpgsqlTransaction t, Guid town)
    {
        await using var command = Cmd(c, t,
            "SELECT EXISTS (SELECT 1 FROM initial_town_imports WHERE player=$1)", town);
        return await command.ExecuteScalarAsync() is true;
    }

    static async Task RequireImportedPlacementTown(NpgsqlConnection c, NpgsqlTransaction t, Guid host)
    {
        if (!await IsImportedPlacementTown(c, t, host))
            throw new GameError(409, "town-checkpoint-required");
    }

    static async Task RequireLegacyCommerce(NpgsqlConnection c, NpgsqlTransaction t, Guid town)
    {
        await RequireNoHostReview(c, t, town);
        if (await IsImportedPlacementTown(c, t, town))
            throw new GameError(409, "card-settlement-required");
    }

    static async Task RequirePlacementLease(NpgsqlConnection c, NpgsqlTransaction t, DevicePrincipal device,
        Guid lease, string villager)
    {
        await using var command = Cmd(c, t, """
            SELECT EXISTS (SELECT 1 FROM town_review_leases
            WHERE host=$1 AND id=$2 AND device=$3 AND generation=$4 AND villager=$5
              AND expires>clock_timestamp())
            """, device.Player, lease, device.Id, device.Generation, villager);
        if (await command.ExecuteScalarAsync() is not true)
            throw new GameError(409, "review-lease-ended");
    }

    static async Task<JsonElement?> ReadPlacementReceipt(NpgsqlConnection c, NpgsqlTransaction t,
        Guid player, Guid id, string fingerprint)
    {
        await using var command = Cmd(c, t,
            "SELECT fingerprint,result::text FROM operations WHERE player=$1 AND id=$2", player, id);
        await using var rows = await command.ExecuteReaderAsync();
        if (!await rows.ReadAsync()) return null;
        if (!string.Equals(rows.GetString(0), fingerprint, StringComparison.Ordinal))
            throw new GameError(409, "idempotency-key-reused");
        return JsonSerializer.Deserialize<JsonElement>(rows.GetString(1));
    }

    static async Task WritePlacementReceipt(NpgsqlConnection c, NpgsqlTransaction t, Guid player, Guid id,
        string fingerprint, object result)
    {
        await using var command = Cmd(c, t,
            "INSERT INTO operations(player,id,fingerprint,result) VALUES($1,$2,$3,$4::jsonb)",
            player, id, fingerprint, JsonSerializer.Serialize(result, PlacementJson));
        await command.ExecuteNonQueryAsync();
    }

    static async Task<List<GuestPlacement>> ReadPlacements(NpgsqlConnection c, NpgsqlTransaction? t,
        Guid host, Guid after, int limit)
    {
        await using var command = Cmd(c, t, """
            SELECT id,guest,villager,item,x,z,rotation,room,owner_slot,version FROM guest_placements
            WHERE host=$1 AND id>$2 ORDER BY id LIMIT $3
            """, host, after, limit);
        await using var rows = await command.ExecuteReaderAsync();
        var placements = new List<GuestPlacement>();
        while (await rows.ReadAsync())
            placements.Add(new GuestPlacement(rows.GetGuid(0), rows.GetGuid(1), rows.GetString(2),
                rows.GetInt32(3), rows.GetInt32(4), rows.GetInt32(5), rows.GetInt32(6), rows.GetInt32(7),
                rows.GetInt32(8), rows.GetInt32(9)));
        return placements;
    }

    static void ProjectPlacements(GameState host, List<GuestPlacement> placements)
    {
        if (placements.Count > PlacementLimit || placements.Count > host.Town.Placed.Length - host.Town.PlacedCount)
            throw new GameError(409, "placement-capacity-exceeded");
        foreach (var placement in placements)
            placement.Project(host.Town.Placed[host.Town.PlacedCount++]);
    }

    static void RequirePlacementReach(int poseRoom, float poseX, float poseZ, int room, int x, int z)
    {
        if (room != poseRoom || (uint)x >= Town.Width || (uint)z >= Town.Height ||
            !float.IsFinite(poseX) || !float.IsFinite(poseZ))
            throw new GameError(403, "placement-out-of-reach");
        float dx = x + .5f - poseX, dz = z + .5f - poseZ;
        if (dx * dx + dz * dz > 2.5f * 2.5f)
            throw new GameError(403, "placement-out-of-reach");
    }

    static async Task<Guid> FindGuestPlacementHost(NpgsqlConnection c, NpgsqlTransaction t, Guid guest, Guid visit)
    {
        await using var command = Cmd(c, t, "SELECT host FROM visits WHERE id=$1 AND guest=$2", visit, guest);
        return await command.ExecuteScalarAsync() is Guid host ? host : throw new GameError(404, "visit-not-found");
    }

    static async Task RequirePlacementVisit(NpgsqlConnection c, NpgsqlTransaction t, Guid host, Guid guest, Guid visit)
    {
        await RequireImportedPlacementTown(c, t, host);
        await RequireImportedPlacementTown(c, t, guest);
        await RequireOpenGate(c, t, host, guest);
        await using var command = Cmd(c, t, """
            SELECT EXISTS (SELECT 1 FROM visits WHERE id=$1 AND host=$2 AND guest=$3
                AND NOT ended AND expires>clock_timestamp())
            """, visit, host, guest);
        if (await command.ExecuteScalarAsync() is not true)
            throw new GameError(403, "active-visit-required");
    }

    public async Task<object> MutateGuestPlacement(DevicePrincipal device, Guid visit, Guid connectionId, GuestPlacementOperation op, HostedAuthority? authority = null)
    {
        if (op.Id == Guid.Empty || op.Kind is not ("place" or "move" or "pickup") ||
            (op.Kind == "place" ? op.Placement != Guid.Empty || op.Version != 0 || op.PlayerRevision < 0 :
                op.Placement == Guid.Empty || op.Version <= 0) ||
            (op.Kind == "pickup" && op.PlayerRevision < 0))
            throw new GameError(400, "invalid-guest-placement-operation");

        Guid guest = device.Player;
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        bool owner=authority is {} own&&own.Host==guest&&own.Player==guest&&visit==Guid.Empty;
        Guid host = owner?guest:await FindGuestPlacementHost(c, t, guest, visit);
        await LockVisitParticipants(c, t, host, guest);
        await RequireActiveDevice(c, t, device);
        var loaded=await Load(c,t,host,false);var town=loaded.State;
        var personal=owner?loaded:await Load(c,t,guest,false);var player=personal.State;long revision=personal.Revision;
        string villager = ReviewDeviceVillager(player, device);
        string fingerprint = Hash("guest-placement:" + visit + ":" + villager + ":" + JsonSerializer.Serialize(op));
        if (await ReadPlacementReceipt(c, t, guest, op.Id, fingerprint) is JsonElement receipt) return receipt;

        if (authority is { } hosted)
        {
            if (hosted.Host != host || hosted.Player != guest || hosted.Connection != connectionId || hosted.Visit != visit)
                throw new GameError(403, "invalid-hosted-placement");
            await RequireHostedActor(c, t, hosted);
            await RequireOnlineInventory(c, t, device, host, visit);
        }

        if(!owner)await RequirePlacementVisit(c, t, host, guest, visit);
        await RequireNoHostReview(c, t, host);
        if (!TryGetLiveGuestPose(host, guest, visit, device, connectionId, out var pose))
            throw new GameError(409, "live-position-required");
        RequireLocalPlayer(town);

        var placements = await ReadPlacements(c, t, host, Guid.Empty, PlacementLimit + 1);
        int baseCount = town.Town.PlacedCount;
        ProjectPlacements(town, placements);
        bool touchesPocket = op.Kind is "place" or "pickup";
        if (touchesPocket && op.PlayerRevision != revision)
            throw new GameError(409, "visitor-inventory-changed");

        var actor = owner?TownItemActor.LocalResident(town):TownItemActor.Visitor(town, player);
        GuestPlacement placement;
        if (op.Kind == "place")
        {
            if(authority is not null)await RequireUnboundOnlinePocket(c,t,guest,op.Pocket);
            RequirePlacementReach(pose.Room, pose.X, pose.Z, op.Room, op.X, op.Z);
            if((uint)op.Pocket>=player.Capacity)throw new GameError(409,"placement-rejected");
            RequireClearLivePlacement(host,player.Pockets[op.Pocket],op.X,op.Z,op.Rotation,op.Room);
            if (placements.Count >= PlacementLimit ||
                !TownItemActions.Place(town, player, actor, op.Pocket, op.X, op.Z, op.Rotation, op.Room))
                throw new GameError(409, "placement-rejected");

            var placed = town.Town.Placed[town.Town.PlacedCount - 1];
            placement = new GuestPlacement(Guid.NewGuid(), guest, villager, placed.Item, placed.X, placed.Z,
                placed.Rotation, placed.Room, placed.Owner, 1);
            await using var command = Cmd(c, t, """
                INSERT INTO guest_placements(host,id,guest,villager,item,x,z,rotation,room,owner_slot,version)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
                """, host, placement.Id, guest, villager, placement.Item, placement.X, placement.Z,
                placement.Rotation, placement.Room, placement.OwnerSlot, placement.Version);
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            int index = placements.FindIndex(entry => entry.Id == op.Placement);
            if (index < 0 || placements[index].Guest != guest || placements[index].Villager != villager)
                throw new GameError(404, "guest-placement-not-found");
            placement = placements[index];
            if (placement.Version != op.Version) throw new GameError(409, "placement-changed");
            RequirePlacementReach(pose.Room, pose.X, pose.Z, placement.Room, placement.X, placement.Z);
            if (op.Kind == "move")
            {
                RequirePlacementReach(pose.Room, pose.X, pose.Z, op.Room, op.X, op.Z);
                RequireClearLivePlacement(host,placement.Item,op.X,op.Z,op.Rotation,op.Room);
                if (!TownItemActions.MovePlacement(town, player, actor, baseCount + index, op.X, op.Z, op.Rotation, op.Room))
                    throw new GameError(409, "placement-rejected");
                placement = placement with { X = op.X, Z = op.Z, Rotation = op.Rotation, Version = checked(op.Version + 1) };
                await using var command = Cmd(c, t, """
                    UPDATE guest_placements SET x=$3,z=$4,rotation=$5,version=$6 WHERE host=$1 AND id=$2
                    """, host, placement.Id, placement.X, placement.Z, placement.Rotation, placement.Version);
                await command.ExecuteNonQueryAsync();
            }
            else
            {
                if (!player.Receive(placement.Item)) throw new GameError(409, "visitor-pockets-full");
                await DeletePendingPlacement(c, t, host, placement.Id);
            }
        }

        if(owner)town.Town.PlacedCount=baseCount;
        if (touchesPocket) await Save(c, t, guest, player);
        var result = new { placement = placement.View(), pickedUp = op.Kind == "pickup", playerRevision = revision + (touchesPocket ? 1 : 0) };
        await WritePlacementReceipt(c, t, guest, op.Id, fingerprint, result);
        if (authority is not null)
        {
            await AppendHostedEvent(c, t, host, null, 1, guest, [1]);
            if (touchesPocket) await AppendHostedEvent(c, t, host, guest, 2, guest, InventoryPacket(player, revision + 1));
        }
        if (!TryGetLiveGuestPose(host, guest, visit, device, connectionId, out _))
            throw new GameError(409, "gameplay-connection-changed");
        await t.CommitAsync();
        return result;
    }

    static async Task DeletePendingPlacement(NpgsqlConnection c, NpgsqlTransaction t, Guid host, Guid id)
    {
        await using var command = Cmd(c, t, "DELETE FROM guest_placements WHERE host=$1 AND id=$2", host, id);
        if (await command.ExecuteNonQueryAsync() != 1) throw new GameError(409, "placement-changed");
    }

    public async Task<object> BeginPlacementReview(DevicePrincipal device,HostedAuthority? authority=null)
    {
        Guid host = device.Player;
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, host);
        await RequireActiveDevice(c, t, device);
        if(authority is {} active)await RequireHostedActor(c,t,active);
        await RequireImportedPlacementTown(c, t, host);
        var state = (await Load(c, t, host, false)).State;
        string villager = ReviewDeviceVillager(state, device);
        await using (var pending = Cmd(c, t, "SELECT EXISTS (SELECT 1 FROM guest_placements WHERE host=$1) OR EXISTS(SELECT 1 FROM offline_resident_reviews WHERE player=$1)", host))
            if (await pending.ExecuteScalarAsync() is not true)
                throw new GameError(409, "review-empty");

        Guid lease = Guid.NewGuid();
        await using var command = Cmd(c, t, """
            INSERT INTO town_review_leases(host,id,device,generation,villager,expires)
            VALUES($1,$2,$3,$4,$5,clock_timestamp()+make_interval(secs=>$6))
            ON CONFLICT(host) DO UPDATE SET
                id=CASE WHEN town_review_leases.expires>clock_timestamp() THEN town_review_leases.id ELSE excluded.id END,
                device=excluded.device,generation=excluded.generation,villager=excluded.villager,expires=excluded.expires
            WHERE town_review_leases.expires<=clock_timestamp()
               OR (town_review_leases.device=excluded.device AND town_review_leases.generation=excluded.generation
                   AND town_review_leases.villager=excluded.villager)
            RETURNING id
            """, host, lease, device.Id, device.Generation, villager, (double)ReviewLeaseSeconds);

        if (await command.ExecuteScalarAsync() is not Guid id) throw new GameError(409, "review-in-progress");
        await t.CommitAsync();
        return new { lease = id, expiresIn = ReviewLeaseSeconds };
    }

    public async Task<object> HeartbeatPlacementReview(DevicePrincipal device, Guid lease)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, device.Player);
        await RequireActiveDevice(c, t, device);
        var state = (await Load(c, t, device.Player, false)).State;
        string villager = ReviewDeviceVillager(state, device);
        await using var command = Cmd(c, t, """
            UPDATE town_review_leases SET expires=clock_timestamp()+make_interval(secs=>$6)
            WHERE host=$1 AND id=$2 AND device=$3 AND generation=$4 AND villager=$5
              AND expires>clock_timestamp()
            """, device.Player, lease, device.Id, device.Generation, villager, (double)ReviewLeaseSeconds);
        if (await command.ExecuteNonQueryAsync() != 1) throw new GameError(409, "review-lease-ended");
        await t.CommitAsync();
        return new { expiresIn = ReviewLeaseSeconds };
    }

    public async Task<object> EndPlacementReview(DevicePrincipal device, Guid lease,HostedAuthority? authority=null)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, device.Player);
        await RequireActiveDevice(c, t, device);
        if(authority is {} active)await RequireHostedActor(c,t,active);
        await using var command = Cmd(c, t, """
            DELETE FROM town_review_leases WHERE host=$1 AND id=$2 AND device=$3 AND generation=$4
            """, device.Player, lease, device.Id, device.Generation);
        await command.ExecuteNonQueryAsync();
        await t.CommitAsync();
        return new { ok = true };
    }

    public async Task<object> ListPlacementReview(DevicePrincipal device, Guid lease, Guid? after)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, device.Player);
        await RequireActiveDevice(c, t, device);
        var state = (await Load(c, t, device.Player, false)).State;
        await RequirePlacementLease(c, t, device, lease, ReviewDeviceVillager(state, device));
        var placements = await ReadPlacements(c, t, device.Player, after ?? Guid.Empty, PlacementPageSize + 1);
        var entries = placements.Take(PlacementPageSize).Select(entry => entry.View()).ToArray();
        Guid? next = placements.Count <= PlacementPageSize ? null : entries[^1].Id;
        await t.CommitAsync();
        return new { entries, next };
    }

    public async Task<object> DecidePlacementReview(DevicePrincipal device, Guid lease, PlacementReviewOperation op,HostedAuthority? authority=null)
    {
        if (op.Id == Guid.Empty || op.Placement == Guid.Empty || op.Version <= 0 || op.Kind is not ("keep" or "koban" or "keep-guest"))
            throw new GameError(400, "invalid-placement-review-operation");
        Guid host = device.Player;
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, host);
        await RequireActiveDevice(c, t, device);
        var (town, revision) = await Load(c, t, host, false);
        string villager = ReviewDeviceVillager(town, device);
        string fingerprint = Hash("placement-review:" + villager + ":" + JsonSerializer.Serialize(op));
        if (await ReadPlacementReceipt(c, t, host, op.Id, fingerprint) is JsonElement receipt) return receipt;
        if(authority is {} active)await RequireHostedActor(c,t,active);
        await RequirePlacementLease(c, t, device, lease, villager);

        var placements = await ReadPlacements(c, t, host, Guid.Empty, PlacementLimit + 1);
        int index = placements.FindIndex(entry => entry.Id == op.Placement);
        if (index < 0) throw new GameError(404, "guest-placement-not-found");
        GuestPlacement placement = placements[index];
        if (placement.Version != op.Version) throw new GameError(409, "placement-changed");

        int baseCount = town.Town.PlacedCount;
        if(op.Kind=="keep-guest")
        {
            var selected=new Placement();placement.Project(selected);
            try{AnimalTown.Online.OnlineTownMerge.KeepPlacement(town,selected);}
            catch(InvalidOperationException){throw new GameError(409,"placement-conflict");}
        }
        else if (op.Kind == "keep")
        {
            if(placement.Room==0)
            {
                var footprint=new Placement();placement.Project(footprint);BuildingWorld.Footprint(footprint,out int width,out int height);
                for(int i=0;i<town.Town.GroundCount;i++)
                {var g=town.Town.Ground[i];if(g.Kind!=4&&g.X>=placement.X&&g.X<placement.X+width&&g.Z>=placement.Z&&g.Z<placement.Z+height)throw new GameError(409,"placement-conflict");}
            }
            if (baseCount == town.Town.Placed.Length || !town.CanPlace(placement.Item, placement.X, placement.Z,
                placement.Rotation, placement.Room, placement.OwnerSlot, existing: true))
                throw new GameError(409, "placement-conflict");
            placement.Project(town.Town.Placed[town.Town.PlacedCount++]);
        }
        else
        {
            ProjectPlacements(town, placements);
            if (!TownItemActions.RemovePlacement(town, town, TownItemActor.LocalResident(town), baseCount + index))
                throw new GameError(409, "koban-capacity-exceeded");
            town.Town.PlacedCount = baseCount;
        }

        await Save(c, t, host, town);
        await DeletePendingPlacement(c, t, host, placement.Id);
        var result = new { placement = placement.View(), decision = op.Kind, revision = revision + 1 };
        await WritePlacementReceipt(c, t, host, op.Id, fingerprint, result);
        await using(var hosted=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM hosted_towns WHERE host=$1)",host))
            if(await hosted.ExecuteScalarAsync() is true)await AppendHostedEvent(c,t,host,null,1,host,[1]);
        await RequirePlacementLease(c, t, device, lease, villager);
        await t.CommitAsync();
        return result;
    }
}
