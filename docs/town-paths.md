# Town paths — approach B

New towns receive a deterministic pedestrian dirt network. A two-metre route
connects the plaza, service areas, resident-home aprons and existing bridges
where a safe full-width route exists. New player homes add a one-metre doorway
spur. Trees, planting, buildings, water, beach edges, gardens and ramp traversal
constrain routes. Unsafe connections remain unpainted and are counted explicitly.
Old saves load an empty road layer; their landscape is never replanned on import.
There is no road-editing menu in this delivery.

The original painted tile is `assets/textures/environment/paths/dirt-64.png`.
It contains broad ochre wear and sparse pebbles, with no noise overlay. Asset
cooking emits one 64×64 opaque CMPR texture with five mip levels through 4×4:
2,752 native pixel bytes / 2,784 disc bytes. All seasons and shapes share it.
The source PNG and decoded source pixels do not ship or stay resident.

Road identity lives in a separate 3,840-byte two-bit overlay. Terrain identities
and collision/planting rules remain unchanged. Shared-town record v6 and combined
save v9 add a versioned packed block protected by the existing checksums; v4/v8
and older records import empty. Incremental manifests remain v5 and can reference
old v4 or current v6 town components alongside unchanged v4 personal components.
Onboarding owns its candidate road bytes, so cancelling a home choice cannot
change the saved town or leave draft entrance spurs behind.

One reserved material (slot 51 of the existing 52) batches road geometry into
the immutable terrain archive. Straight strips still merge. Exposed flat corners
replace their ground faces with a bevel and complementary grass triangle, so
there is no coplanar decal or alpha overdraw. Ramps keep the exact original ground
triangles. UVs remain world-aligned at one repeat per two metres and receiver
alpha remains opaque for the shared shadow/light map. Cliff sides and river banks
continue to use their underlying ground material.

One central lease owns the path page. Selected/prefetched archive views drive
demand; async disc jobs return bytes only and stale generations are discarded.
Without demand, indoors, on town replacement and on disposal, material borrowers
are cleared before explicit cache eviction after EndFrame. The indoor rule is
independent of the terrain-cache debug option. Cold reads use a dirt-tinted shared
ground fallback; a failed page does not remove the road mesh or retry every frame.

## Reproducing measurements

`dotnet run --project tests/AnimalTown.Checks -c Release -- --path-fixture OUTPUT`
produces paired disposable snapshots of the same seed, trees, buildings, time,
profiles and cameras, with only the path overlay changed. The existing
`tools/profile-card-fixture.py` writes them onto separate emulator test cards.
Never use a player's card for the study.

The existing L+R+D-right research mode selects native 640×480, AA off, Fresnel at
100%. L+R+D-left records 240 frames. Additional `PATH PROFILE` rows record
instrumented CPU/submission work, preparation time, terrain submission time,
allocator activity and GC work; memory rows report SDK native bytes, texture
bytes, used ARAM, cache peak/reservation, DMA reads and bindings. All log dumps
follow the capture END timestamp. Ordinary play allocates none of these arrays.
Capture baseline and roads at matched stationary views, matched travel, indoors
and far from the network; repeat in spring and winter. Disable AVI recording.
Dolphin software rendering and emulated guest timings are not hardware timings.

Measured results, before/after screenshots and reproduction instructions are in
[the performance report](performance/town-paths/README.md).
