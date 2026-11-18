# Local light on villagers and houses

Existing doorway and street-lamp sources also light outdoor villagers and house
exteriors. Sources follow the same cylinder and terrain height as the models.
The two nearest sources per model use GX point lights with normal-based diffuse
lighting and quadratic distance attenuation. The clock's existing lamp strength
fades them in and out. Door lights sit 1.3 m above ground; lamp lights sit 2.3 m
above ground. Their model-light radius is 1.65 times the baked pool radius.

A second raster channel adds warm light through the existing albedo texture,
after the ambient tint and optional villager rim. Alpha passes through unchanged.
Indexed positions, normals and skin buffers stay indexed. The direct fallback
normalizes transformed normals and supplies unshaded reflectance separately from
its ordinary headlight color. Scope changes invalidate imported material caches.

Only outdoor actor and building model draws enable this pass. Ground pools use
a separate layout in the existing packed IA4 shadow map. `TownLights` sends
that layout after a town, building or terrain revision, and sends clock gain
on its own while the layout is stable. A dirty bake clears the light nibble and
samples each source's bounded rectangle. Ground sampling retains maximum
coverage across overlaps, and the four-stage GX footer applies the clock's
warm gain to that coverage.

Costs: no new textures, render targets, meshes, draw calls or per-frame arrays.
The shared native buffer grows by 480 bytes on the host layout, from 521,536 to
522,016 bytes, still below its 512 KiB cap. Managed source staging adds 576 bytes
of float payload plus two array headers. During active local lighting, an object
uses two GX lights, a second color channel and one additional TEV stage; the
existing albedo is sampled again. Daytime skips the pass. Source selection scans
at most 24 entries per mesh, and sources beyond four radii are ignored.

The recording GX fixture checks scope/day gating, nearest-source selection,
eye-space positions, attenuation and preserved alpha. The native lifecycle
fixture checks invalid inputs, release/reinitialization and byte-identical ground
coverage after point movement. It also verifies a full lamp-gain sweep without
rebaking or publishing a ground mask.

Generated trees use local ambient occlusion at trunk feet and canopy undersides.
Their three shared meshes multiply the existing seasonal palette in vertex RGB
when preparing geometry. The same material samples the mipmapped detail texture
and passes through vertex alpha. [Timings and Dolphin captures](performance/static-lighting.md)
