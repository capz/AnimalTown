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

Only outdoor actor and building model draws enable this pass. Terrain, trees,
fences, dropped items, interiors and lamp meshes keep their existing shading.
Ground pools remain baked into the existing shadow map: per-frame point updates
never dirty or rewrite its coverage. This is local diffuse illumination, without
new occlusion or dynamic cast-shadow maps.

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
coverage after point movement. Console compilation and nighttime emulator
captures exercise the actual indexed/direct model paths.
