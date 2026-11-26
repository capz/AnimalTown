using System.Text.Json;
using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;
using Npgsql;

sealed record HostedSnapshot(byte[] Bytes, long Cursor);
sealed record HostedCommandResult(byte[] Reply, HostedSnapshot? Snapshot = null,
    HostedSeat? Pose = null, GameState? World = null, long? ReplayAfter = null, Guid? ReviewLease = null);

sealed partial class GameDatabase
{
    static string JobKey(JobIdentity id) => id.Town.ToString("X16", System.Globalization.CultureInfo.InvariantCulture) + "/" + id.Serial.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
    static OnlineId WireId(Guid value)
    { var bytes = value.ToByteArray(); var r = new OnlinePacketReader(bytes, 0, 16); return r.Id(); }
    static Guid HostId(OnlineId value)
    { var bytes = new byte[16]; new OnlinePacketWriter(bytes).Id(value); return new Guid(bytes); }
    static byte[] Packet(Action<OnlinePacketWriter> write, int maximum = 512)
    { var bytes = new byte[maximum]; var w = new OnlinePacketWriter(bytes); write(w); return bytes[..w.Count]; }
    static byte[] CommandResult(OnlineCommand command, int status, long revision = 0, string error = "", Action<OnlinePacketWriter>? extra = null)
        => Packet(w => { w.Byte(OnlineGameplayCodec.Result); w.Id(command.Id); w.Int(status); w.Long(revision); w.Text(error, 128); extra?.Invoke(w); });
    static byte[] InventoryPacket(GameState player, long revision) => Packet(w =>
    {
        w.Long(revision); w.Int(player.Capacity); w.Int(player.Wallet); w.Long(player.Savings); w.Long(player.Debt);
        w.Int(player.Species); w.Int(player.Palette); w.Int(player.Shirt); w.Text(player.Name, 32);
        for (int i = 0; i < GameState.MaximumPockets; i++) w.Int(player.Pockets[i]);
    });

    internal async Task<HostedCommandResult> ExecuteHostedCommand(HostedAuthority authority, OnlineCommand command)
    {
        try
        {
            if (command.Kind == OnlineCommandKind.Snapshot)
            {
                var snapshot = await SnapshotHostedTown(authority);
                return new(CommandResult(command, 200, snapshot.Cursor), snapshot);
            }
            if (command.Kind == OnlineCommandKind.Replay)
            {
                if (command.Revision < 0) throw new GameError(400, "invalid-event-cursor");
                return new(CommandResult(command, 200), ReplayAfter: command.Revision);
            }
            if (command.Kind is OnlineCommandKind.Place or OnlineCommandKind.MovePlacement or OnlineCommandKind.PickupPlacement)
            {
                var operation = new GuestPlacementOperation(HostId(command.Id),
                    command.Kind == OnlineCommandKind.Place ? "place" : command.Kind == OnlineCommandKind.MovePlacement ? "move" : "pickup",
                    HostId(command.Entity), command.A, command.B, command.Revision, command.C, command.D, command.E, command.F);
                var result = JsonSerializer.SerializeToElement(await MutateGuestPlacement(authority.Device, authority.Visit, authority.Connection, operation, authority), PlacementJson);
                var placement = result.GetProperty("placement");
                return new(CommandResult(command, 200, result.GetProperty("playerRevision").GetInt64(), extra: w =>
                { w.Id(WireId(placement.GetProperty("id").GetGuid())); w.Int(placement.GetProperty("version").GetInt32()); }));
            }
            if (command.Kind == OnlineCommandKind.Return)
            {
                await PrepareOnlinePublication(authority.Device, HostId(command.Entity));
                return new(CommandResult(command, 200));
            }
            if(command.Kind is OnlineCommandKind.ReviewBegin or OnlineCommandKind.ReviewDecision or OnlineCommandKind.ReviewEnd)
            {
                if(authority.Player!=authority.Host)throw new GameError(403,"owner-review-only");
                if(command.Kind==OnlineCommandKind.ReviewBegin)
                {
                    var started=JsonSerializer.SerializeToElement(await BeginPlacementReview(authority.Device,authority),PlacementJson);
                    Guid lease=started.GetProperty("lease").GetGuid();
                    return new(CommandResult(command,200,extra:w=>w.Id(WireId(lease))),await SnapshotHostedTown(authority),ReviewLease:lease);
                }
                if(command.Kind==OnlineCommandKind.ReviewEnd)
                {
                    await EndPlacementReview(authority.Device,HostId(command.Entity),authority);
                    return new(CommandResult(command,200),ReviewLease:Guid.Empty);
                }
                if(!Guid.TryParseExact(command.Text,"D",out Guid context)||command.A is <0 or >1||command.Revision is <1 or >int.MaxValue)
                    throw new GameError(400,"invalid-placement-review-operation");
                await DecidePlacementReview(authority.Device,context,new(HostId(command.Id),HostId(command.Entity),(int)command.Revision,command.A==0?"keep":"koban"),authority);
                return new(CommandResult(command,200));
            }
            return await ExecuteHostedMutation(authority, command);
        }
        catch (GameError error) { return new(CommandResult(command, error.Status, error: error.Message)); }
        catch (InvalidOperationException) { return new(CommandResult(command, 400, error: "invalid-gameplay-command")); }
    }

    internal async Task<HostedSnapshot> SnapshotHostedTown(HostedAuthority authority)
    {
        await using var c = await source.OpenConnectionAsync();
        await using var t = await c.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        await RequireHostedActor(c, t, authority);
        var town = (await Load(c, t, authority.Host, false)).State;
        var player = authority.Player == authority.Host ? town : (await Load(c, t, authority.Player, false)).State;
        var placements = await ReadPlacements(c, t, authority.Host, Guid.Empty, PlacementLimit + 1);
        ProjectPlacements(town, placements);
        var world = new byte[OnlineTownProjection.MaximumBytes]; int length = OnlineTownProjection.Write(town, world);
        long cursor;
        await using (var head = Cmd(c, t, "SELECT cursor FROM hosted_towns WHERE host=$1", authority.Host)) cursor = (long)(await head.ExecuteScalarAsync())!;
        byte[] inventory = InventoryPacket(player, (await Load(c, t, authority.Player, false)).Revision);
        var bytes = new byte[OnlineTownProjection.MaximumBytes + 8192]; var w = new OnlinePacketWriter(bytes);
        w.Byte(1); w.Long(cursor); w.Int(length); w.Bytes(world, 0, length); w.Int(inventory.Length); w.Bytes(inventory, 0, inventory.Length);
        w.Int(placements.Count);
        foreach (var placement in placements)
        { w.Id(WireId(placement.Id)); w.Id(WireId(placement.Guest)); w.Int(placement.Version); }
        await t.CommitAsync(); return new(bytes[..w.Count], cursor);
    }

    async Task<HostedCommandResult> ExecuteHostedMutation(HostedAuthority authority, OnlineCommand command)
    {
        bool query = command.Kind is OnlineCommandKind.Inventory or OnlineCommandKind.ShopView or OnlineCommandKind.Jobs;
        if (!query && command.Id.IsEmpty) throw new GameError(400, "operation-id-required");
        Guid id = HostId(command.Id);
        byte[] request = new byte[4096]; int requestLength = OnlineGameplayCodec.WriteCommand(request, command);
        string fingerprint = Hash("hosted:" + authority.Host + ":" + authority.Visit + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(request.AsSpan(0, requestLength))));
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        if (authority.Player == authority.Host) await LockTown(c, t, authority.Host);
        else await LockVisitParticipants(c, t, authority.Host, authority.Player);
        var seat = await RequireHostedActor(c, t, authority);
        if (!query && await ReadPlacementReceipt(c, t, authority.Player, id, fingerprint) is { } receipt)
            return new(Convert.FromBase64String(receipt.GetProperty("wire").GetString()!));
        if (!query) await RequireNoHostReview(c, t, authority.Host);
        var loaded = await Load(c, t, authority.Host, false); var town = loaded.State;
        var personal = authority.Player == authority.Host ? loaded : await Load(c, t, authority.Player, false);
        var player = personal.State;
        byte[] reply; HostedSeat? changedPose = null; bool townChanged = false, inventoryChanged = false;
        switch (command.Kind)
        {
            case OnlineCommandKind.Inventory:
                reply = CommandResult(command, 200, personal.Revision, extra: w => { byte[] p = InventoryPacket(player, personal.Revision); w.Bytes(p, 0, p.Length); });
                break;
            case OnlineCommandKind.ShopView:
                reply = CommandResult(command, 200, town.Town.Shop.Revision, extra: w =>
                {
                    w.Int(town.Town.StockCount);
                    for (int i = 0; i < town.Town.StockCount; i++) { w.Int(town.Town.Stock[i]); w.Int(town.Town.Shop.Price(town, ShopSource.Display, i)); }
                    w.Int(TownShop.CabinetCount);
                    for (int i = 0; i < TownShop.CabinetCount; i++) { w.Int(TownShop.CabinetItem(i)); w.Int(town.Town.Shop.Price(town, ShopSource.Cabinet, i)); }
                    w.Int(TownShop.SaleSlots(town));
                    for (int i = 0; i < TownShop.SaleSlots(town); i++) { w.Int(town.Town.Shop.SaleItems[i]); w.Int(town.Town.Shop.SalePrices[i]); }
                });
                break;
            case OnlineCommandKind.Jobs:
            case OnlineCommandKind.AskJob:
            case OnlineCommandKind.AcceptJob:
            case OnlineCommandKind.CompleteJob:
            case OnlineCommandKind.AbandonJob:
            case OnlineCommandKind.CollectReward:
                (reply, townChanged, inventoryChanged) = await HostedJobCommand(c, t, authority, seat, town, player, command);
                break;
            case OnlineCommandKind.EnterRoom:
                ProjectPlacements(town, await ReadPlacements(c, t, authority.Host, Guid.Empty, PlacementLimit + 1));
                changedPose = ResolveHostedPortal(town, seat, command.A);
                await using (var update = Cmd(c, t, "UPDATE hosted_actors SET room=$4,x=$5,z=$6,updated=clock_timestamp() WHERE host=$1 AND player=$2 AND connection=$3",
                    authority.Host, authority.Player, authority.Connection, changedPose.Value.Room, changedPose.Value.X, changedPose.Value.Z))
                    if (await update.ExecuteNonQueryAsync() != 1) throw new GameError(409, "gameplay-connection-changed");
                reply = CommandResult(command, 200, extra: w => { w.Int(changedPose.Value.Room); w.Int((int)(changedPose.Value.X * 65536)); w.Int((int)(changedPose.Value.Z * 65536)); });
                break;
            case OnlineCommandKind.Buy:
            case OnlineCommandKind.List:
            case OnlineCommandKind.CancelListing:
            case OnlineCommandKind.Reprice:
                await RequireOnlineInventory(c, t, authority.Device, authority.Host, authority.Visit);
                if (seat.Room != TownShop.Room) throw new GameError(409, "enter-shop-first");
                if (command.Revision != town.Town.Shop.Revision) throw new GameError(409, "shop-changed");
                if (command.Kind != OnlineCommandKind.Buy && authority.Player != authority.Host) throw new GameError(403, "only-local-villagers-may-stock-shop");
                if (command.A < 0 || command.A > 2) throw new GameError(400, "invalid-shop-source");
                if (command.Kind == OnlineCommandKind.List) await RequireUnboundOnlinePocket(c, t, authority.Player, command.C);
                var source = (ShopSource)command.A;
                bool bought = command.Kind switch
                {
                    OnlineCommandKind.Buy => town.Town.Shop.Buy(town, player, source, command.B, checked((int)command.Revision)),
                    OnlineCommandKind.List => source == ShopSource.Consignment && town.Town.Shop.List(town, command.B, command.C, command.D),
                    OnlineCommandKind.CancelListing => source == ShopSource.Consignment && town.Town.Shop.Remove(town, command.B),
                    OnlineCommandKind.Reprice => source == ShopSource.Consignment && town.Town.Shop.Reprice(town, command.B, command.D),
                    _ => false
                };
                if (!bought) throw new GameError(409, "shop-operation-rejected");
                townChanged = true; inventoryChanged = command.Kind != OnlineCommandKind.Reprice;
                reply = CommandResult(command, 200, town.Town.Shop.Revision);
                break;
            case OnlineCommandKind.Chat:
                if (command.Text.Length == 0 || command.Text.Length > TownChat.MaximumLength || !TownChat.Supported(command.Text) || command.Text[0] == '/')
                    throw new GameError(400, "invalid-chat-text");
                await using (var restricted = Cmd(c, t, "SELECT chat_restricted FROM players WHERE id=$1", authority.Player))
                    if (await restricted.ExecuteScalarAsync() is true) throw new GameError(403, "chat-restricted");
                await using (var rate = Cmd(c, t, """
                    INSERT INTO hosted_chat_limits(player,started,count) VALUES($1,clock_timestamp(),1)
                    ON CONFLICT(player) DO UPDATE SET
                      started=CASE WHEN hosted_chat_limits.started<clock_timestamp()-interval '10 seconds' THEN excluded.started ELSE hosted_chat_limits.started END,
                      count=CASE WHEN hosted_chat_limits.started<clock_timestamp()-interval '10 seconds' THEN 1 ELSE hosted_chat_limits.count+1 END
                    RETURNING count
                    """, authority.Player))
                    if ((int)(await rate.ExecuteScalarAsync())! > 5) throw new GameError(429, "chat-too-fast");
                byte[] chat = Packet(w => { w.Id(WireId(authority.Player)); w.Text(player.Name, 32); w.Text(command.Text, TownChat.MaximumLength); });
                long chatCursor = await AppendHostedEvent(c, t, authority.Host, null, 4, authority.Player, chat);
                await using (var evidence = Cmd(c, t, "INSERT INTO hosted_chat_evidence(host,cursor,actor,text) VALUES($1,$2,$3,$4)", authority.Host, chatCursor, authority.Player, command.Text)) await evidence.ExecuteNonQueryAsync();
                await using (var trim = Cmd(c, t, "DELETE FROM hosted_chat_evidence WHERE host=$1 AND (cursor<=$2 OR created<clock_timestamp()-interval '1 day')", authority.Host, chatCursor - 64)) await trim.ExecuteNonQueryAsync();
                reply = CommandResult(command, 200, chatCursor);
                break;
            case OnlineCommandKind.Mute:
                Guid muted = HostId(command.Entity);
                if (muted == authority.Player || muted == Guid.Empty || command.A is < 0 or > 1) throw new GameError(400, "invalid-mute");
                if (command.A == 0)
                { await using var remove = Cmd(c, t, "DELETE FROM hosted_mutes WHERE player=$1 AND muted=$2", authority.Player, muted); await remove.ExecuteNonQueryAsync(); }
                else
                {
                    await using (var count = Cmd(c, t, "SELECT count(*) FROM hosted_mutes WHERE player=$1", authority.Player))
                        if ((long)(await count.ExecuteScalarAsync())! >= 32) throw new GameError(409, "mute-list-full");
                    await using var add = Cmd(c, t, "INSERT INTO hosted_mutes(player,muted) SELECT $1,id FROM players WHERE id=$2 ON CONFLICT DO NOTHING", authority.Player, muted); await add.ExecuteNonQueryAsync();
                }
                reply = CommandResult(command, 200); break;
            case OnlineCommandKind.Report:
                Guid reported = HostId(command.Entity); string evidenceText;
                await using (var read = Cmd(c, t, """
                    SELECT text FROM hosted_chat_evidence e WHERE host=$1 AND cursor=$2 AND actor=$3
                    AND created>clock_timestamp()-interval '1 day'
                    AND e.cursor>(SELECT joined_cursor FROM hosted_actors WHERE host=$1 AND player=$4)
                    """, authority.Host, command.Revision, reported,authority.Player))
                    evidenceText = await read.ExecuteScalarAsync() as string ?? throw new GameError(404, "chat-evidence-expired");
                await using (var count = Cmd(c, t, "SELECT count(*) FROM reports WHERE reporter=$1 AND created>clock_timestamp()-interval '1 day'", authority.Player))
                    if ((long)(await count.ExecuteScalarAsync())! >= 5) throw new GameError(429, "too-many-reports");
                await using (var insert = Cmd(c, t, "INSERT INTO reports(id,reporter,reported,lines) VALUES($1,$2,$3,$4::jsonb)", id, authority.Player, reported, JsonSerializer.Serialize(new[] { evidenceText }))) await insert.ExecuteNonQueryAsync();
                await using (var trim = Cmd(c, t, "DELETE FROM reports WHERE created<clock_timestamp()-interval '7 days'")) await trim.ExecuteNonQueryAsync();
                reply = CommandResult(command, 200); break;
            case OnlineCommandKind.SendMail:
                await RequireOnlineInventory(c, t, authority.Device, authority.Host, authority.Visit);
                if (command.B != -1 || command.C != 0 || !JobLetterGrader.TryGrade(command.Text, false, out var grade)) throw new GameError(400, "online-mail-text-only");
                var recipient = new JobIdentity(command.Entity.First, command.Entity.Second);
                int npc = town.Town.Jobs.FindNpc(recipient), profile = town.Town.Jobs.FindPlayer(recipient);
                if (npc < 0 && ((uint)profile >= GameState.ProfileCount || !town.Profiles[profile].Created)) throw new GameError(403, "visited-town-residents-only");
                if (npc >= 0 && !town.NpcPresent(npc)) throw new GameError(403, "visited-town-residents-only");
                if ((uint)command.A >= player.Capacity || player.IsJobItem(command.A) || !Catalog.Has(player.Pockets[command.A], ItemFlags.Stationery)) throw new GameError(409, "stationery-required");
                await RequireUnboundOnlinePocket(c, t, authority.Player, command.A);
                await using (var count = Cmd(c, t, "SELECT count(*) FROM hosted_mail WHERE sender=$1 AND NOT replied", authority.Player))
                    if ((long)(await count.ExecuteScalarAsync())! >= 32) throw new GameError(409, "outbox-full");
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), due = now / 86400 * 86400 + 8 * 3600;
                if (due <= now) due += 86400;
                await using (var send = Cmd(c, t, """
                    INSERT INTO hosted_mail(id,host,sender,sender_identity,recipient_identity,recipient_profile,recipient_npc,body,due)
                    VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)
                    """, id, authority.Host, authority.Player, JobKey(player.Town.Jobs.Player(player.ActiveProfile)), JobKey(recipient),
                    npc < 0 ? profile : -1, npc, grade.NormalizedBody, due)) await send.ExecuteNonQueryAsync();
                player.Pockets[command.A] = 0; inventoryChanged = true;
                reply = CommandResult(command, 200, due); break;
            default: throw new GameError(403, "unsupported-online-action");
        }
        if (townChanged) await Save(c, t, authority.Host, town);
        if (inventoryChanged && (authority.Player != authority.Host || !townChanged)) await Save(c, t, authority.Player, player);
        if (townChanged) await AppendHostedEvent(c, t, authority.Host, null, 1, authority.Player, [1]);
        if (inventoryChanged) await AppendHostedEvent(c, t, authority.Host, authority.Player, 2, authority.Player,
            InventoryPacket(player, personal.Revision + 1));
        if (!query) await WritePlacementReceipt(c, t, authority.Player, id, fingerprint, new { wire = Convert.ToBase64String(reply) });
        await t.CommitAsync(); return new(reply, Pose: changedPose, World: townChanged ? town : null);
    }

    static HostedSeat ResolveHostedPortal(GameState town, HostedSeat actor, int building)
    {
        if (!BuildingWorld.Exists(town, building)) throw new GameError(409, "room-unavailable");
        float x, z, dx, dz;
        if (actor.Room == 0)
        {
            BuildingWorld.Entrance(town, building, out x, out z); dx = actor.X - x; dz = actor.Z - z;
            if (dx * dx + dz * dz > PortalSystem.PlayerEntryRadius * PortalSystem.PlayerEntryRadius || actor.Z < z - .25f ||
                !BuildingWorld.Segment(town, 0, actor.X, actor.Z, x, z)) throw new GameError(409, "door-out-of-reach");
            int room = BuildingWorld.Space(building); BuildingWorld.InsideDoor(town, room, out x, out z); z -= .7f;
            if (!BuildingWorld.CanStand(town, room, x, z)) throw new GameError(409, "door-blocked");
            return actor with { Room = room, X = x, Z = z };
        }
        if (BuildingWorld.Building(actor.Room) != building) throw new GameError(409, "wrong-room-door");
        BuildingWorld.InsideDoor(town, actor.Room, out x, out z); dx = actor.X - x; dz = actor.Z - z;
        if (dx * dx + dz * dz > PortalSystem.DoorRadius * PortalSystem.DoorRadius) throw new GameError(409, "door-out-of-reach");
        BuildingWorld.Entrance(town, building, out x, out z); z += 1;
        if (!BuildingWorld.CanStand(town, 0, x, z)) throw new GameError(409, "door-blocked");
        return actor with { Room = 0, X = x, Z = z };
    }
}
