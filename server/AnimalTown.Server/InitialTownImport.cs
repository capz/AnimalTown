using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using AnimalTown;
using Microsoft.AspNetCore.Http.Features;
using Npgsql;

sealed partial class GameDatabase
{
    sealed record ParsedInitialTown(GameState State, byte[] Commit, byte[][] Records, string Fingerprint);

    static void LimitCheckpointRequest(HttpRequest request, int budget)
    {
        if (!string.Equals(request.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            throw new GameError(415, "binary-save-required");
        if (request.ContentLength > budget) throw new GameError(413, "save-too-large");
        var limit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = budget;
    }

    // One native split-save commit followed by its seven records. Each field
    // is prefixed by its unsigned 32-bit length in little-endian order.
    static async Task<ParsedInitialTown> ReadInitialTown(Stream body, CancellationToken cancellation, string? expectedFingerprint = null)
    {
        const int budget = SaveCodec.MaximumBytes;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] prefix = new byte[4];
        int remaining = budget;
        async Task<byte[]> Record(int maximum)
        {
            await body.ReadExactlyAsync(prefix, cancellation);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
            remaining -= prefix.Length;
            if (length < 32 || length > maximum || length > remaining)
                throw new GameError(400, "invalid-save-record-length");
            byte[] record = new byte[(int)length];
            await body.ReadExactlyAsync(record, cancellation);
            remaining -= record.Length;
            hash.AppendData(prefix);
            hash.AppendData(record);
            return record;
        }

        try
        {
            byte[] commit = await Record(128);
            byte[][] parts = new byte[SplitSaveCodec.PartCount][];
            for (int i = 0; i < parts.Length; i++) parts[i] = await Record(budget);
            if (await body.ReadAsync(prefix.AsMemory(0, 1), cancellation) != 0)
                throw new GameError(400, "trailing-save-data");
            string fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            if (expectedFingerprint is not null && fingerprint != expectedFingerprint)
                throw new GameError(400, "checkpoint-fingerprint-mismatch");
            GameState state = SplitSaveCodec.DecodeCommitted(commit, parts);
            RequireLocalPlayer(state);
            if (!state.Town.Jobs.Initialized || state.Sequence == int.MaxValue)
                throw new GameError(400, "invalid-save-identity");
            return new(state, commit, parts, fingerprint);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidOperationException or OverflowException)
        {
            throw new GameError(400, "invalid-save");
        }
    }

    sealed record InitialTownReceipt(string Town, long Revision);

    public async Task<object> ImportInitialTown(Guid owner, HttpRequest request)
    {
        LimitCheckpointRequest(request, SaveCodec.MaximumBytes);
        var upload = await ReadInitialTown(request.Body, request.HttpContext.RequestAborted);
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var result = await CommitInitialTown(connection, transaction, owner, upload);
        await transaction.CommitAsync();
        return result;
    }

    async Task<InitialTownReceipt> CommitInitialTown(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid owner, ParsedInitialTown upload)
    {
        var (state, commit, records, fingerprint) = upload;
        string identity = state.Town.Jobs.TownId.ToString("X16", CultureInfo.InvariantCulture);
        await using(var issuer=Cmd(connection,transaction,"SELECT EXISTS(SELECT 1 FROM hosted_identity WHERE issuer=$1)",identity))
            if(await issuer.ExecuteScalarAsync() is true)throw new GameError(409,"town-identity-reserved");
        await using(var retired=Cmd(connection,transaction,"SELECT EXISTS(SELECT 1 FROM retired_online_towns WHERE town_identity=$1)",identity))
            if(await retired.ExecuteScalarAsync() is true)throw new GameError(409,"town-identity-retired");
        var current = await Load(connection, transaction, owner, true);

        await using (var command = Cmd(connection, transaction,
            "SELECT fingerprint,revision FROM initial_town_imports WHERE player=$1", owner))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                if (reader.GetString(0) != fingerprint)
                    throw new GameError(409, "town-already-imported");
                return new(identity, reader.GetInt64(1));
            }
        }

        await using (var command = Cmd(connection, transaction, """
            SELECT EXISTS(SELECT 1 FROM gates WHERE host=$1 AND expires>now())
                OR EXISTS(SELECT 1 FROM visits WHERE guest=$1 AND NOT ended AND expires>now())
            """, owner))
        {
            if (current.Revision != 0 || await command.ExecuteScalarAsync() is true)
                throw new GameError(409, "reconciliation-required");
        }

        long revision = current.Revision + 1;
        await using (var command = Cmd(connection, transaction, """
            INSERT INTO initial_town_imports(player,town_identity,fingerprint,revision,save_commit,save_records)
            VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(town_identity) DO NOTHING
            """, owner, identity, fingerprint, revision, commit, records))
        {
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new GameError(409, "town-already-linked");
        }
        await WriteParts(connection, transaction, owner, state, true);
        await using(var reopen=Cmd(connection,transaction,"DELETE FROM online_town_closures WHERE player=$1",owner))await reopen.ExecuteNonQueryAsync();
        return new(identity, revision);
    }
}
