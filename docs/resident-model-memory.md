# Resident model memory

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
| Fox | 330,912 |
| Bunny | 655,968 |
| Giraffe | 331,360 |
| Cat | 659,712 |
| Hamster | 331,040 |
| Spaniel | 332,864 |

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

The six default models recover **2,641,856 bytes** (about 2.52 MiB). The largest
temporary compact allocation is 247,740 bytes; models are processed one at a
time during loading. This avoids making another multi-megabyte model set.

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
