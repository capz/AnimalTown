# Implementation status

This is an early playable implementation, **not the completed game** described in
the GDD. The technical baseline was committed/pushed first as `0064efd`.

## Implemented foundation

* Pinned hypercube-engine and DolphinDotNet submodules, separate console and
  ASP.NET Core/PostgreSQL server builds; shared AOT-compatible gameplay assembly.
* Full-size bootable GameCube ISO, original homebrew apploader, FST, game title,
  commit in disc header/BNR/title/save comment, disc banner and save banner/icon.
* Eight supplied GLBs converted into textured native models, six species selection.
  The supplied guest tent is also converted and packaged without eagerly loading
  it; the shop interior is in source under assets/models/interiors. See structure-assets.md.
  The five non-fox villagers use the supplied ~1,600-triangle replacements;
  only the fox currently has walking/running clips. See villager-assets.md for
  source mappings and missing rigs/clips. True cylindrical terrain with an
  east-west axis and radius 360/(2*pi), square 8×8 screen-tile world, conservative
  frustum/horizon visibility and up to 32 shared generated low-poly trees (60–84 triangles each), now explicitly authorized by the user. Original tree GLBs remain reference sources and are excluded from the ISO.
  Objects inherit the world root and use the cylinder's radial surface normal;
  terrain extends beyond the viewport. Speech balloons and all UI panels use
  large rounded corners, with pill selections and rounded keyboard keys.
  Gorditas Bold titles/headings use offline-baked IA8; all other UI reads the
  console's native IPL font through SDK PR #145. No firmware font bytes ship.
* Developer-authored seasonal repeating floors use crops from the supplied transition sheets, with mirrored repeat, load-time downsizing to at most 64 pixels, CMPR/RGB565 mipmaps and merged curved strips. Current floor textures occupy 13.5 KiB per season. UI/IPL remain paged at 128 pixels, with source-keyed caching. The camera is one third closer, with frustum/cylinder-horizon rejection and GX backface culling. Opt-in checksummed frame telemetry separates CPU phases and presentation waits. See render-performance.md.
  See generated-trees-repeat-floor.md for current sources, budgets and authoring; wood/indoor-floor materials use color fallbacks where no artwork is supplied.
* Controller navigation, character/name/catchphrase creation, house reservation,
  backpack icon grid (10/20/30 slots), selected-item popovers, shop capacity
  upgrades and context actions, map, store buying/selling, banking, loans and tier
  rules, catalog deliveries, simple tutorial objectives and villager fetch jobs.
* Indoor/outdoor footprint validation, placement ownership, rotating footprints,
  garden reservations and relocation, tier-dependent room size.
* Native RTC bridge, 06:00 day rollover, stock and monthly interest rules,
  dropped-item sweep and Lost & Found owner/retention checks.
* Schema 4 binary saves record the 8×8 world dimensions; old towns crop to the
  northwest retained region, with home/furniture relocation and Lost & Found
  recovery for outlying possessions. See town-size.md. Versioned saves have run-length terrain compression and legacy schema
  migration, CRC and bounded decoding, alternating card files with
  readback verification. Both damaged saves are preserved rather than replaced.
* Server registration/password hashing/opaque sessions, owner authorization,
  friends/blocks, gate leases and visit reservations, serialized inventory
  operations with idempotency keys, escrow listings, atomic purchase and grants.

## Validation performed

* 2448 host checks: safe house arrival and trapped-save repair, backpack tiers/capacity persistence, cylindrical radius and radial normals, viewport/tile sizing,
  square world bounds, seasonal material selection and stable repeat variants, nine-slice coordinates, wallet/capacity atomicity, bank overflow, money bags, stock,
  expansion/rollover, interest replay, CRC rejection, lost-item protection and
  connectivity of all mandatory areas for 20 seeds.
* Independent server publish on .NET 10; real PostgreSQL integration checks for
  authentication, save ownership, operation retries/body mismatch, rejected
  operation rollback, mutual friends, blocking, escrow/purchase retry and grant
  claim retry. These are not yet simultaneous-buyer race tests.
* Native card-reader regression verifies sector-aligned reads, prefixes, sector boundaries, length clamping and cleanup after failures. The project overlay fixes the pinned SDK reader. SDK IPL-font PR #145 passes its framework CI checks.
* Terrain importer checks verify variant crops, GX RGB565 pixel order, duplicated
  gutters, nine-slice order, source confinement/opacity, patch bounds and atlas
  capacity. Dolphin custom-RTC runs render spring and winter ground; the bounded
  fall terrain passes backpack/controller navigation and card readback saving.
* Native PowerPC AOT/link succeeds. Disc validator checks full image size, boot
  header/apploader, DOL entry/sections/BSS, FST ranges/alignment and required BNR.
* Direct ISO boot in Dolphin reaches the rendered title, character creation,
  house placement, walking world, map and backpack with injected controller input
  using the actual supplied models. A card save passed readback, a second ISO boot loaded the same town/backpack and wrote the alternate save. The raw card directory advertises the RGB5A3 banner and its stored pixels match the build asset. No separately booted DOL is involved.

Physical GCLoader, Swiss and burned-disc testing has not been performed. BBA,
memory-card removal/full/corruption tests and PAL timing need target validation.
The pipeline's structural checks do not replace those tests. Native SDK metrics
do not include all heap/framebuffer residency; hardware memory budgets remain
unverified. Model normalization uses bind bounds; animated extremities need QA.

## Remaining work (release blockers)

* Online console transport, companion/gateway, signed tickets, heartbeat, four
  players, reliable intent/delta replay, movement interpolation and reconnect.
  Current visit endpoint only reserves a session; it does not start multiplayer.
* Server cloud PUT/reconciliation deliberately returns an explicit unsupported
  error. Preventing edited offline assets from entering server economy requires
  a complete journal/import policy. House/garden/profile writes, gifts/rewards,
  tents and moderation restrictions still need endpoints and integration.
* Full resident schedules/pathfinding, personality dialogue, all six request
  types, lost-object quests and proper tutorial objective verification.
* Fishing/net timing games, fruit-regrowth limits, watering/growth state, weeds,
  slingshot, seasonal palettes, weather, soundscape and accessibility.
* Distinct indoor rooms/stairways for upper floor/basement/annex, full furniture
  visuals/editor, delayed paid exterior customization, mailbox/stall interactions
  and clay-chime rhythms. Current exterior menus are immediate palette values;
  several visuals are placeholders and five authored house models do not exist.
* Player chat, phrase wheel/emotes, chat history/filter/mute/block/report UI,
  voice customization and species-specific clothes/tool sockets.
* LOD asset reduction and bounded streaming across full town, animated-instance
  culling, frame-time telemetry and on-hardware memory/performance profiling.
* Full playthrough, extended controller/save recovery verification, concurrency and
  save recovery tests, clean-release reproducibility and hardware smoke matrix.

Do not interpret a menu entry, domain rule, or server route as a completed
end-to-end gameplay feature. This list must shrink against tested behavior.
