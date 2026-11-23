# Resident model memory

Gameplay loads the 128px variant for the player and all NPC species. The 256px
variant remains an explicit option; 512/1024px villager packages are not cooked,
included in ISO/Depot payloads or accepted by the model-loading policy. Other
texture defaults remain 256px.

Across the six current cooked species (eight textures, base plus one mip),
texture payloads fall from **2,621,440 to 655,360 bytes**: **1,966,080 bytes
(1.875 MiB), or 75%, less per complete texture copy**. The six source model
packages shrink by the same amount. Removing the twelve unused 512/1024 variants
also removes **54,810,176 bytes** of cooked package files from disc staging;
compressed download savings differ. These are cooked-payload measurements, not
a whole-game MEM1 reading. Runtime residency and shared texture ownership decide
which copies occupy MEM1 or ARAM, so do not sum them with the savings below.

The ARAM residency path uses complete SDK packages for pageable villagers.
The selected player's shared species is permanently pinned for the session, so
it alone calls `NativeModels.Retain`, before any instances exist. Compact packages
disable snapshot/restore; the residency manager therefore excludes them from
demotion. Other species retain full packages for ARAM. Static buildings still use
their separate retention path. See [ARAM model residency](aram-model-residency.md).

The selected-only native fixture preserves geometry, skins, animation, poses,
textures and disposal for each of the six choices. Its requested allocation
savings are:

| Selected species | Requested native bytes freed |
| --- | ---: |
| Fox | 85,152 |
| Bunny | 164,448 |
| Giraffe | 85,600 |
| Cat | 168,192 |
| Hamster | 85,280 |
| Spaniel | 87,104 |

These are host native allocation measurements, not whole-game MEM1 or hardware
timings. The all-six permanent-policy saving below must not be added to the
ARAM cold-cohort saving; only the selected species uses both session pinning and
permanent compaction in the game.

The six resident villagers previously kept their embedded texture payloads in
the DNMD package after the renderer had acquired separate shared GX texture
storage. Retaining both copies exhausted MEM1 when enabling the 150 KiB water
capture. The permanently resident policy compacts each model after asynchronous load
completes and before creating its first instance.

`NativeModels.Retain` keeps geometry, indices, skin bindings and animation tracks,
rebases their pointers, and releases the original package allocation. Parsed
nodes/materials and the shared GPU texture cache keep their existing ownership.
No pixels, mip levels, mesh data, animation keys or poses are approximated.
The existing static-building retention path keeps its static-only checks.

With the updated rigs and unused texture maps excluded at import, the six
128px default models recover **517,248 bytes** from duplicate package storage.
The retained texture payload is **491,520 bytes** across all six models. Models
are compacted one at a time during loading, which avoids allocating a second
set of packages.

Compaction requires a ready model with one owner and no active graphics frame.
Span/alignment validation and allocation complete before the source allocation
is released. Failure leaves the original model intact. Compacted models remain
resident: copying/exporting their source package or unloading it is rejected,
as with retained buildings. Disposal remains supported. Future source-package
streaming must use a separate policy rather than treating this compact buffer
as a complete DNMD file. Disc assets and save data are unchanged.

`tests/native-resident-models.c` uses the six actual cooked villagers with the
production loader/animator. It compares geometry, indices, skin bytes, keyframes,
animated poses/global transforms and skinned vertices; checks shared texture
ownership/disposal; and rejects invalid pointers, clip spans, live instances,
active-frame calls and allocation failures without mutating the source model.
The normal console build runs this fixture after native patching. It also passes
AddressSanitizer, UndefinedBehaviorSanitizer and leak detection on the host.
