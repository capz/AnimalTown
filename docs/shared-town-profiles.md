# Four villagers, one town

Start opens a four-card [graphical villager picker](villager-picker.md) before game assets load. A continues
with that villager; B returns to the title. Empty slots start character creation
in the existing town. Each session plays one villager; offline profiles are not
additional simulated actors. Creation and selection do not autosave: use the
in-game Save action to commit the shared town and every profile.

The town shares its terrain, calendar, shop stock/spending, NPC names/locations/
journeys, building definitions, dropped/planted objects and placed furnishings.
Each villager has independent identity, appearance, position/interior, inventory,
mail, ownership/catalog flags, money/savings/debt, tutorial/jobs, NPC friendships/
requests, house/garden upgrades, pending construction and home appearance.
Calendar rollover updates all created profiles once, including offline mail,
interest and pending construction; shared shop stock rolls over once per town.

Each profile owns a separate, non-overlapping home plot and interior spaces.
Everyone sees the homes and gardens, collides with them and can visit another
villager's home. Placing furnishings is restricted to the current villager's
home/garden; moving a home moves only that owner's outdoor furnishings. Dropped
items record the active profile for Lost & Found protection. Existing NPC
ownership IDs remain unchanged.

## Memory card and migration

A committed bank contains seven data files and one manifest:

| Item | Bank 0 / bank 1 filenames |
| --- | --- |
| Shared town, NPCs, world objects and furnishings | `AT-Town0` / `AT-Town1` |
| All home plots and NPC home anchors | `AT-Houses0` / `AT-Houses1` |
| Shared shop state | `AT-Shops0` / `AT-Shops1` |
| Villager slot 1 | `AT-Player0` / `AT-Player1` |
| Villager slot 2 | `AT-Villager2-0` / `AT-Villager2-1` |
| Villager slot 3 | `AT-Villager3-0` / `AT-Villager3-1` |
| Villager slot 4 | `AT-Villager4-0` / `AT-Villager4-1` |
| Complete-bank manifest | `AT-Commit0` / `AT-Commit1` |

The trailing bank number is a backup generation, not a villager number. Both
banks use 16 directory entries after two saves, plus the separate `dev config`
item and temporary SDK transaction files. Every file keeps AnimalTown's banner,
icon and build identity; each is rounded to the card's block size.

Split schema 4 gives every profile its own checksummed, versioned record. All
seven records must have the same town seed and sequence; the manifest binds
their lengths and CRCs. Saving writes and verifies the inactive bank before
publishing its manifest. Loading chooses the newest complete valid bank and
falls back as a whole if any profile or shared record is missing or corrupt.
Readback shares terrain arrays read-only; the small-town allocation regression
budget is 256 KiB for all four profiles.

Split schemas 1–3 and combined schemas 1–7 remain readable. The original player
becomes slot 1 and the other three slots are empty, preserving the existing
terrain, belongings, relationships and home. Combined schema 8 exports the
complete town and all profiles. Explicit legacy encoders are for single-player
compatibility fixtures and cannot discard a populated multi-profile town.
Existing combined `AnimalTown0/1` migration files are preserved.

## Backend

The existing authenticated account retains one town with four local profiles.
`town_data`, `house_positions` and `shop_states` remain shared; `player_villager`
and `player_villager2/3/4` store the four residents separately. Database checks
reject partial rosters. Old combined rows migrate at startup; old split rows
remain readable and expand on their next transactional write. Export includes
all profiles. This does not add remote account sharing or simultaneous players.
