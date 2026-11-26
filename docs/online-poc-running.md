# Running the online play PoC backend

This setup runs on a local computer with Docker and PowerShell 7. The host
runtime alternative also needs the pinned .NET SDK. The backend computer must
stay on for unattended guests. Console UI and physical transport qualification
remain separate gates in [the PoC backlog](online-play-poc.md).

Run from the repository root:

```powershell
./tools/online-poc.ps1
./tools/online-poc.ps1 -Action Status
```

This starts persistent PostgreSQL, the backend, and the pinned free Cloudflare
Quick Tunnel. No domain, account, router forwarding, or inbound workspace port
is required. The origin binds to loopback; clients use the printed HTTPS origin
and `/v1/gameplay` WSS route. The temporary origin is also recorded in
`out/online/online-origin.txt`. Use `-EnablePlay` only for a PoC candidate whose
card reservation and return lifecycle is being exercised. Without that option,
gameplay mutation commands remain disabled.

`out/online/private.env` contains the generated database password. Keep it and
the database volume across restarts. Files under `out/` are ignored by Git.
Backups contain account, device and town records and should remain private.
The development stack in `build/online.compose.yml` uses persistent storage;
the integration-test stack is disposable and is not a substitute.

Where Docker cannot obtain the .NET image, install `cloudflared` and run:

```powershell
./tools/online-poc.ps1 -HostRuntime -Cloudflared cloudflared
```

Keep this terminal open. It runs the backend in the foreground and stops its
child tunnel on exit. PostgreSQL still runs in Docker on loopback port 55440.
For isolated protocol checks use `-NoTunnel`; a successful loopback check does
not qualify public HTTPS/WSS. Docker hosting runs detached. Stop it with
`./tools/online-poc.ps1 -Action Stop`; this retains the town-data volume.

Cloudflare names change when a Quick Tunnel is replaced. Explicitly configure
the new origin on both ISO and SD candidates and relink through its `/link`
page. Stored credentials are bound to their exact origin. Preserve the home
card and pending travel/publication records; never erase them to get past a
changed endpoint. Reconnecting to an unchanged origin uses a fresh single-use
gameplay ticket and the current epoch, then replay or a fresh recipient snapshot.

Make a backup and verify it without replacing the running database:

```powershell
./tools/online-poc.ps1 -Action Backup
./tools/online-poc.ps1 -Action Restore -BackupFile out/online/town-TIMESTAMP.dump -RestoreDatabase animaltown_restore_check
```

Restore creates a new empty database and refuses an existing name. If a restore
fails, retain the backup and use a new restore name for the next attempt. To
select verified restored data, stop the backend, set `ONLINE_POC_DATABASE` in
`out/online/private.env` to that restore name, and start it again. Retain the
original database until the restored town and credentials have been checked.

The repository's checks start their own database and service. Run
`tools/test-changes.ps1` for normal validation. The online fixture uses three
actual device-linked clients, bounded WSS messages, public/private projections,
command receipts, and staged return checkpoints. The native fixture compiles
the actual SDK connection path and executes portable framing with GC stress.
Neither constitutes a Cloudflare edge or DOL-015 measurement. Record those
results against the exact source, SDK and release artifact hashes before
closing the corresponding acceptance gates.
