# Villager rim and object ground shadows

Villagers retain their imported albedo, vertex/material colour, cutout and
transparency. The native model renderer adds a grazing-angle rim in the same
draw: GX normal TexGen transforms the existing static or skinned normals into
view space, normalizes them, and samples a shared 64×64 I8 ring lookup. One TEV
stage adds the lookup multiplied by a subdued blue-white colour and passes the
previous alpha through unchanged. Ordinary materials use two TEV stages; an
imported secondary-map combiner uses four. No inverted hull, per-vertex CPU rim,
framebuffer copy or model texture replacement is involved. This approximates a
camera-parallel Fresnel rim; it is not a reproduction of Nintendo's shader.

The game-owned bridge in `tools/native-rim.inc` is applied to the pinned native
package by `tools/patch-native.ps1`, with checked hook signatures. Both indexed
and direct submissions carry normals even for unlit materials. Every instance
refreshes its normal texture matrix, including material-batched draws. The
bridge is scoped to villagers and resets the model material cache at its edges.
The 4 KiB immutable lookup is initialized between frames and uses no resource
table handle. Buildings, terrain, water and UI keep their own combiners.

House and tree shadows use four original soft proxy silhouettes in one 128×128
IA8 atlas (32 KiB). They are contact/ground shadows with a fixed light direction,
not dynamic shadow maps or self-shadowing. Tree species and sapling scale match
the visible generated trees. Houses use a simple roof/body footprint; future
building shapes will need an appropriate proxy. Projection bounds include
offscreen casters whose shadows can reach the view.

Mask quantization uses a byte-returning helper to avoid a pinned AOT compiler
float-to-byte array-store bug. The native patch/build step checks the bundled
atlas function for integer byte stores; managed texture tests alone cannot
detect this compiler regression. Native captures must show actual ground shade.

Receiver heights share the floor's `GroundSurface` rule, including the low beach
toe. Receiver triangles are clipped to the projected footprint, dry ground cells and
the exact upper/lower diagonal half-planes. Clipping interpolates the existing
cylinder row chords, rather than independently bending fractional vertices.
Ramps receive the shadow; discontinuous support heights and water stop it.
These support-connected shadows deliberately do not cast from an upper cliff
onto a disconnected lower surface, or onto actors, walls and other objects.
They draw after ground and before actors/objects, with depth testing, a small
surface offset, alpha blending and no depth writes. Clouds remain independent.

The cache holds at most 32 caster meshes, prioritizing visible buildings then
nearby trees. Each mesh is capped at 96 triangles; an overflowing proxy is
omitted. One staging buffer and retained native mesh capacities are reused.
At the vertex limit the geometry payload is 216 KiB managed and 342 KiB native
(including native normal storage and indices), plus the shared atlas, staging
data and buffer bookkeeping; actual usage depends on selected casters.
No geometry is generated while stationary after the cache fills. Planting,
chopping, growth and house relocation invalidate affected caster entries;
terrain revision changes invalidate receivers. Save formats are unchanged.

One fine receiver row is a work stage. Shadow jobs consume only time left in
the existing terrain deadline (2 ms, eight total stages per frame). Terrain
streaming has priority. Unfinished shadows are omitted and never hold up the
landscape readiness gate. The deadline is cooperative: a single stage can run
past it, so emulator and hardware timing still matter.

For developer inspection, hold L+R+Z and press Y to cycle both/off/rim/shadows.
Z also toggles telemetry when first pressed. `tools/iso-smoke.py --lighting
--dsp-hle` captures the four settings, interior camera/exit, UI and water state
transitions. `--terrain-scroll --stream-stress` exercises streaming peaks with
both effects enabled. Physical GameCube validation is still required.
