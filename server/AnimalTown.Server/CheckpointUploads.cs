using System.Data;
using System.Globalization;
using AnimalTown;
using Npgsql;

sealed partial class GameDatabase
{
    const int CheckpointChunkBytes = 16 * 1024;
    const int CheckpointUploadSlots = 16;
    const int MinCheckpointBytes = (1 + SplitSaveCodec.PartCount) * (4 + 32);
    const long CheckpointAdmissionLock = 0x41544f574e55504c;
    static readonly TimeSpan CheckpointProgressTimeout = TimeSpan.FromSeconds(25);

    public async Task<CheckpointStatus> StartCheckpointUpload(DevicePrincipal device, CheckpointOffer offer, CancellationToken cancellation)
    {
        if (offer.Length is < MinCheckpointBytes or > SaveCodec.MaximumBytes ||
            offer.ParentRevision is <= 0 ||
            offer.Sha256 is not { Length: 64 } || !offer.Sha256.All(char.IsAsciiHexDigit))
            throw new GameError(400, "invalid-checkpoint-offer");
        string fingerprint = offer.Sha256.ToUpperInvariant();
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        // Serialize the global admission check before taking any account or device locks.
        await using (var quota = Cmd(connection, transaction, "SELECT pg_advisory_xact_lock($1)", CheckpointAdmissionLock))
            await quota.ExecuteNonQueryAsync(cancellation);
        await LockTown(connection, transaction, device.Player);
        await RequireActiveDevice(connection, transaction, device);
        await using (var cleanup = Cmd(connection, transaction, """
            DELETE FROM checkpoint_uploads
            WHERE expires<=clock_timestamp() OR (player=$1 AND device<>$2)
            """, device.Player, device.Id))
            await cleanup.ExecuteNonQueryAsync(cancellation);

        CheckpointStatus? previous = null;
        await using (var current = Cmd(connection, transaction, """
            SELECT id,total_bytes,received_bytes,revision,town_identity,fingerprint,parent_revision
            FROM checkpoint_uploads WHERE player=$1 FOR UPDATE
            """, device.Player))
        await using (var rows = await current.ExecuteReaderAsync(cancellation))
        {
            if (await rows.ReadAsync(cancellation))
            {
                previous = ReadCheckpointStatus(rows);
                long? previousParent = rows.IsDBNull(6) ? null : rows.GetInt64(6);
                if (previous.Length == offer.Length && rows.GetString(5) == fingerprint && previousParent == offer.ParentRevision)
                {
                    await rows.DisposeAsync();
                    await transaction.CommitAsync(cancellation);
                    return previous;
                }
                if (!previous.Completed || previous.Revision != offer.ParentRevision)
                    throw new GameError(409, "another-checkpoint-upload-pending");
            }
        }

        if (previous is not null)
        {
            await using var remove = Cmd(connection, transaction, "DELETE FROM checkpoint_uploads WHERE id=$1", previous.Id);
            await remove.ExecuteNonQueryAsync(cancellation);
        }

        await using (var quota = Cmd(connection, transaction,
            "SELECT count(*) FROM checkpoint_uploads WHERE revision IS NULL"))
            if ((long)(await quota.ExecuteScalarAsync(cancellation))! >= CheckpointUploadSlots)
                throw new GameError(429, "checkpoint-upload-capacity");

        Guid id = Guid.NewGuid();
        await using (var insert = Cmd(connection, transaction, """
            INSERT INTO checkpoint_uploads(id,player,device,town_identity,fingerprint,total_bytes,expires,parent_revision)
            VALUES($1,$2,$3,$4,$5,$6,clock_timestamp()+interval '30 minutes',nullif($7,0))
            """, id, device.Player, device.Id, device.Town, fingerprint, offer.Length, offer.ParentRevision ?? 0))
            await insert.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return new(id, offer.Length, 0, null, null);
    }

    public async Task<CheckpointStatus> CheckpointUploadStatus(DevicePrincipal device, Guid upload, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await RequireActiveDevice(connection, transaction, device);
        await using var command = FindCheckpointUpload(connection, transaction, device, upload, false);
        await using var rows = await command.ExecuteReaderAsync(cancellation);
        if (!await rows.ReadAsync(cancellation)) throw new GameError(404, "checkpoint-upload-not-found");
        return ReadCheckpointStatus(rows);
    }

    public async Task AbandonCheckpointUpload(DevicePrincipal device, Guid upload, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await RequireActiveDevice(connection, transaction, device);
        await using var command = Cmd(connection, transaction, """
            DELETE FROM checkpoint_uploads
            WHERE id=$1 AND player=$2 AND device=$3 AND town_identity=$4 AND revision IS NULL
            """, upload, device.Player, device.Id, device.Town);
        await command.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
    }

    public async Task<CheckpointStatus> StreamCheckpointUpload(DevicePrincipal device, Guid upload, int offset, HttpRequest request)
    {
        LimitCheckpointRequest(request, SaveCodec.MaximumBytes + 1);
        var cancellation = request.HttpContext.RequestAborted;
        Guid writer = Guid.NewGuid();
        var status = await ClaimCheckpointWriter(device, upload, offset, writer, request.ContentLength, cancellation);
        if (status.Completed) return status;

        try
        {
            // A truncated request commits only whole staging chunks. A client
            // resumes from the returned offset, not from its local byte count.
            byte[] bytes = new byte[Math.Min(CheckpointChunkBytes, Math.Max(1, status.Length - offset))];
            while (offset < status.Length)
            {
                int count = Math.Min(bytes.Length, status.Length - offset);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                deadline.CancelAfter(CheckpointProgressTimeout);
                await request.Body.ReadExactlyAsync(bytes.AsMemory(0, count), deadline.Token);
                if (offset + count == status.Length)
                    await RequireCheckpointEnd(request.Body, deadline.Token);

                byte[] chunk = count == bytes.Length ? bytes : bytes.AsSpan(0, count).ToArray();
                await WriteCheckpointChunk(device, upload, writer, offset, chunk, deadline.Token);
                offset += count;
            }

            if (status.Offset == status.Length)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                deadline.CancelAfter(CheckpointProgressTimeout);
                await RequireCheckpointEnd(request.Body, deadline.Token);
            }
            return await FinalizeCheckpointUpload(device, upload, writer, cancellation);
        }
        catch (EndOfStreamException)
        {
            throw new GameError(400, "checkpoint-incomplete");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new GameError(408, "checkpoint-progress-timeout");
        }
        finally
        {
            await ReleaseCheckpointWriter(upload, writer);
        }
    }

    static async Task RequireCheckpointEnd(Stream body, CancellationToken cancellation)
    {
        byte[] extra = new byte[1];
        if (await body.ReadAsync(extra.AsMemory(), cancellation) != 0)
            throw new GameError(400, "checkpoint-trailing-data");
    }

    async Task<CheckpointStatus> ClaimCheckpointWriter(DevicePrincipal device, Guid upload, int offset, Guid writer,
        long? bodyLength, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await RequireActiveDevice(connection, transaction, device);
        CheckpointStatus status;
        await using (var command = FindCheckpointUpload(connection, transaction, device, upload, true))
        await using (var rows = await command.ExecuteReaderAsync(cancellation))
        {
            if (!await rows.ReadAsync(cancellation)) throw new GameError(404, "checkpoint-upload-not-found");
            status = ReadCheckpointStatus(rows);
        }
        if (status.Completed) return status;
        if (offset != status.Offset) throw new GameError(409, "checkpoint-offset-changed");
        if (bodyLength is not null && bodyLength.Value > status.Length - offset)
            throw new GameError(400, "checkpoint-trailing-data");

        await using var claim = Cmd(connection, transaction, """
            UPDATE checkpoint_uploads SET writer=$2,writer_expires=clock_timestamp()+interval '30 seconds'
            WHERE id=$1 AND (writer IS NULL OR writer_expires<=clock_timestamp())
            """, upload, writer);
        if (await claim.ExecuteNonQueryAsync(cancellation) != 1)
            throw new GameError(409, "checkpoint-upload-busy");
        await transaction.CommitAsync(cancellation);
        return status;
    }

    async Task WriteCheckpointChunk(DevicePrincipal device, Guid upload, Guid writer, int offset, byte[] bytes, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using (var advance = Cmd(connection, transaction, """
            UPDATE checkpoint_uploads
            SET received_bytes=received_bytes+$5,writer_expires=clock_timestamp()+interval '30 seconds',
                expires=clock_timestamp()+interval '30 minutes'
            WHERE id=$1 AND writer=$2 AND player=$3 AND received_bytes=$4
                AND writer_expires>clock_timestamp() AND expires>clock_timestamp()
            """, upload, writer, device.Player, offset, bytes.Length))
            if (await advance.ExecuteNonQueryAsync(cancellation) != 1)
                throw new GameError(409, "checkpoint-offset-changed");
        await using (var insert = Cmd(connection, transaction, """
            INSERT INTO checkpoint_upload_chunks(upload,position,payload) VALUES($1,$2,$3)
            """, upload, offset, bytes))
            await insert.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
    }

    async Task<CheckpointStatus> FinalizeCheckpointUpload(DevicePrincipal device, Guid upload, Guid writer, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await LockTown(connection, transaction, device.Player);
        await RequireActiveDevice(connection, transaction, device);
        CheckpointStatus status;
        string fingerprint;
        long? parentRevision;
        await using (var command = FindCheckpointUpload(connection, transaction, device, upload, true))
        await using (var rows = await command.ExecuteReaderAsync(cancellation))
        {
            if (!await rows.ReadAsync(cancellation)) throw new GameError(404, "checkpoint-upload-not-found");
            status = ReadCheckpointStatus(rows);
            if (status.Completed) return status;
            if (status.Offset != status.Length || rows.IsDBNull(6) || rows.GetGuid(6) != writer)
                throw new GameError(409, "checkpoint-offset-changed");
            fingerprint = rows.GetString(5);
            parentRevision = rows.IsDBNull(7) ? null : rows.GetInt64(7);
        }

        try
        {
            ParsedInitialTown checkpoint;
            await using (var command = Cmd(connection, transaction, """
                SELECT position,payload FROM checkpoint_upload_chunks WHERE upload=$1 ORDER BY position
                """, upload))
            await using (var rows = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellation))
            await using (var body = new CheckpointChunkStream(rows))
                checkpoint = await ReadInitialTown(body, cancellation, fingerprint);
            if (checkpoint.State.Town.Jobs.TownId.ToString("X16", CultureInfo.InvariantCulture) != device.Town)
                throw new GameError(403, "device-town-mismatch");
            var receipt = parentRevision is long parent
                ? await CommitOfflineTown(connection, transaction, device.Player, parent, checkpoint, cancellation)
                : await CommitInitialTown(connection, transaction, device.Player, checkpoint);

            await using (var complete = Cmd(connection, transaction, """
                UPDATE checkpoint_uploads
                SET revision=$2,writer=NULL,writer_expires=NULL,expires=clock_timestamp()+interval '30 minutes'
                WHERE id=$1
                """, upload, receipt.Revision))
                await complete.ExecuteNonQueryAsync(cancellation);
            await using (var clear = Cmd(connection, transaction, "DELETE FROM checkpoint_upload_chunks WHERE upload=$1", upload))
                await clear.ExecuteNonQueryAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            return status with { Revision = receipt.Revision, Town = receipt.Town };
        }
        catch (GameError error) when (error.Status is >= 400 and < 500)
        {
            // Fully uploaded but rejected bytes cannot be repaired by appending.
            // Remove the staging atomically so the next valid offer can proceed.
            await using var clear = Cmd(connection, transaction, "DELETE FROM checkpoint_uploads WHERE id=$1", upload);
            await clear.ExecuteNonQueryAsync(cancellation);
            await transaction.CommitAsync(cancellation);
            throw;
        }
    }

    async Task ReleaseCheckpointWriter(Guid upload, Guid writer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await using var connection = await source.OpenConnectionAsync(timeout.Token);
            await using var release = Cmd(connection, null, """
                UPDATE checkpoint_uploads SET writer=NULL,writer_expires=NULL WHERE id=$1 AND writer=$2
                """, upload, writer);
            await release.ExecuteNonQueryAsync(timeout.Token);
        }
        catch (Exception error) when (error is NpgsqlException or OperationCanceledException)
        {
            // The stored lease expires even if the database is temporarily unreachable.
        }
    }

    static NpgsqlCommand FindCheckpointUpload(NpgsqlConnection connection, NpgsqlTransaction transaction,
        DevicePrincipal device, Guid upload, bool locked)
        => Cmd(connection, transaction, """
            SELECT id,total_bytes,received_bytes,revision,town_identity,fingerprint,
                CASE WHEN writer_expires>clock_timestamp() THEN writer ELSE NULL END,parent_revision
            FROM checkpoint_uploads
            WHERE id=$1 AND player=$2 AND device=$3 AND town_identity=$4 AND expires>clock_timestamp()
            """ + (locked ? " FOR UPDATE" : ""), upload, device.Player, device.Id, device.Town);

    static CheckpointStatus ReadCheckpointStatus(NpgsqlDataReader rows)
    {
        Guid id = rows.GetGuid(0);
        int length = rows.GetInt32(1), offset = rows.GetInt32(2);
        return rows.IsDBNull(3) ? new(id, length, offset, null, null)
            : new(id, length, offset, rows.GetInt64(3), rows.GetString(4));
    }

    sealed class CheckpointChunkStream(NpgsqlDataReader reader) : Stream
    {
        byte[] chunk = [];
        int offset;
        int position;

        public override async ValueTask<int> ReadAsync(Memory<byte> target, CancellationToken cancellationToken = default)
        {
            if (target.IsEmpty) return 0;
            if (offset == chunk.Length)
            {
                if (!await reader.ReadAsync(cancellationToken)) return 0;
                if (reader.GetInt32(0) != position) throw new InvalidOperationException("Noncontiguous checkpoint staging");
                chunk = reader.GetFieldValue<byte[]>(1);
                offset = 0;
            }
            int copied = Math.Min(target.Length, chunk.Length - offset);
            chunk.AsMemory(offset, copied).CopyTo(target);
            offset += copied;
            position += copied;
            return copied;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
