using AnimalTown;
using AnimalTown.Online;
using Npgsql;

sealed partial class GameDatabase
{
    async Task<InitialTownReceipt> CommitOfflineTown(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid owner, long parent, ParsedInitialTown upload, CancellationToken cancellation)
    {
        await EnsureOnlineCardHead(connection,transaction,owner);
        string town;long head;string fingerprint;GameState baseline;
        await using(var read=Cmd(connection,transaction,"""
            SELECT i.town_identity,h.revision,h.fingerprint,h.save_commit,h.save_records
            FROM online_card_heads h JOIN initial_town_imports i ON i.player=h.player WHERE h.player=$1
            """,owner))
        await using(var row=await read.ExecuteReaderAsync(cancellation))
        {
            if(!await row.ReadAsync(cancellation))throw new GameError(409,"town-checkpoint-required");
            town=row.GetString(0);head=row.GetInt64(1);fingerprint=row.GetString(2);
            baseline=SplitSaveCodec.DecodeCommitted(row.GetFieldValue<byte[]>(3),row.GetFieldValue<byte[][]>(4));
        }
        if(parent!=head)throw new GameError(409,"card-lineage-changed");
        if(upload.Fingerprint==fingerprint)return new(town,head);
        if(upload.State.Sequence<=baseline.Sequence)throw new GameError(409,"offline-checkpoint-not-newer");
        await using(var active=Cmd(connection,transaction,"""
            SELECT EXISTS(SELECT 1 FROM online_travel WHERE player=$1 AND phase<3)
              OR EXISTS(SELECT 1 FROM town_review_leases WHERE host=$1 AND expires>clock_timestamp())
            """,owner))
            if(await active.ExecuteScalarAsync(cancellation) is true)throw new GameError(409,"finish-online-return-first");
        var current=await Load(connection,transaction,owner,false);
        if(OnlineResidentDecisions.Changes(upload.State,baseline)!=0)
        {
            await StageResidentReview(connection,transaction,owner,parent,upload);
            return new(town,head);
        }
        try { OnlineTownMerge.Apply(upload.State,baseline,current.State,DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
        catch(InvalidOperationException){throw new GameError(409,"offline-reconciliation-required");}
        await AcceptOfflineCheckpoint(connection,transaction,owner,parent,upload,cancellation);
        return new(town,checked(head+1));
    }

    async Task AcceptOfflineCheckpoint(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid owner,long parent,ParsedInitialTown upload,CancellationToken cancellation)
    {
        long head=parent;
        long revision=checked(head+1);
        // The upload itself is already on the card. Remember its exact bytes as
        // the new acknowledged baseline; the merged candidate is canonical and
        // must still be downloaded, published and read back before its own ack.
        await using(var advance=Cmd(connection,transaction,"""
            INSERT INTO offline_town_checkpoints(player,parent_revision,revision,fingerprint,save_commit,save_records)
            VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(player) DO UPDATE SET
            parent_revision=excluded.parent_revision,revision=excluded.revision,fingerprint=excluded.fingerprint,
            save_commit=excluded.save_commit,save_records=excluded.save_records
            """,owner,parent,revision,upload.Fingerprint,upload.Commit,upload.Records))await advance.ExecuteNonQueryAsync(cancellation);
        await using(var advance=Cmd(connection,transaction,"UPDATE online_card_heads SET revision=$2,fingerprint=$3,save_commit=$4,save_records=$5 WHERE player=$1",owner,revision,upload.Fingerprint,upload.Commit,upload.Records))await advance.ExecuteNonQueryAsync(cancellation);
        await using(var calendar=Cmd(connection,transaction,"""
            INSERT INTO hosted_personal_calendar(player,day,month) VALUES($1,$2,$3)
            ON CONFLICT(player) DO UPDATE SET day=excluded.day,month=excluded.month
            """,owner,upload.State.Town.Day,upload.State.Town.Month))await calendar.ExecuteNonQueryAsync(cancellation);
        await WriteParts(connection,transaction,owner,upload.State,true);
        await using(var clear=Cmd(connection,transaction,"DELETE FROM offline_resident_reviews WHERE player=$1",owner))await clear.ExecuteNonQueryAsync(cancellation);
        await using(var hosted=Cmd(connection,transaction,"SELECT EXISTS(SELECT 1 FROM hosted_towns WHERE host=$1)",owner))
            if(await hosted.ExecuteScalarAsync(cancellation) is true)await AppendHostedEvent(connection,transaction,owner,null,1,null,[1]);
    }
}
