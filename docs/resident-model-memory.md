# Resident model memory

The ARAM residency path uses complete SDK model packages for villagers and does
not call `NativeModels.Retain`: compact packages disable snapshot/restore.
Villagers are registered with `ModelResidency` instead. The compaction mechanism
and measurements below describe the permanently resident policy; its 2.52 MiB
saving must not be counted alongside ARAM paging. Static buildings still use
their separate retention path. See [ARAM model residency](aram-model-residency.md).

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
