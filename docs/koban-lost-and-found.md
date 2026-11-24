# Koban and visitor item custody

The town has an original enterable koban: a compact rounded police box, slate
roof, red beacon, teal door, warm service counter and lost-property shelves.
The approved direction combines the small GameCube-era building silhouette
with softer contemporary materials. The kit reuses the existing building,
door, room, camera and menu systems; it adds no resident actor.

Actual source-model renders, geometry budgets and reproduction commands are in
[the asset preview](screenshots/koban/README.md).

## Playing

The blue koban marker appears on both town maps. Enter through its front door,
then press A near the counter or shelves to browse lost property. Town Hall's
old lost-and-found entry now directs players to the koban.

Any active human resident save slot in this town can collect an item with A.
Visitors can inspect the list, but cannot claim it. NPC actors cannot claim it.
Lost property is shared by the town's local human residents and does not expire.

A visitor removal carries a durable recovery ID. A local resident can press X
on that entry to restore the original ground object or furniture placement.
Restoration retains its item, owner, wrapping, room, rotation and original
position. It fails without changing either item if the destination is occupied.
Claiming consumes the recovery opportunity; retrying cannot create a second
copy. Harvested and caught yields have no removed original entity, so undo
discards that unclaimed yield instead of recreating a tree or animal.

## Permission rules

| Action | Local human resident | Visitor |
| --- | --- | --- |
| Buy from the shop | Purchase enters pockets | Purchase enters pockets and travels home |
| Add an ordinary pocket item | Debits own pocket | Debits visitor's pocket |
| Move or rotate a world item | Validated destination | Validated destination |
| Remove an object | Goes to custody | Goes to custody, with a recovery ID |
| Pick up or dig a world item | Normal pocket acquisition | Goes to custody |
| Harvest fruit or catch an animal | Normal pocket acquisition | Goes to custody |
| Claim or restore lost property | Allowed for the active local resident | Denied |

Shop purchases remain committed and never enter the recovery ledger. Existing
possessions brought from home and the sealed parcels used by memory-card travel
keep their own provenance. Destination-issued ordinary rewards use town
custody; they cannot turn a visitor interaction into an item export.

Custody shares the existing 512-entry ground store. A failed admission leaves
the source object and inventory intact. Custody entries are excluded from
world lookup and house-plot obstruction checks. A separate bounded extension
stores at most 512 recovery records and a monotonic ID counter; consumed IDs
are never reused. If an owed destination reward cannot fit in that store, its
item stays in the existing bounded reward ledger as explicit deferred custody.
It appears in the same local-resident claim list. This preserves the reward
without trapping a visitor in a full town; any money still owed travels home
independently. Home-issued obligations retain their original provenance.

## Save compatibility

Building instance 12 and room 26 are added without renumbering any existing
home, shop or town-hall space. The art definition is type 2. New combined saves
use schema 16; the shared split-town record uses schema 11. Profile, economy,
house and incremental manifest versions are unchanged. The embedded offline
job ledger advances to schema 13 for explicit deferred custody. Existing combined
schemas 4–15 and split-town schemas remain readable, including the historical
snow and job fixtures.

New towns place the koban at (40, 25). When an older save already uses that plot,
migration selects a nearby clear, level site and persists the chosen coordinates.
It never repaints saved terrain or moves existing belongings. The footprint is
a service overlay. An imported town with no clear site can still load with the
building unavailable instead of losing possessions. Legacy single-profile
export rejects live recovery state that its older schema cannot preserve.

The recovery extension belongs to the static town record. Incremental saves
continue to separate NPC/runtime changes from terrain, inventories and economy;
an unchanged save remains a no-op.

## Backend contract

All requests use the existing authenticated account, friendship/blocking and
visit checks. Roles are derived by the server, never from a submitted actor or
resident flag.

| Endpoint | Purpose |
| --- | --- |
| `GET /v1/towns/{host}/items` | Read editable world entries and host revision |
| `GET /v1/towns/{host}/lost-found` | Read custody and recovery IDs as a local resident |
| `POST /v1/towns/{host}/items/ops` | Apply `add`, `move`, `remove`, `claim` or `restore` |

Each operation has a unique `id` and expected host `revision`. Visitor `add`
also supplies `playerRevision`, protecting the source pocket from stale-index
substitution. Target `source` is `ground`, `placed` or `lost-found`; restore
uses `recoveryId`. Reusing an operation ID with different content is rejected.
The server locks participating accounts in the existing order and commits both
saves and the idempotency receipt in one transaction.

These endpoints provide authoritative item transactions. They do not add a
realtime movement stream, NAT traversal, relay transport or session-wide owner
review. The implemented undo covers still-unclaimed visitor removals; it is
not a rollback of every addition, movement or whole online visit. Those broader
networking and review lifecycles require a separate change.

The remaining work is indexed in the
[Codex networking implementation brief](networking-implementation-plan.md).
It specifies the additional journal, dependency reservations and owner review
needed to preserve undo rights throughout a live visit. Those are planned
extensions; the current recovery behavior above remains the implemented scope.

## Verification

Focused checks cover building identity, entry/exit, fixture collision, safe old
town migration, save schemas and custody permissions. Custody tests exercise
full pockets/storage, wrong resident slots, NPC/visitor denial, blocked restore
destinations, replay prevention and combined/split/incremental persistence.
PostgreSQL integration covers real account authorization, stale revisions,
duplicate operations, concurrent claims and purchases surviving service restart.
The production building importer and native loader enforce per-part and total
resident memory limits. Recovery metadata is allocated only when receipts exist;
empty towns retain the original ground-item layout. The native offline-job
stress test runs both normal and forced collection within the unchanged 6 MiB
managed heap, including two maximum economies and post-travel world buffers.
Source renders are not GameCube hardware captures.
