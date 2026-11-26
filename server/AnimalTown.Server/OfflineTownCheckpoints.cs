using Npgsql;

sealed partial class GameDatabase
{
    async Task<InitialTownReceipt> CommitOfflineTown(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid owner, long parent, ParsedInitialTown upload, CancellationToken cancellation)
    {
        // The caller holds the owner and device locks. Restrict this path to
        // straight-line offline progress over the last shared checkpoint.
        string town;
        long head;
        await using (var command = Cmd(connection, transaction, """
            SELECT original.town_identity,coalesce(latest.revision,original.revision),
                   latest.parent_revision,latest.fingerprint
            FROM initial_town_imports original LEFT JOIN offline_town_checkpoints latest ON latest.player=original.player
            WHERE original.player=$1
            """, owner))
        await using (var rows = await command.ExecuteReaderAsync(cancellation))
        {
            if (!await rows.ReadAsync(cancellation)) throw new GameError(409, "town-checkpoint-required");
            town = rows.GetString(0);
            head = rows.GetInt64(1);
            if (!rows.IsDBNull(2) && rows.GetInt64(2) == parent && rows.GetString(3) == upload.Fingerprint)
                return new(town, head);
        }

        if (head != parent)
            throw new GameError(409, "reconciliation-required");
        await using (var check = Cmd(connection, transaction, """
            SELECT (SELECT revision FROM players WHERE id=$1)=$2
                AND NOT EXISTS(SELECT 1 FROM gates WHERE host=$1 AND expires>clock_timestamp())
                AND NOT EXISTS(SELECT 1 FROM visits WHERE (host=$1 OR guest=$1) AND NOT ended AND expires>clock_timestamp())
                AND NOT EXISTS(SELECT 1 FROM guest_placements WHERE host=$1 OR guest=$1)
                AND NOT EXISTS(SELECT 1 FROM town_review_leases WHERE host=$1 AND expires>clock_timestamp())
                AND NOT EXISTS(SELECT 1 FROM operations WHERE player=$1)
                AND NOT EXISTS(SELECT 1 FROM listings WHERE seller=$1 OR buyer=$1)
                AND NOT EXISTS(SELECT 1 FROM grants WHERE player=$1)
            """, owner, parent))
        {
            if (await check.ExecuteScalarAsync(cancellation) is not true)
                throw new GameError(409, "reconciliation-required");
        }

        var current = await Load(connection, transaction, owner, false);
        if (upload.State.Sequence <= current.State.Sequence)
            throw new GameError(409, "offline-checkpoint-not-newer");

        long revision = checked(head + 1);
        await using (var advance = Cmd(connection, transaction, """
            INSERT INTO offline_town_checkpoints(player,parent_revision,revision,fingerprint,save_commit,save_records)
            VALUES($1,$2,$3,$4,$5,$6)
            ON CONFLICT(player) DO UPDATE SET
                parent_revision=excluded.parent_revision,revision=excluded.revision,fingerprint=excluded.fingerprint,
                save_commit=excluded.save_commit,save_records=excluded.save_records
            """, owner, parent, revision, upload.Fingerprint, upload.Commit, upload.Records))
            await advance.ExecuteNonQueryAsync(cancellation);
        await WriteParts(connection, transaction, owner, upload.State, true);
        return new(town, revision);
    }
}
