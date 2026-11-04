# Split persistence

Storage now consists of four independent, versioned records. This changes
physical memory-card files and database storage, not just in-memory classes.

| Record | Contents |
| --- | --- |
| Town data | Seed, calendar/rollover state, terrain, elevation, ground objects, computer-villager names and walking positions |
| House positions | Player house placement flag and plot coordinates; each computer villager's fixed home anchor |
| Shop states | Shop stock, stock count and spending used for shop progression |
| Player villager | Character name, catchphrase, species, palette, clothing, equipment, outdoor position, backpack/items, money, savings/debt, mail, tutorial/jobs, resident relationships/requests, house/garden upgrades, pending building work, house appearance and placed furnishings |

House positions describe shared world placement; house customization, contents
and progression belong to the player record. Current gameplay has one playable
resident per town. This change does not add multiple player profiles or visits.
Shop records persist currently implemented shop state; no new shop simulation
or opening-hours rules are implied.

## Memory card

Two banks contain `AT-Town0/1`, `AT-Houses0/1`, `AT-Shops0/1` and `AT-Player0/1`.
`AT-Commit0/1` commits the matching bank. Every file retains the AnimalTown title,
commit identity and memory-card banner/icon. Ten directory entries are used once
both banks exist, plus the SDK's temporary transaction files while writing.
Every entry is rounded to the GameCube card's block size and has its own artwork;
splitting therefore costs more card space than the old two-file layout.

All record headers contain magic `ATSP`, schema 1, catalog version, record kind,
save sequence, town seed and exact byte length. A trailing CRC-32 covers the
header and payload. The commit record lists all four record lengths and payload
checksums; identities and sequences must also match. Records have bounded counts,
RLE terrain/elevation and reuse existing full-state semantic validation.

Saving writes all four records to the inactive bank, reads them back, validates
the complete state, then writes the commit record last and verifies that bank.
Loading selects the newest complete valid bank. Missing, truncated, corrupt or
mixed records cause fallback to the other complete bank. Parts are never selected
individually across banks. A full card or failed write reports failure and keeps
the previous committed bank available. An error after a successful final write
can report failure although that new bank is recoverable on reload.

The old `AnimalTown0/1` v1-v5 files remain readable through the existing migrations
and are never overwritten or automatically deleted. The next successful save
creates split records. Keeping the original migration files requires spare card
space. No complete recoverable bank or legacy save means existing damaged files
are preserved rather than silently replaced with a new town.

The combined v5 format remains the compatibility export/import format and test
validator. It is no longer the normal console/backend persistence payload.

## Backend

PostgreSQL stores `town_data`, `house_positions`, `shop_states` and
`player_villager` separately. Registration inserts one complete set. Updates change
the four payloads and revision in the existing transaction alongside inventory,
escrow and grant changes. A database constraint rejects partial sets.

Startup migrates old `players.state` rows one locked row per transaction,
preserving state, credentials, player ID and revision. Only after the complete
split set is stored does the old combined column become NULL. Interrupted
migration can be restarted. Normal reads use the four columns; the authenticated
owner's existing town-export endpoint continues to return compatible v5 bytes.

Checks cover exact round trips, record ownership, CRC/truncation/wrong-kind errors,
mixed towns/sequences, same-sequence commit mismatch, failure at each of five
writes, bank fallback, migration and backend transactional constraints.
