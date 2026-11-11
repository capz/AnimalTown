# AnimalTown / Project Meadowroll — GameCube technical design

The [town generation implementation](town-generation.md) documents the researched
GameCube acre approach, our flat 8x8 adaptation, seed/save behavior and terrain-kit
requirements for future two-/three-tier towns.

Status: implementation baseline, 2026-11-02. Product requirements: `design-doc.md`.
Revision: cylinder/world/bubble requirements updated from the user's instruction.
This document supersedes that document's Unity, PC, shader, WebRTC and SignalR
implementation assumptions. None of those technologies run in the game.

2026-11-03 implementation updates: [render-performance.md](render-performance.md)
specifies the closer camera, configurable texture residency/cache and culling;
[villager-assets.md](villager-assets.md) records the replacement species, their
lower-poly sources and current animation limitations. These supersede the
original asset inventory, camera calibration and terrain submission budgets below.
The later [generated-trees-repeat-floor.md](generated-trees-repeat-floor.md)
update supersedes supplied-tree rendering and the ground-atlas renderer with
authorized generated low-poly trees and repeating, load-time optimized textures.

## Targets and separate builds

`game/AnimalTown.GameCube` is a C# net8 assembly compiled by DolphinDotNet to
native PowerPC code and linked with libogc2. It references Hypercube.Core and
Hypercube.GameCube from pinned NuGet packages. `shared/AnimalTown.Domain` contains
bounded gameplay rules usable by both AOT and desktop .NET. No ASP.NET, EF,
reflection-based serializer, dynamic code generation or desktop dependency enters
the console dependency graph.

`server/AnimalTown.Server` is a separately published ASP.NET Core .NET 10 service.
Its database, web dependencies and credentials never enter the disc. Independent
commands build the console ISO, host rule checks, and server container. A server
build does not cross-compile a game; a game build never starts a server.

Pinned package baseline (see [package adoption](nuget-rendering-services.md)):

* Capz.Hypercube.GameCube/Core `0.1.0-preview.15.1`.
* DolphinDotNet.Sdk/GameCube/Core and Assets `0.1.0-preview.305.1`.
* Package locks record exact transitive content hashes; global.json pins the SDK.
* Native toolchain: ghcr.io/extremscorner/libogc2 at
  sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85.

The engine is experimental. Its 24-cell validation fixture is not an 80×64 town
format; do not put the town into that hard-coded fixture. Reuse its bounded
scheduler, visibility/LOD policies and native adapter; add game-specific chunk
indexing and model residency. Native SDK APIs provide controller input, graphics,
model animation, sound, disc storage, memory cards and IPv4 sockets.

## Hardware and frame budget

Target stock GameCube: 485 MHz Gekko, 24 MiB main RAM, 16 MiB ARAM, fixed-function
GX/TEV; no programmable vertex shader, GPU skeletal shader or modern PBR pipeline.
Use 640×480 interlaced output initially, safe margins and large controller text.
Simulation is fixed 30 Hz; rendering follows video timing with interpolation.
PAL timing must not change simulation speed. Pause catch-up at four steps to avoid
a spiral after I/O stalls. All scene work is bounded and observable.

Initial main RAM ceilings (hypotheses to measure, not measured results): runtime,
native code and heap 7 MiB; graphics/frame/FIFO/native staging 3 MiB; models and
poses 4 MiB; resident texture pages 3 MiB; world/save state 1 MiB; I/O and network
queues 1 MiB; UI/audio staging 1 MiB; reserve 4 MiB. Reserve 2 MiB ARAM for audio
before creating any Hypercube ARAM cache. ARAM is DMA storage, never directly
addressable gameplay memory. Fail asset budgets at build time; record actual
native high-water marks on target. Aim at 10k visible triangles initially,
under 150 submissions, 256 KiB maximum new asset data per frame and 1 ms
streaming pump. Tune from measurements; emulator FPS is not hardware proof.

## Asset intake and animation

Inventory of the checkout: six rigged GLBs (fox, gecko, giraffe, Maine coon,
prairie dog, spaniel), one konbini-like building and deciduous/palm/pine trees.
No bush or additional house is currently present. Reuse these sources; missing
tools, stalls, tents, house attachments, fences and furniture use small original
procedural meshes until authored assets exist, except trees: those must use only the user-provided GLBs, with no generated artwork or procedural stand-ins. Five selectable house styles may
share the supplied building with distinct roof/wall palettes; do not claim five
different supplied meshes.

Hypercube's static scene importer rejects skins/animations and uses diagnostic
textures. Route GLBs through the SDK's DolphinDotNet.Assets model converter
instead, retaining authored materials, skeletal tracks and texture UVs. Audit
triangle counts, bounds, clip names, joint names/counts, texture dimensions,
extensions and weights. Do not assume a shared skeleton: each species retains
its own rig and animation-name mapping. Texture/material swaps and tool socket
offsets are species-specific. SDK Model/ModelInstance/Animator handles poses;
create shared Model resources and separate instances. Never recreate models per
frame. Prefer RGB5A3/CMPR textures, 128–256 px atlases, four influences at most,
and authored/decimated LODs preserving UVs and joints. Reject assets exceeding
the resident budget instead of silently dropping limbs or animation channels.

## World and rolling presentation

The authoritative world is a square **8×8 grid of screen-sized square tiles**.
Each screen tile is 20×20 logical metres, giving a 160×160-metre square world.
The fixed 4:3 camera is calibrated to a 20-metre horizontal span at the player's
tangent plane. One-metre fine cells within these tiles support item footprints
and collision; they are not additional screen-sized world tiles. Terrain uses
25,600 bytes rather than an array of managed objects. A seeded integer generator lays down terrain, river, bridges,
cliff tier, plaza, store, hall, gate, campsite and six houses, then flood-fills
walkability and checks that mandatory entrances connect. Logical collision and
ownership always use flat tiles. Paths never cross water except bridges, and
cliff transitions need a valid ramp. House reservation validates the entire
15×13 maximum garden against terrain and all other reservations.

Fixed north-facing camera, true **cylindrical** geometry with east-west axis;
never curve X into a sphere. For surface arc distance d=z-focusZ, radius
R=360/(2*pi), independent of the cropped playable bounds, angle a=d/R: render y=(R+h)*cos(a)-R and
z'=focusZ+(R+h)*sin(a), retaining X. CPU tessellation subdivides each screen
tile for fixed-function GX. All object transforms are children of the world
root and rotate around X by a before their local facing. Local down is the
inward surface normal (0,-cos(a),-sin(a)); feet remain attached to the ground
as the cylinder rolls. Terrain, props, villagers, player, fence, furniture and
placement markers use this same transform. Cylinder terrain extends 64 metres
left and right of the focus, beyond the visible frustum even at the horizon;
its sides must never appear as exposed edges. Map remains a square 8×8 grid.
The radius is ten percent smaller than the original 400/(2*pi) presentation,
bringing the forward ground horizon from roughly 19.53 to 18.23 logical metres.
The far plane is 60 metres and terrain considers 32 one-metre rows ahead plus
20 behind; the one-metre viewport guard band is preserved. Camera distance,
pitch and field of view are unchanged. Coverage tests audit beyond this row
window so a shorter lookahead cannot silently remove on-screen ground.
Avoid real-time shadow maps. Cull conservatively in flat XZ
with cylinder-expanded height bounds. Use Hypercube.ScenePolicy LOD hysteresis and
coarse fallback residency; never allow collision to depend on high-detail visual
availability. Controller selection uses facing plus tile adjacency. Any future
screen pointer must intersect the quadratic surface, not a flat raycast.

Sky and fog use GX-compatible colours/TEV; season/time/weather choose palettes.
Map draws a cached illustrated tile texture plus named icons, acre labels,
reservation outlines and online players. Refresh only dirty regions.

## Gameplay state and rules

Stable numeric catalog IDs map to original string names in the server/content
manifest; a catalog version is part of every save and join. Item flags,
footprints, prices and all authority rules live in the shared domain assembly.
Backpack inventory is a five-column icon grid: 10 slots initially, upgraded to
20 and then 30. A compact anchored popover shows the selected item's name and
actions; item names do not clutter the grid. Capacity is enforced in shared
receive/buy/sell/place/transfer rules and on the server, not just visually.
Two shop upgrades cost 6,000 and 18,000 acorns as initial tuning values. Items
remain non-stacking; tool/clothing/accessory are separate; wallet 0..99,999;
checked 64-bit savings and debt. Transfers check capacity and funds before any
mutation. Money bags open atomically only when the complete value fits.

Implement buy/sell, catalog ownership and next-day ordering, mail (10 visible
slots plus held overflow), savings/deposit/withdraw, monthly 0.5% interest with
integer rounding and an applied-month marker, interest-free loan, all six house
tiers and five garden tiers. Expansion requires debt zero, prerequisites and
one pending construction; apply once at the next 06:00 rollover. Relocation
translates house/garden objects together after maximum-footprint validation and
charges 50,000 up front. Placement checks every rotated footprint cell, flags,
ownership, terrain, fence/mailbox/house occupancy and existing items. Drop,
plant and bury use distinct rules from furniture placement.

Arrival starts at debt 24,000. Six real tutorial objectives each credit 1,000;
there is no button that skips directly to an 18,000 balance. Subsequent odd jobs
are capped at two/day. Six residents have personality, schedule, catchphrase,
friendship (0..255) and dialogue/request state. At most three open requests per
player, one per villager; expire at rollover. Lost quest objects have a three-day
owner deadline. Ground objects outside gardens sweep to Lost & Found; original
dropper retains exclusive claim for three days, recycle after seven days.
Distinguish lost-quest age from Lost & Found retention age.

Activities: shake fruit/money from trees; plant/water flowers; digging reveals
fossils/clay chimes; rod and net perform bounded timing/range checks; axe obeys
host permission; tools do not break. Interiors are separate small room scenes
with tier-specific sizes and per-room placements. Exterior palette, fence,
mailbox, paths and construction changes are persisted, not just UI selections.
Clay chimes replace the working-name gyroids. Adjacent chime enables a four-item
garden stall; server purchases credit seller savings and enqueue buyer grants.

## Time, controls, UI and sound

Use console RTC/local calendar; logical day begins 06:00. Persist last processed
day/month. Forward jumps process bounded aggregate growth and expire old state;
backward jumps never pay interest/rewards twice. Online session uses host time,
but server UTC enforces economy reward caps and ticket expiry.

Stick or D-pad moves in eight directions, B runs/cancels, A interacts/confirms, X pockets, Y map, Z tools,
Start save/settings; stick or D-pad changes tabs/selection, and L/R switches tabs.
Menu stick input has a dead zone, one dominant direction and timed hold repeat
(350 ms initially, then 120 ms). Recenter after opening a menu or switching tabs
so held walking input cannot change a selection. Settings explanations return
to their selected settings row when dismissed. All screens navigable without
a mouse. Character creation selects all six species, palette, name, catchphrase
and pitch. All UI surfaces use the same rounded visual language: 30-pixel panel
corners, gently bulging convex menu sides, pill-shaped HUD cards and selection highlights, rounded keyboard keys,
and rounded map/plot/menu containers. Speech balloons use large 30-pixel rounded corners, rounded name
tabs and an attached tail, drawn through the batched GX 2D API. A reusable
balloon supports name tab, typewriter text, choices, world chat anchoring and
50-line history.
Gorditas Bold for titles/major headings, and the native IPL firmware font for
all remaining text. SDK `IplFont.Load` and the `Draw2D.DrawText` overload read
the console firmware through libogc; no firmware font bytes enter the ISO.
Gorditas is baked offline; no TTF rasterizer enters the console. See `docs/visual-identity.md`
for the original doorway/seed mark and shared colour/shape tokens.
Panel headings rock gently up to 1.8 degrees about their centred pivots.
48-pixel source glyphs are shelf-packed into 512×512 IA8 atlases and filtered
down to the desired physical size. A separate 1024×256 Gorditas wordmark avoids
enlarging small title glyphs. The backpack uses an original RGB5A3 icon atlas.
Walk/run use 0.08925/0.168 logical metres per 30 Hz step, five percent above
the former 0.085/0.16. Radial dead zones and vector normalization preserve light
analog movement without faster diagonals. Model yaw follows the full travel
vector, collision sliding follows actual travel, and blocked input can still
turn toward an interaction target. Eight-way interactions use the nearest
neighbour in that heading.

Generate short original PCM syllables
and bound speech to one explicitly owned channel. Stop it before another
syllable and on completion, skip, close or replacement; optional playback
failure cannot terminate dialogue. Reserve channels for UI, ambience and chimes. On-screen
keyboard caps chat at 80 chars; quick phrases, mute, chat-off and block remain
available without text entry. Animations use each species' actual clip mapping.

## Memory-card persistence and banners

Disc identity `AMTE01` is a project-local homebrew ID, not an allocated retail ID.
Memory-card identity is `AMTE` / `01`. Use SDK MemoryCard.WriteSave with original
96×32 RGB5A3 banner, 32×32 icon, title AnimalTown and commit in the comment.
Four separate records store town data, house positions, shop states and player
villager data, in two alternating banks. A commit record binds each bank and is
written last after readback. All headers/checksums must match one generation;
load the newest complete bank. Backend storage uses matching separate columns
and transactional migration. See [split-saves.md](split-saves.md) for ownership,
recovery rules, card-space overhead and legacy import behavior.
Schema 4 records the current 160×160 fine-cell dimensions. Older 400×400
schema 2/3 terrain is cropped by original row stride, preserving the northwest
160×160 region. Outlying trees are removed, other ground items move to Lost &
Found with owner protection, and outlying homes relocate with their furniture.
Player/resident arrivals stay inside the retained town. See [town-size.md](town-size.md).
Schema 3 stores the backpack tier and 30 backing slots, while enforcing unlocked
capacity. Legacy 15-slot saves migrate to 20 slots if needed to preserve items.
Schema 2 introduced run-length fine terrain for the full 400×400 world;
schema 1 migration preserves the former 80×72 region. Save staging allocates
from actual encoded size rather than the 1-MiB format ceiling. The managed heap
is explicitly 4 MiB to accommodate bounded terrain, native-font loading and save staging.
Never format a card, overwrite another game's file, or silently discard errors.
Save only through Settings → Save now. Timers, rollover, travel and debug tools
never write a save. Loading and legacy migration stay read-only until that action. Report card
missing/full/write failures; gameplay continues and player can retry. Unknown
newer versions are read-only errors; migrations must preserve a backup.

## Online transport and trust boundary

Broadband Adapter is optional. No BBA means full offline game with gate UI
explaining the requirement. The original SDK baseline supplied IPv4 TCP/UDP and
asynchronous pumping. The updated SDK pin also exposes native TLS/HTTPS, which
can serve authenticated backend control calls. WebRTC, SignalR and NAT traversal
are not the console transport. The initial companion design below remains an
unimplemented transport option; it is no longer required just to obtain HTTPS.

Use a separately built local companion gateway for internet play. Console to
gateway uses length-prefixed binary TCP for control/reliable events and optional
UDP movement on the trusted LAN; display pairing and connection state. Companion
owns TLS/account credentials and forwards authenticated traffic to server and
peer gateways. Remote town simulation stays on its GameCube host; gateway can
relay encrypted internet traffic for NAT/privacy without running town gameplay.
Direct LAN visits can use the same framed protocol. Never send account passwords
or bearer tokens over the console LAN link. A public unauthenticated raw socket
server is not an acceptable substitute. End-to-end ticket validation is handled
by paired gateways until a verified target crypto implementation exists.

Header: magic/version/type/payload length/session/sequence, network byte order.
Control payload <=4 KiB; snapshot chunks <=16 KiB, total <=512 KiB; max four
players. Validate enum, tile, slot, text length, ownership and message rate before
mutating state. Hello checks protocol/build/catalog versions. Host sim 30 Hz,
movement 15 Hz, NPC replication 5 Hz, 100 ms interpolation; guests predict only
their movement. Disconnect ends visits without host migration. Snapshots have
chunk index/count, CRC and deadline; no allocation from untrusted lengths.
Reliable intents carry unique operation IDs; deltas carry monotonic revision.

Friends-only visits require mutual acceptance, no blocks, open gate and a
two-minute signed visit ticket. Server atomically reserves capacity (three
guests), expires stale sessions and revokes blocked sessions. One persistent
3×3 tent per guest, max three, no custom furniture; removal at rollover or block.
Guests cannot place furniture, take garden contents or expand a host property.
Gift, pickup across owners, rewards and stall purchase require server commit
before either client permanently consumes/receives assets. Every operation has
an idempotency key and result; disconnect reconciles pending operations by key.
Guest rewards are limited to three per host/day, computed by server UTC.

## Backend, database and synchronisation

ASP.NET Core .NET 10 API, PostgreSQL persistence, explicit migrations. Relational
accounts, mutual friendships, blocks, wallets, savings, debt, inventory, escrowed
listings, grants, tents, visits, reports and idempotency records; bounded JSONB
town document with revision. Authentication uses gateway pairing and authenticated
web account flows; no invented GameCube platform login. Enforce ownership on
every endpoint and resource lookup. TLS terminates at service ingress. Rate and
payload limits apply before deserializing large content; logs omit credentials.

Implement the GDD §16.7 routes using versioned `/v1` paths and gateway-friendly
WebSocket events in place of SignalR/WebRTC signalling. API and realtime share
authorization. Purchase runs in one transaction: lock unsold listing and visit,
debit buyer conditionally, credit seller savings with overflow guard, create
unique grant/receipt and record operation result. Listing creation escrows the
specific inventory instance, so it cannot be sold or gifted twice. Claim grant
locks a free inventory slot; capacity failure retains grant. Concurrent buyers,
retries and transaction rollback need database integration tests.

Cloud saves use If-Match revision; 412 exposes device/cloud conflict selection.
Never replay arbitrary edited offline balances as trusted online currency.
Offline operation journal is bounded and validated against server-owned base
state; divergence requires an explicit branch/reconciliation. Keep single-player
save playable even if online import is rejected. Moderation reports contain at
most 50 lines with membership checks; restrict chat both at send and receive.

## Bootable disc contract

Every successful console build ends with `out/AnimalTown-<version>.iso`, never
only an ELF/DOL or the engine's data ISO. Disc has a GameCube boot header/magic,
BI2, a redistributable homebrew apploader, valid main.dol, aligned FST and assets,
root opening.bnr and BUILD.txt with full game/engine/SDK revisions. Header title,
BNR metadata and title screen name AnimalTown and show the build version. Full
source commits remain in BUILD.txt; see [versioning](versioning.md). Disc
and memory-card artwork are different required outputs. Dirty builds append a
dirty marker and record a source digest; releases require clean source.

Package to a full 1,459,978,240-byte GameCube image for mini-DVD compatibility;
sparse local creation is acceptable, archived artifacts compress padding. Verify
offsets, DOL load sections/BSS/entrypoint, FST hierarchy, alignment, banner format,
required files and hashes before publishing. Consume the pinned SDK-owned
apploader image with its source identity and license; the SDK builds it from
redistributable source. Never copy Nintendo boot code. Boot ISO
directly in Dolphin with no separately selected DOL, then test GCLoader/Swiss.
Burned media additionally requires suitable modified hardware and media; ISO
structure alone cannot validate an optical drive or disc. Physical tests must
be reported as unperformed until actually run.

## Acceptance and implementation order

1. Commit and push this design before game implementation.
2. Reproducible native build, bootable ISO, both banners, commit identity and
   direct-ISO emulator smoke check; prove assets convert without unbounded RAM.
3. Shared deterministic rules and tests: town connectivity, money atomicity,
   inventory capacity, placement rotation/ownership, all expansions and rollover.
4. Console world, six species/animations, controller UI, tutorial, activities,
   villagers, store, mail, interiors, garden/editor and recoverable card saves.
5. Independent server and gateway builds; authenticated persistence and database
   race/idempotency tests before exposing transfers or visits.
6. Four-console visits, tent persistence, stalls, chat restrictions, disconnect
   reconciliation, backward/forward clock and corrupted/full-card tests.
7. Hardware profiling, PAL/NTSC output, long sessions, offline completeness and
   full GDD traceability. Do not label a vertical slice the entire game.

Track implemented, tested, blocked and missing features separately. Passing host
rules or creating an ISO does not establish a complete game or hardware success.

## API evidence

Engine README/build pipeline and ScenePolicy are in the [engine repository](https://github.com/capz/hypercube-engine).
SDK Model/ModelInstance, Storage.MemoryCard/MemoryCardSaveOptions, IPv4 network
types, asset converter and SaveImage tool are in the pinned SDK checkout. Inspect
these concrete APIs before adding calls; desktop compilation is not proof of AOT
support. External references: https://github.com/capz/hypercube-engine and
https://github.com/capz/DolphinDotNet at the revisions above.

## Terrain authoring implementation

See [terrain-authoring.md](terrain-authoring.md) for the JSON source contract, seasonal/repeating variants and non-repeating nine-slice patches. The active RGB565 atlas is bounded to 128 KiB by default (512 KiB optional), with no runtime image/JSON dependencies. Reusable textured terrain meshes replace solid-color primitives; the outdoor band has 8,036 triangles. These costs and up to eight detailed supplied tree meshes require hardware profiling. Further tree geometry/LODs must come from the user; requested world assets and reference scope are in [world-asset-requests.md](world-asset-requests.md).
