# Validate only affected components

Use `./tools/test-changes.ps1` during development. It includes working changes
and compares against the most recent ancestor release. `-BaseRef HEAD` tests only
local edits; `-PlanOnly` prints/writes the plan without executing component tests.
Native checks use the Linux builder image through Docker, or `-NativeInProcess`
when already inside it. Use `-Full` when a broad run is deliberately required.

The runner and main-branch release workflow use the same dependency map in
`tools/validation-plan.ps1`. Examples:

| Change | Selected checks |
| --- | --- |
| Menu navigation | Menu suite |
| Text wrapping | Text suite |
| Native UV diagnostics | Native UV topology checks |
| Ground texture/import | Terrain importer and core render/texture checks |
| Repacker | Repacker checks |
| Villager navigation/naming | Terrain-life and generation checks |
| Server/database | Backend integration |
| Shared gameplay/save behavior | Managed domain suites and backend integration |
| Docs/concept art only | No tests, toolchain build or release |

Large mixed files such as the game entry point select the managed suite set,
but do not automatically run unrelated server, repacker or importer tests.
Unknown production paths, SDK/engine changes and validation/build/workflow changes
use full coverage. Edit the map when adding a new component; never silently omit
checks for an unrecognized path.

The managed runner accepts `--suites` with comma-separated names: `ui`, `menu`,
`text`, `trim`, `terrain`, `movement`, `dialogue`, `generation`, `life`, `saves`,
`core`. Omitting it still runs everything. Unknown names fail rather than passing
with zero checks. The older inline gameplay/render/texture checks remain together
as `core`; they can be split later if their measured cost warrants it.

CI compares all commits since the preceding ancestor `build-*` release tag,
not merely the last pushed commit. Thus an unreleased/failed commit's changes
remain in the next plan. Renames include both old and new paths. Missing history,
an invalid base or manual workflow dispatch selects full validation. Reruns exclude
the current commit's own tag when finding the preceding release.

Releases still build the full bootable ISO, backend and tools from the same clean
commit, perform real asset imports/texture audits and verify the ISO structure.
These are build/delivery checks, independent of the selected unit/integration
suites. When backend behavior is unaffected, CI compiles/packages the backend
without starting PostgreSQL. No executable reuse from a different commit occurs.

Every release includes `validation-plan.json` with its source commit, comparison
base, changed paths and selected checks. Its SHA-256 is in SHA256SUMS.txt. Stale
plans are rejected by build and release scripts. The selector's routing checks
run when the routing/build gates change or full coverage is requested.

Choose Dolphin/hardware scenarios for the behavior actually changed. A concept
sheet, palette adjustment or test-routing edit does not justify repeating every
movement, dialogue, backpack and migration smoke scenario. Re-run a scenario
after a related implementation change, a failure or an unresolved concern.
