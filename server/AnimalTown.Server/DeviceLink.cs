using System.Globalization;
using System.Security.Cryptography;
using Npgsql;

record DeviceLinkStart(string Town);
record DeviceLinkPoll(string Secret);
record DeviceLinkApproval(string Code);

sealed partial class GameDatabase
{
    const int LinkLifetimeSeconds = 300;
    const int LinkPollSeconds = 5;

    public async Task<object> StartDeviceLink(DeviceLinkStart request)
    {
        if (request.Town is not { Length: 16 } || !request.Town.All(char.IsAsciiHexDigit) || request.Town == "0000000000000000")
            throw new GameError(400, "invalid-town-identity");
        string town = request.Town.ToUpperInvariant();
        await using var connection = await source.OpenConnectionAsync();
        // Bound the cleanup per request. An abandoned code cannot be redeemed.
        await using (var cleanup = Cmd(connection, null, """
            DELETE FROM device_links WHERE secret_hash IN
                (SELECT secret_hash FROM device_links WHERE expires<=now() ORDER BY expires LIMIT 32)
            """))
            await cleanup.ExecuteNonQueryAsync();

        string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            string code = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
            await using var command = Cmd(connection, null, """
                INSERT INTO device_links(secret_hash,user_code_hash,expires,town_identity)
                VALUES($1,$2,now()+make_interval(secs => $3),$4)
                ON CONFLICT(user_code_hash) DO NOTHING
                """, Hash(secret), Hash(code), (double)LinkLifetimeSeconds, town);
            if (await command.ExecuteNonQueryAsync() == 1)
                return new { secret, code, verificationPath = "/link", expiresIn = LinkLifetimeSeconds, interval = LinkPollSeconds };
        }
        throw new GameError(503, "link-code-unavailable");
    }

    public async Task<object> ApproveDeviceLink(Guid player, DeviceLinkApproval request)
    {
        if (request.Code is not { Length: 8 } || !request.Code.All(char.IsAsciiDigit))
            throw new GameError(400, "invalid-link-code");
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await LockTown(connection, transaction, player);
        string codeHash = Hash(request.Code);
        await using var command = Cmd(connection, transaction, """
            UPDATE device_links AS link SET player=$2
            WHERE user_code_hash=$1 AND expires>clock_timestamp() AND (player IS NULL OR player=$2)
              AND town_identity IS NOT NULL AND device IS NULL
              AND NOT EXISTS (SELECT 1 FROM initial_town_imports WHERE player=$2 AND town_identity<>link.town_identity)
            """, codeHash, player);
        if (await command.ExecuteNonQueryAsync() != 1)
        {
            await using var issued = Cmd(connection, transaction, """
                SELECT 1 FROM device_links WHERE user_code_hash=$1 AND player=$2 AND device IS NOT NULL AND expires>clock_timestamp()
                """, codeHash, player);
            if (await issued.ExecuteScalarAsync() is int) return new { ok = true };
            throw new GameError(404, "link-code-unavailable");
        }
        await using var expirePending = Cmd(connection, transaction, """
            UPDATE device_links SET expires=clock_timestamp() WHERE player=$1 AND device IS NULL AND user_code_hash<>$2
            """, player, codeHash);
        await expirePending.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return new { ok = true };
    }

    public async Task<object> PollDeviceLink(DeviceLinkPoll request)
    {
        if (request.Secret is not { Length: 64 } || !request.Secret.All(char.IsAsciiHexDigit))
            throw new GameError(400, "invalid-link-secret");
        string secret = request.Secret.ToUpperInvariant();
        string secretHash = Hash(secret);
        await using var connection = await source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var lookup = Cmd(connection, transaction, """
            SELECT player FROM device_links WHERE secret_hash=$1 AND expires>clock_timestamp() AND town_identity IS NOT NULL
            """, secretHash);
        object? approval = await lookup.ExecuteScalarAsync();
        if (approval is null) throw new GameError(404, "link-expired");
        if (approval is not Guid player) return new { status = "pending" };

        await LockTown(connection, transaction, player);
        Guid? issued;
        string town;
        await using (var command = Cmd(connection, transaction, """
            SELECT device,town_identity FROM device_links AS link
            WHERE secret_hash=$1 AND player=$2 AND expires>clock_timestamp()
              AND NOT EXISTS (SELECT 1 FROM initial_town_imports WHERE player=$2 AND town_identity<>link.town_identity)
            FOR UPDATE
            """, secretHash, player))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) throw new GameError(404, "link-expired");
            issued = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            town = reader.GetString(1);
        }

        // Reconstruct the first result for a repeated poll while the original
        // grant is current. Persist only hashes and keep old codes from relinking.
        string refresh = DeviceSecret(RefreshPrefix, secret, "link-refresh");
        string access = DeviceSecret(AccessPrefix, secret, "link-access");
        if (issued is Guid existing)
        {
            await using var status = Cmd(connection, transaction, """
                SELECT floor(extract(epoch FROM access_expires-clock_timestamp()))::int FROM devices
                WHERE id=$1 AND revoked_at IS NULL AND generation=1 AND expires>clock_timestamp()
                """, existing);
            if (await status.ExecuteScalarAsync() is not int seconds)
                throw new GameError(404, "link-expired");
            return DeviceCredentials(player, existing, town, 1, access, refresh, Math.Max(0, seconds));
        }

        Guid device = Guid.NewGuid();
        await using (var supersede = Cmd(connection, transaction, "UPDATE devices SET revoked_at=clock_timestamp() WHERE player=$1 AND revoked_at IS NULL", player))
            await supersede.ExecuteNonQueryAsync();
        await using (var insert = Cmd(connection, transaction, """
            INSERT INTO devices(id,player,town_identity,access_hash,refresh_hash,access_expires,expires)
            VALUES($1,$2,$3,$4,$5,clock_timestamp()+make_interval(secs => $6),clock_timestamp()+interval '30 days')
            """, device, player, town, Hash(access), Hash(refresh), (double)DeviceAccessSeconds))
            await insert.ExecuteNonQueryAsync();
        await using (var mark = Cmd(connection, transaction, "UPDATE device_links SET device=$2 WHERE secret_hash=$1", secretHash, device))
            await mark.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return DeviceCredentials(player, device, town, 1, access, refresh, DeviceAccessSeconds);
    }
}

static class DeviceLinkPage
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Link AnimalTown</title>
          <script src="/link.js" defer></script>
        </head>
        <body>
          <h1>Link AnimalTown</h1>
          <p>Sign in and enter the eight-digit code shown on your GameCube.</p>
          <form id="link" method="post">
            <p><label>Account name <input name="name" required maxlength="16" autocomplete="username"></label></p>
            <p><label>Password <input name="password" type="password" required maxlength="128" autocomplete="current-password"></label></p>
            <p><label>GameCube code <input name="code" required inputmode="numeric" pattern="[0-9]{8}" maxlength="8" autocomplete="one-time-code"></label></p>
            <button type="submit">Link GameCube</button>
          </form>
          <p id="status" role="status"></p>
        </body>
        </html>
        """;

    public const string Script = """
        const form = document.getElementById("link");
        const status = document.getElementById("status");
        const submit = form.querySelector("button");
        form.addEventListener("submit", async event => {
          event.preventDefault();
          submit.disabled = true;
          status.textContent = "";
          try {
            const fields = new FormData(form);
            const login = await fetch("/v1/auth/token", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({ name: fields.get("name"), password: fields.get("password") })
            });
            if (!login.ok) {
              status.textContent = login.status === 401
                ? "Check your account name and password."
                : "Sign-in is temporarily unavailable. Try again.";
              return;
            }
            const { token } = await login.json();
            const approval = await fetch("/v1/account/link", {
              method: "POST",
              headers: { "Content-Type": "application/json", Authorization: "Bearer " + token },
              body: JSON.stringify({ code: fields.get("code") })
            });
            if (approval.ok) {
              form.reset();
              form.hidden = true;
              status.textContent = "GameCube linked. Return to AnimalTown.";
            } else {
              status.textContent = approval.status === 404
                ? "Check the code or request a new one on your GameCube."
                : "Linking is temporarily unavailable. Try again.";
            }
          } catch {
            status.textContent = "Couldn't reach AnimalTown. Try again.";
          } finally {
            submit.disabled = false;
          }
        });
        """;
}
