# Town shop prototype

The town's existing shop entrance now opens a dedicated 8 × 8 metre interior.
The original low-poly kit has a striped canopy, shop windows, an acorn sign,
a central stock island, wall sale stands, an essentials cabinet and a service counter. C-stick
turns the cutaway camera; L recentres it. Walk up to a fixture and press A when
its prompt appears. Doorway cues and entry/exit use the existing portal system.

* **Daily displays:** six, nine or twelve stocked positions around one central
  island, following the existing shop spending tiers. Furniture/decor is a physical one-item purchase. Buying
  removes the object, leaves a sold placard and prevents a second purchase.
  Displays replenish at the existing daily rollover.
* **Essentials cabinet:** a list of peaches, wildflowers, pine saplings, the
  starter work shirt, fishing rod, bug net, shovel and axe. These remain in stock
  after every purchase. Furniture, money bags, quest objects and the retired
  watering can are excluded. Each purchase requires payment and pocket space.
* **Villager sales:** three, five or seven green wall stands shared by the four local playable
  villagers. An empty stand opens pocket selection and a 1–99,999 acorn price
  editor. Left/right chooses the price increment, up/down adjusts it, A confirms
  and B backs out. Sellers can change their price or take an unsold item back.
  Other buyers can purchase it once. Proceeds go to the exact seller's savings;
  if their character card is absent, the town keeps a bounded earnings ledger
  until that character returns. Deleting a character also deletes their listings.
  New stands appear with the existing overnight shop upgrades. Slots keep their
  positions: the first three are on the left, right and back walls; upgrades add
  paired left/right stands farther down the room. Inactive stands are neither
  drawn nor collidable. Existing excess listings are retained off sale; their
  owner can use **Reclaim stored sale items** at the counter to take them back.
* **Counter:** existing selling, tutorial, daily jobs, backpack and expansion
  services remain available. No ordinary NPC-stock revenue is awarded to a
  playable villager.

| Shop tier | Daily island stock | Villager sale slots |
| --- | ---: | ---: |
| Small | 6 | 3 |
| Middle (25,000 total shop spending) | 9 | 5 |
| Large (100,000 total shop spending) | 12 | 7 |

The island has one solid collision footprint and a clear aisle around all four
sides. Every stock position has a reachable approach on its nearest edge; actors
cannot walk through the island to reach a product. The cabinet and service
counter remain against the back wall.

NPCs include shop visits in their existing home/neighbour journeys. They walk
to an affordable stocked display, browse, pay, retain the purchase and leave.
Each has a persistent wallet (initially 6,000 acorns), a daily 1,000-acorn budget
top-up capped at 20,000, and sixteen item slots. Autonomous shopping is limited
to one successful purchase per day and stops when pockets are full. Their logic
continues while the player browses shop menus. Failed purchases change nothing.

The same domain transaction accepts a separate guest buyer's money/pockets.
The backend exposes authenticated `GET /v1/towns/{host}/shop` and idempotent
`POST /v1/towns/{host}/shop/ops`. A guest requires an active permitted visit and
may only buy; local villagers may list, remove and reprice. Both accounts and
stock commit in one database transaction. Global stock revisions reject stale
selections, and ordered account locks give concurrent finite purchases exactly
one winner. Cabinet purchases never deplete stock. Live guest transport/rendering
is not implemented in the current console game; these endpoints provide the
transaction integration for that work, without pretending the gate is connected.

## Assets and resource budget

`assets/definitions/buildings/town_shop.json` selects stable type 1. Its room
definition lives in `assets/definitions/interiors/town_shop.json`. Exterior and
door GLBs are under `assets/models/buildings/town_shop`; cutaway room parts are
under `assets/models/interiors/town_shop`; the shared 128px authoring palette is
under `assets/textures/buildings/town_shop`. Regenerate this original prototype
with `dotnet run --project tools/AnimalTown.Tools -- shop-generate ROOT`.

`ShopLayout` supplies model island/stand positions, fixture collision, interaction
approaches and NPC destinations. Host tools cook the same bounded definition
used by other buildings. Static parts share the existing indexed retention path,
64px cottage / 32px flat shop palette policy and 768 KiB combined building ceiling. Products are
small batched instances of the existing cube mesh, with category placeholders
for non-furniture consignment; there is no model/texture allocation per product.
Room meshes stay within the existing 600-triangle per-part and 800-triangle
exterior limits. Actors/stock outside the indoor camera are culled. The island
is baked once into the floor model; active wall stands reuse the native box
batch, without allocating another mesh or material per tier or per stand.
The host native fixture enforces the unchanged 786,432-byte combined-kit ceiling
(host pointers are wider than the console's) and 256 KiB per-part load/compaction budget. The
independent unpatched SDK loader check validates packages separately; the
retained check enforces the actual aggregate runtime ceiling.

The extra shop record remains 154 explicit 32-bit words (616 bytes), including
the original eight listing cells so older excess items can be reclaimed. Full saves use
schema 9; split and incremental saves append a bounded extension to the existing
checksummed shop record, without adding a memory-card item. Older saves without
the extension keep their terrain, possessions and sold slots. The old cottage
shop becomes the new shop, and remaining essential stock moves to decor displays.
Legacy export refuses to discard listings, earnings or purchased NPC inventory.

## Verification

`ShopChecks` in the buildings suite covers shared-stock depletion, repeated
cabinet purchases, local/guest/NPC wallets, failed-operation atomicity, seller
ownership, absent-card earnings, both save formats and real NPC routes to all
all active fixtures in all three tiers, including selection hit tests, absent
stand collision and the solid island. Backend integration races two customers for one display and checks
one winner, retry safety and unlimited essentials. The focused Dolphin scenario
is `tools/iso-smoke.py ISO OUTPUT --shop --dsp-hle --season summer`, using a
disposable card from `--shop-fixture` and `tools/profile-card-fixture.py`.
The fixture accepts `--shop-fixture OUTPUT --tier 0`, `1` or `2` to inspect the
small, middle and large shop layouts with deterministic NPC purchase targets.
It exercises the actual native shop, purchases, cabinet, listing/price/removal,
NPC shopping, camera and explicit save. `--shop-load` checks the saved result
after reboot. L+R+C-stick down+A inspects the shop entrance outdoors, then cycles its
interaction sockets indoors. Physical GameCube testing remains outstanding.
