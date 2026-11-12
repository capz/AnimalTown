# Actual-model native residency measurement

`current-model-cohort.json` records a CPU-only host run of the production SDK
model and ARAM code using the six current AnimalTown species assets. There are
seven instances: the six NPCs and a fox player sharing the fox model. The test
keeps both fox instances warm, then demotes and restores the other five models.
It reserves 2 MiB for audio/platform ownership and 8 MiB of ARAM for terrain,
and limits model snapshots to 4 MiB, as the game does.

The primary cohort uses the game's default 256-pixel texture policy. The 128
cohort is a comparison, not a change to the game's asset quality.

The verifier intercepts the production model/animation translation units'
`calloc`, `aligned_alloc` and `free`. Its requested-byte counts come from the
actual allocator calls, rather than sums of per-model budget charges. It also
reports the host allocator's usable allocation sizes when glibc or macOS exposes
them. Usable sizes depend on host rounding/page behavior and are not GameCube
MEM1 measurements. Test inputs, expected hashes, simulated external ARAM, native
static storage and the managed heap are outside this allocation scope.

Every restore checks the complete package bytes, material/authored-material
tables, runtime texture copies, skin outputs, retained global/pose matrices and
animation state. The final teardown must free every tracked native allocation.
Each residency call is capped at 4,096 logical bytes and 32 native work items.
This proves CPU data/lifetime behavior, not rendering, GX cache invalidation,
stock-console frame time or doorway latency.

## Reproduce

Prepare/cook the species through the normal build asset steps. No console build
is required: use `AnimalTown.Tools prepare`, then `dnd-assets model` with
`--texture-limit 256 --mip-levels 2 --compile-gx-materials`, then
`AnimalTown.Tools model-native` for native face winding. Non-fox inputs also use
`--allow-material-approximation`. Optional 128-pixel inputs use the same recipe
and `-tex128.dndmodel` suffix. Preserve the converter version in the report;
changing the current game's package pins does not identify an older cooked file.

From the AnimalTown repository root, with a C11 host compiler available:

```sh
python3 docs/measurements/aram/measure_model_residency.py \
  --sdk-root ../DolphinDotNet-aram --assets out/disc \
  --assets-version 0.1.0-preview.328.1 \
  --output out/aram-model-validation.json
```

Use `--edges 256` to run only the default cohort. The runner snapshots the SDK
headers/runtime sources before compilation, records their SHA256 values, and
checks every input package hash against the native verifier's output. It writes
no SDK source, package cache or game dependency pins. The checked-in report
retains the source revision and dirty-state observations from the measured
worktrees; the source hashes identify the actual compiled native snapshot.
Pass `--assets-package /path/to/DolphinDotNet.Assets.0.1.0-preview.328.1.nupkg`
to also retain the published converter package checksum and repository commit.
The current report uses that published package from SDK merge `d53a6589`.
