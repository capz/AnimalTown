using System.Buffers.Binary;
using System.Security.Cryptography;
using AnimalTown;
using Npgsql;

sealed record OnlineReservationRequest(Guid Id, Guid Host, Guid Visit, long Revision, string Fingerprint, int Profile);
sealed record OnlinePublicationAck(string Fingerprint);

static class OnlineSettlementRoutes
{
    internal static void MapOnlineSettlement(this WebApplication app, GameDatabase database)
    {
        DevicePrincipal Device(HttpContext context) => (DevicePrincipal)context.Items["device"]!;
        var routes = app.MapGroup("/v1/online/reservations").WithMetadata(ApiAccess.DeviceOnly);
        routes.MapPost("", (HttpContext c, OnlineReservationRequest r) => database.ReserveOnlineCard(Device(c), r));
        routes.MapGet("/{id:guid}", (HttpContext c, Guid id) => database.OnlineCardStatus(Device(c), id));
        routes.MapPost("/{id:guid}/activate", (HttpContext c, Guid id) => database.ActivateOnlineCard(Device(c), id));
        routes.MapPost("/{id:guid}/settlement", (HttpContext c, Guid id) => database.PrepareOnlinePublication(Device(c), id));
        routes.MapGet("/{id:guid}/checkpoint", async (HttpContext c, Guid id, int offset, int count) =>
            Results.Bytes(await database.ReadOnlinePublication(Device(c), id, offset, count), "application/octet-stream"));
        routes.MapPost("/{id:guid}/published", (HttpContext c, Guid id, OnlinePublicationAck ack) => database.AcknowledgeOnlinePublication(Device(c), id, ack));
    }
}

sealed partial class GameDatabase
{
    static byte[] CheckpointEnvelope(byte[] commit, byte[][] records)
    {
        int length = 4 + commit.Length;
        foreach (var record in records) length = checked(length + 4 + record.Length);
        if (length > SaveCodec.MaximumBytes) throw new GameError(413, "save-too-large");
        byte[] bytes = new byte[length]; int at = 0;
        void Part(byte[] value) { BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at, 4), value.Length); at += 4; value.CopyTo(bytes, at); at += value.Length; }
        Part(commit); foreach (var record in records) Part(record);
        return bytes;
    }

    static async Task RequireOnlineInventory(NpgsqlConnection c, NpgsqlTransaction t, DevicePrincipal device, Guid host, Guid visit)
    {
        await using var read = Cmd(c, t, """
            SELECT EXISTS(SELECT 1 FROM online_travel WHERE player=$1 AND device=$2 AND town_identity=$3
              AND host=$4 AND visit=$5 AND phase=1)
            """, device.Player, device.Id, device.Town, host, visit);
        if (await read.ExecuteScalarAsync() is not true) throw new GameError(409, "card-reservation-required");
    }

    async Task EnsureOnlineCardHead(NpgsqlConnection c, NpgsqlTransaction t, Guid player)
    {
        await using var init = Cmd(c, t, """
            INSERT INTO online_card_heads(player,revision,fingerprint,save_commit,save_records)
            SELECT i.player,coalesce(o.revision,i.revision),coalesce(o.fingerprint,i.fingerprint),
              coalesce(o.save_commit,i.save_commit),coalesce(o.save_records,i.save_records)
            FROM initial_town_imports i LEFT JOIN offline_town_checkpoints o ON o.player=i.player WHERE i.player=$1
            ON CONFLICT DO NOTHING
            """, player);
        await init.ExecuteNonQueryAsync();
    }

    public async Task<object> ReserveOnlineCard(DevicePrincipal device, OnlineReservationRequest request)
    {
        if (request.Id == Guid.Empty || request.Host == Guid.Empty || request.Revision < 1 ||
            (uint)request.Profile >= GameState.ProfileCount || request.Fingerprint is null ||
            request.Fingerprint.Length != 64 || !request.Fingerprint.All(char.IsAsciiHexDigit) ||
            (request.Host == device.Player) != (request.Visit == Guid.Empty))
            throw new GameError(400, "invalid-card-reservation");
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        if (device.Player == request.Host) await LockTown(c, t, device.Player);
        else await LockVisitParticipants(c, t, request.Host, device.Player);
        await RequireActiveDevice(c, t, device);
        string fingerprint = Hash("online-reservation:" + System.Text.Json.JsonSerializer.Serialize(request));
        if (await ReadPlacementReceipt(c, t, device.Player, request.Id, fingerprint) is { } prior) return prior;
        if (device.Player != request.Host) await RequirePlacementVisit(c, t, request.Host, device.Player, request.Visit);
        var state = (await Load(c, t, device.Player, false)).State;
        if (ReviewDeviceVillager(state, device).Length == 0 || state.ActiveProfile != request.Profile || state.ProfileLocked(request.Profile))
            throw new GameError(409, "selected-villager-changed");
        await EnsureOnlineCardHead(c, t, device.Player);
        await using (var read = Cmd(c, t, "SELECT revision,fingerprint FROM online_card_heads WHERE player=$1", device.Player))
        await using (var row = await read.ExecuteReaderAsync())
            if (!await row.ReadAsync() || row.GetInt64(0) != request.Revision || row.GetString(1) != request.Fingerprint.ToUpperInvariant())
                throw new GameError(409, "card-lineage-changed");
        await using (var create = Cmd(c, t, """
            INSERT INTO online_travel(id,player,device,town_identity,profile,host,visit,parent_revision,fingerprint)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)
            """, request.Id, device.Player, device.Id, device.Town, request.Profile, request.Host, request.Visit,
            request.Revision, request.Fingerprint.ToUpperInvariant())) await create.ExecuteNonQueryAsync();
        var result = new { id = request.Id, revision = request.Revision, phase = "reserved", town = device.Town };
        await WritePlacementReceipt(c, t, device.Player, request.Id, fingerprint, result);
        await t.CommitAsync(); return result;
    }

    public async Task<object> ActivateOnlineCard(DevicePrincipal device, Guid id)
    {
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, device.Player); await RequireActiveDevice(c, t, device);
        await using var update = Cmd(c, t, """
            UPDATE online_travel SET phase=1 WHERE id=$1 AND player=$2 AND device=$3 AND town_identity=$4 AND phase IN (0,1)
            """, id, device.Player, device.Id, device.Town);
        if (await update.ExecuteNonQueryAsync() != 1) throw new GameError(409, "card-reservation-ended");
        await t.CommitAsync(); return new { id, phase = "active" };
    }

    public async Task<object> OnlineCardStatus(DevicePrincipal device, Guid id)
    {
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        await RequireActiveDevice(c, t, device);
        await using var read = Cmd(c, t, """
            SELECT phase,parent_revision,revision,publication_fingerprint,save_commit,save_records,profile
            FROM online_travel WHERE id=$1 AND player=$2 AND town_identity=$3
            """, id, device.Player, device.Town);
        await using var row = await read.ExecuteReaderAsync();
        if (!await row.ReadAsync()) throw new GameError(404, "card-reservation-not-found");
        int phase = row.GetInt32(0), length = 0;
        if (!row.IsDBNull(4)) length = CheckpointEnvelope(row.GetFieldValue<byte[]>(4), row.GetFieldValue<byte[][]>(5)).Length;
        return new { id, phase, parentRevision = row.GetInt64(1), revision = row.IsDBNull(2) ? (long?)null : row.GetInt64(2),
            sha256 = row.IsDBNull(3) ? null : row.GetString(3), length, profile = row.GetInt32(6) };
    }

    public async Task<object> PrepareOnlinePublication(DevicePrincipal device, Guid id)
    {
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, device.Player); await RequireActiveDevice(c, t, device);
        int phase, profile; long parent;
        await using (var read = Cmd(c, t, "SELECT phase,parent_revision,profile FROM online_travel WHERE id=$1 AND player=$2 AND town_identity=$3", id, device.Player, device.Town))
        await using (var row = await read.ExecuteReaderAsync())
        {
            if (!await row.ReadAsync()) throw new GameError(404, "card-reservation-not-found");
            phase = row.GetInt32(0); parent = row.GetInt64(1); profile = row.GetInt32(2);
        }
        if (phase < 2)
        {
            if (await ReviewActive(c, t, device.Player)) throw new GameError(409, "finish-review-first");
            await using (var pending = Cmd(c, t, "SELECT EXISTS(SELECT 1 FROM guest_placements WHERE host=$1)", device.Player))
                if (await pending.ExecuteScalarAsync() is true) throw new GameError(409, "placement-review-required");
            var state = (await Load(c, t, device.Player, false)).State;
            if (state.ActiveProfile != profile) throw new GameError(409, "selected-villager-changed");
            int sequence;
            await using (var head = Cmd(c, t, "SELECT save_commit,save_records FROM online_card_heads WHERE player=$1 AND revision=$2", device.Player, parent))
            await using (var row = await head.ExecuteReaderAsync())
            {
                if (!await row.ReadAsync()) throw new GameError(409, "card-lineage-changed");
                sequence = SplitSaveCodec.DecodeCommitted(row.GetFieldValue<byte[]>(0), row.GetFieldValue<byte[][]>(1)).Sequence;
            }
            // Publication follows the actual card sequence, independently of
            // the backend's mutation/revision counter.
            state.Sequence = checked(sequence + 1);
            byte[][] records = SplitSaveCodec.Encode(state); byte[] commit = SplitSaveCodec.EncodeCommit(state, records);
            string hash = Convert.ToHexString(SHA256.HashData(CheckpointEnvelope(commit, records)));
            await using var update = Cmd(c, t, """
                UPDATE online_travel SET phase=2,revision=$3,publication_fingerprint=$4,save_commit=$5,save_records=$6
                WHERE id=$1 AND player=$2
                """, id, device.Player, checked(parent + 1), hash, commit, records);
            await update.ExecuteNonQueryAsync();
        }
        await t.CommitAsync(); return await OnlineCardStatus(device, id);
    }

    public async Task<byte[]> ReadOnlinePublication(DevicePrincipal device, Guid id, int offset, int count)
    {
        if (offset < 0 || count is < 1 or > 16384) throw new GameError(400, "invalid-checkpoint-page");
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        await RequireActiveDevice(c, t, device);
        await using var read = Cmd(c, t, "SELECT save_commit,save_records FROM online_travel WHERE id=$1 AND player=$2 AND town_identity=$3 AND phase>=2", id, device.Player, device.Town);
        await using var row = await read.ExecuteReaderAsync();
        if (!await row.ReadAsync()) throw new GameError(409, "publication-not-ready");
        byte[] envelope = CheckpointEnvelope(row.GetFieldValue<byte[]>(0), row.GetFieldValue<byte[][]>(1));
        if (offset > envelope.Length) throw new GameError(400, "invalid-checkpoint-page");
        return envelope.AsSpan(offset, Math.Min(count, envelope.Length - offset)).ToArray();
    }

    public async Task<object> AcknowledgeOnlinePublication(DevicePrincipal device, Guid id, OnlinePublicationAck ack)
    {
        await using var c = await source.OpenConnectionAsync(); await using var t = await c.BeginTransactionAsync();
        await LockTown(c, t, device.Player); await RequireActiveDevice(c, t, device);
        int phase; long revision; string fingerprint; byte[] commit; byte[][] records;
        await using (var read = Cmd(c, t, "SELECT phase,revision,publication_fingerprint,save_commit,save_records FROM online_travel WHERE id=$1 AND player=$2 AND town_identity=$3 AND phase>=2", id, device.Player, device.Town))
        await using (var row = await read.ExecuteReaderAsync())
        {
            if (!await row.ReadAsync()) throw new GameError(409, "publication-not-ready");
            phase = row.GetInt32(0); revision = row.GetInt64(1); fingerprint = row.GetString(2);
            commit = row.GetFieldValue<byte[]>(3); records = row.GetFieldValue<byte[][]>(4);
        }
        if (ack.Fingerprint != fingerprint) throw new GameError(409, "publication-fingerprint-mismatch");
        if (phase == 2)
        {
            await using (var update = Cmd(c, t, "UPDATE online_card_heads SET revision=$2,fingerprint=$3,save_commit=$4,save_records=$5 WHERE player=$1 AND revision=$2-1", device.Player, revision, fingerprint, commit, records))
                if (await update.ExecuteNonQueryAsync() != 1) throw new GameError(409, "card-lineage-changed");
            await using (var update = Cmd(c, t, "UPDATE online_travel SET phase=3 WHERE id=$1", id)) await update.ExecuteNonQueryAsync();
        }
        await t.CommitAsync(); return new { id, revision, phase = "complete", sha256 = fingerprint };
    }
}
