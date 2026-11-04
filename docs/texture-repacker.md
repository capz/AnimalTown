# Texture and UV repacker

This offline tool rewrites an embedded-texture GLB and produces a neighbouring
`.repack.json` audit. Artist sources are never overwritten. It does not change
the game assets automatically.

```powershell
dotnet run --project tools/AnimalTown.Tools -c Release -- repack-textures . input.glb output.glb --size256
```

Choose exactly one output flag: `--size128`, `--size256`, `--size512`,
`--size1024`, or `--sizeOriginal`. The numbered options make square atlases.
`--sizeOriginal` keeps each source image's original width and height, including
non-square sources. It does not shrink chart contents. If lossless rectangular
chart packing plus gutters cannot fit, it preserves the source layout and still
stacks eligible flat-colour UVs. This fallback is recorded as `preserveLayout`.

The repacker identifies triangles whose complete source sampling rectangle is
one **exact RGBA colour**, and redirects matching triangles to the same swatch.
This conservative test includes bilinear neighbours; it never equates similar
colours or discards painted detail. Textured triangles are grouped into connected
UV charts. Exact chart pixels and their filter context are compared at 0, 90,
180 and 270 degrees; matching charts share one atlas region with compensating UV
rotation. The rectangle packer also considers 90-degree placement. Numbered outputs shrink chart
contents only when necessary to fit. Two base-level gutter texels retain source
sampling context, including clamp/repeat/mirror addressing, and protect the base
plus one half-size mip used by AnimalTown. More mip levels need larger gutters
and are outside this tool's current contract.

Positions, normals, colours, joint indices, weights and other vertex streams
retain their original component bytes. Vertices split where UV charts require
different coordinates. Triangle order, materials, hierarchy, skin bindings and
animation streams remain intact. Dead buffer views and obsolete accessors are
removed; output images are embedded lossless PNGs.

## Appearance validation

Before writing, the tool compares source and output RGBA samples with the authored
magnification filter (linear or nearest) at 45
barycentric positions per triangle, including corners and edges. The audit
records overall and worst-triangle RMSE, maximum channel error, sample count,
chart scaling, shared colours, dimensions and SHA-256 identities. This catches
UV shifts, colour substitutions and excessive downsampling; it is a numerical
check rather than a substitute for inspecting the model under game lighting.

The default maximum **per-triangle** RGBA RMSE is 8 byte values for numbered
sizes and 0.01 for `--sizeOriginal`. Failure produces a nonzero exit code and no
new output model. An explicit `--max-rmse N` override allows a reviewed lossier
conversion; 0..255 is accepted. For example:

```powershell
dotnet run --project tools/AnimalTown.Tools -c Release -- repack-textures . input.glb output.glb --size512 --max-rmse 4
dotnet run --project tools/AnimalTown.Tools -c Release -- repack-textures . input.glb output.glb --sizeOriginal
dotnet run --project tools/AnimalTown.Tools -c Release -- repack-check .
```

The current shop and fox use noisy 2048-pixel painted atlases. Original-size
validation passes for both; the shop initially has seven additional flat-colour
triangles eligible for sharing, while the fox has no exact uniform triangles.
Do not expect aggressive exact-colour stacking to simplify noisy painted art.
A small target can fail packing or appearance validation; that is preferable to
quietly writing a model with damaged artwork.

Supported input: GLB v2, one embedded buffer, embedded PNG/JPEG images, triangle
lists, base-colour-only materials, untransformed TEXCOORD_0 in 0..1, float or
normalized unsigned UVs, ordinary core skinning and animation. Clamp, repeat and
mirror samplers are retained, but one image cannot use conflicting wrap modes or
magnification filters. The check covers the base level; lighting and minified
mipmap appearance still need inspection in the game.
Secondary material maps, extensions, morphs, sparse vertex accessors, external
images and repeating UVs outside 0..1 are rejected explicitly. They require
additional coordinated map/geometry handling, not silent approximation.

The Linux build runs synthetic tests for all five size policies, exact colour
sharing, position/weight/joint preservation, animation remapping, source safety,
and refusal of unsupported or excessively lossy conversions.

The report also records `sharedCharts` and `rotatedStacks`; rotated matches are
byte-compared after fingerprint lookup. Tests exercise 90/180/270-degree sharing
with asymmetric, non-square painted charts and verify their appearance.

See [human-driven editor design](uv-editor-design.md) for the proposed Blender UI,
source/target UV workflow and preview-baking requirements.
