# Running the online play PoC backend

This setup runs on a local computer with Docker and PowerShell 7. Source-mode
host runtime needs the pinned .NET SDK; the published backend ZIP needs only
the .NET 10 ASP.NET runtime. The backend computer must stay on for unattended
guests. Dolphin and physical transport qualification remain separate gates in
[the PoC backlog](online-play-poc.md).

The release's compiled backend ZIP includes `online-poc.ps1`,
`online-account.ps1` and a runtime-only Compose file. Extract the complete ZIP
and run `./online-poc.ps1 -EnablePlay` there. It runs that exact compiled backend;
no source checkout or compiler is needed. Its settings/backups live in
`AnimalTown-online-data` beside the extracted directory and persist across
bundle replacement. Source-checkout commands below use `tools/` and `out/online/`.

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

Create each PoC account, then establish mutual friends using the same public
origin. The helper prompts for its password without saving it or displaying
account tokens:

```powershell
./tools/online-account.ps1 -Origin https://YOUR-SERVICE -Name Mira -Action Register
./tools/online-account.ps1 -Origin https://YOUR-SERVICE -Name Mira -Action AddFriend -Target OTHER-ACCOUNT-ID
./tools/online-account.ps1 -Origin https://YOUR-SERVICE -Name Mira -Action Friends
```

Registration prints the account ID. Each friend must add the other's ID. Link
each selected home card through the console and the service's `/link` page.
The same helper supports `RemoveFriend`, `Block`, `Unblock`, `Devices` and
`RevokeDevice`; `-Target` is the relevant account or device ID. In the published
bundle, omit `tools/` from these commands. Do not expose the loopback origin
directly or put passwords/tokens in command history.

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
Use `-EnablePlay` for authorized PoC gameplay in either hosting mode. Optional
`-Port` and `-DatabasePort` select initial loopback ports; existing installations
keep their recorded values in `private.env`.

Cloudflare names change when a Quick Tunnel is replaced. Explicitly configure
the new origin on both ISO and SD candidates and relink through its `/link`
page. Stored credentials are bound to their exact origin. Preserve the home
card and pending travel/publication records; never erase them to get past a
changed endpoint. The approved console flow stores the service hostname on the
selected card, for both disc and SD installations. Use **Link account / service**
to change it. During a pending return, choose **Change service**, then reopen the
same town and relink the same account. The new service must prove the same town,
reservation, profile, revision and fingerprint before the recovery journal can
be rebound. A different or reset backend cannot unlock the old reservation.
Reconnecting to an unchanged origin uses a fresh single-use
gameplay ticket and the current epoch, then replay or a fresh recipient snapshot.

After offline profile changes or a completed two-card resident move, use **Sync
town** and review each changed identity beside the current world. The stick
inspects the world; the D-pad selects a review action. Assign a removed player's
new online earnings to a surviving resident. A full mailbox or reward ledger
keeps the review pending: free capacity on the card and retry. The completed
two-card move stays completed.

Deleting the last player or erasing a linked town from title **Options** first
opens the same closure preview. Cancelling keeps the town. Confirmation closes
admission, then waits for guest returns, placement review, outstanding mail and
verified card publication. If closure cannot finish, keep the card and retry;
do not remove its online recovery files. Whole-town erasure also retires and
archives the old backend town before local deletion. A power interruption can
resume that retirement/erase, and a new town can later link to the same account
without reusing the retired town's identity or receipts.

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
The public-route workflow also checks the shipped CA bundle against the actual
edge certificate. Neither backend clients nor native CPU fixtures constitute
a GameCube or DOL-015 measurement. Record those
results against the exact source, SDK and release artifact hashes before
closing the corresponding acceptance gates.
