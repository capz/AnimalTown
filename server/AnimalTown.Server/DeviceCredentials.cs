using System.Security.Cryptography;
using System.Text;
using Npgsql;

sealed record ApiAccess(bool Account, bool Device)
{
    public static readonly ApiAccess Public = new(false, false);
    public static readonly ApiAccess AccountOnly = new(true, false);
    public static readonly ApiAccess DeviceOnly = new(false, true);
    public static readonly ApiAccess AccountOrDevice = new(true, true);
}

sealed record DevicePrincipal(Guid Id, Guid Player, string Town, long Generation, string AccessHash);
record DeviceRenewRequest(Guid DeviceId, string RefreshToken, Guid RotationId);

sealed partial class GameDatabase
{
    const string AccessPrefix = "d2_";
    const string RefreshPrefix = "r2_";
    const int DeviceAccessSeconds = 600;

    // The original device proof or previous refresh token supplies the key.
    // Separate labels make access and refresh credentials independent.
    static string DeviceSecret(string prefix, string proof, string purpose)
    {
        string hex = proof.Length == 64 ? proof : proof[3..];
        byte[] key = Convert.FromHexString(hex);
        try
        {
            return prefix + Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("AnimalTown device v2/" + purpose)));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    static bool HasDeviceFormat(string? secret, string prefix) =>
        secret is { Length: 67 } && secret.StartsWith(prefix, StringComparison.Ordinal) && secret[3..].All(char.IsAsciiHexDigit);

    static object DeviceCredentials(Guid player, Guid deviceId, string town, long generation, string token, string refreshToken, int expiresIn) =>
        new { status = "approved", id = player, deviceId, town, generation, token, refreshToken, expiresIn };

    const string ValidDevice = """
        d.revoked_at IS NULL AND d.expires>clock_timestamp()
        AND NOT EXISTS (SELECT 1 FROM initial_town_imports WHERE player=d.player AND town_identity<>d.town_identity)
        """;

    public async Task<DevicePrincipal?> AuthenticateDevice(string accessToken)
    {
        if (!HasDeviceFormat(accessToken, AccessPrefix)) return null;
        string accessHash = Hash(accessToken);
        await using var connection = await source.OpenConnectionAsync();
        await using var command = Cmd(connection, null, """
            SELECT d.id,d.player,d.town_identity,d.generation FROM devices AS d
            WHERE d.access_hash=$1 AND d.access_expires>clock_timestamp() AND
            """ + " " + ValidDevice, accessHash);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new DevicePrincipal(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetInt64(3), accessHash)
            : null;
    }

    // Take this lock after all required account rows and before updating game
    // state. Revocation or rotation then cannot split authorization and commit.
    internal static async Task RequireActiveDevice(NpgsqlConnection connection, NpgsqlTransaction transaction, DevicePrincipal device)
    {
        await using var command = Cmd(connection, transaction, """
            SELECT d.id FROM devices AS d
            WHERE d.id=$1 AND d.player=$2 AND d.town_identity=$3 AND d.generation=$4 AND d.access_hash=$5
              AND d.access_expires>clock_timestamp() AND
            """ + " " + ValidDevice + " FOR UPDATE OF d", device.Id, device.Player, device.Town, device.Generation, device.AccessHash);
        if (await command.ExecuteScalarAsync() is null) throw new GameError(401, "device-authorization-expired");
    }

    public async Task<object> RenewDevice(DeviceRenewRequest request)
    {
        if (request.DeviceId == Guid.Empty || request.RotationId == Guid.Empty || !HasDeviceFormat(request.RefreshToken, RefreshPrefix))
            throw new GameError(400, "invalid-device-renewal");

        string refreshHash = Hash(request.RefreshToken);
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        Guid player;
        string town, currentHash;
        string? previousHash, rotationNonce;
        Guid? lastRotation;
        long generation;
        int seconds;
        await using (var command = Cmd(connection, transaction, """
            SELECT d.player,d.town_identity,d.generation,d.refresh_hash,d.previous_refresh_hash,d.last_rotation,d.rotation_nonce,
              greatest(0,floor(extract(epoch FROM d.access_expires-clock_timestamp())))::int
            FROM devices AS d WHERE d.id=$1 AND
            """ + " " + ValidDevice + " FOR UPDATE OF d", request.DeviceId))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new GameError(401, "invalid-device-credential");
            player = reader.GetGuid(0);
            town = reader.GetString(1);
            generation = reader.GetInt64(2);
            currentHash = reader.GetString(3);
            previousHash = reader.IsDBNull(4) ? null : reader.GetString(4);
            lastRotation = reader.IsDBNull(5) ? null : reader.GetGuid(5);
            rotationNonce = reader.IsDBNull(6) ? null : reader.GetString(6);
            seconds = reader.GetInt32(7);
        }

        bool retry = previousHash == refreshHash && lastRotation == request.RotationId && rotationNonce != null;
        if (!retry && currentHash != refreshHash) throw new GameError(401, "invalid-device-credential");
        if (!retry && lastRotation == request.RotationId) throw new GameError(409, "device-rotation-id-reused");

        string nonce = retry ? rotationNonce! : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string rotation = request.RotationId.ToString("N") + "/" + nonce;
        string refresh = DeviceSecret(RefreshPrefix, request.RefreshToken, "renew-refresh/" + rotation);
        string access = DeviceSecret(AccessPrefix, request.RefreshToken, "renew-access/" + rotation);
        if (retry) return DeviceCredentials(player, request.DeviceId, town, generation, access, refresh, seconds);

        await using var replace = Cmd(connection, transaction, """
            UPDATE devices SET previous_refresh_hash=refresh_hash,last_rotation=$2,refresh_hash=$3,access_hash=$4,
              generation=generation+1,access_expires=clock_timestamp()+make_interval(secs => $5),rotation_nonce=$6,
              expires=clock_timestamp()+interval '30 days'
            WHERE id=$1 RETURNING generation
            """, request.DeviceId, request.RotationId, Hash(refresh), Hash(access), (double)DeviceAccessSeconds, nonce);
        generation = (long)(await replace.ExecuteScalarAsync())!;
        await transaction.CommitAsync();
        return DeviceCredentials(player, request.DeviceId, town, generation, access, refresh, DeviceAccessSeconds);
    }

    public async Task<object> RevokeDevice(Guid player, Guid device)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var command = Cmd(connection, null, """
            UPDATE devices SET revoked_at=COALESCE(revoked_at,clock_timestamp()) WHERE id=$1 AND player=$2
            """, device, player);
        if (await command.ExecuteNonQueryAsync() != 1) throw new GameError(404, "device-unavailable");
        return new { ok = true };
    }

    public async Task<object> Devices(Guid player)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var command = Cmd(connection, null, """
            SELECT d.id,d.town_identity,d.expires FROM devices AS d WHERE d.player=$1 AND
            """ + " " + ValidDevice, player);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new { device = (object?)null };
        return new { device = new { id = reader.GetGuid(0), town = reader.GetString(1), expires = reader.GetDateTime(2) } };
    }
}
