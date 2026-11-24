# Optional clear-water artwork

The game runs with procedural textures when this directory contains no PNGs.

For authored replacements, supply only the exact files described in
[the water asset brief](../../../../docs/water-art-assets.md):

- `riverbed-color.png`: 128×128 opaque color.
- `surface-ripples.png`: 64×64 opaque grayscale.
- `bed-caustics.png`: 64×64 opaque grayscale.
- `surface-distortion.png`: 64×64 opaque two-channel displacement field.
- `shoreline-foam.png`: 64×64 grayscale plus alpha.

Run `dotnet run --project tools/AnimalTown.Tools -c Release -- water-assets .`
from the repository root, or use the normal game build. These optional PNGs
replace individual generated maps; none is required to play.
