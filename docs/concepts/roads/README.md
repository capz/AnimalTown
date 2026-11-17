# Road concept evidence

- `path-directions.png`: generated comparison art, 16 November 2026, made with
  the built-in ImageGen tool. References were the repository's
  `docs/performance/visibility-outdoor.png`,
  `docs/performance/immutable-winter-house.png`,
  `assets/textures/environment/ground/outdoor-floor-neutral.png` and
  `assets/textures/environment/transitions/stone_pavement/previews/spring_preview.png`.
  It compares shared tint, tiny painted art, baked mesh silhouettes and selective
  painted reuse in the same invented town scene. It is not an emulator capture,
  a final reusable runtime texture or approved implementation artwork.
- `measure-textures.py`: offline study using Pillow and the repository's actual
  `FloorTexture` encoder/`TexturePixels` decoder through a temporary .NET 8
  project. No external NuGet dependencies or GameCube SDK are required; the
  .NET 8 targeting pack must be installed. Original PNGs are read without being
  changed. Temporary RGBA, project and cooked packages are discarded.
- `texture-study.json`: results from the successful 13-sample study, including
  encoder/input SHA-256 identities, crop coordinates, formats, mip counts,
  pixel/package sizes and base-level RGB compression error. This is a host
  encoding experiment, not proof that the cropped art repeats seamlessly or
  that a runtime renderer/pruning policy has been implemented.

The [design proposal](../../roads-and-paths-design.md) defines the recommended
combination and the additional rendering, residency and art work needed after
visual approval. None of these files enters the active game asset manifest.
