# Validate only affected components

Use `./tools/test-changes.ps1` during development. It includes working changes
and compares against the most recent ancestor release. Pass `-BaseRef origin/main`
when that ref is an ancestor to select the committed changes on your branch.
`-BaseRef HEAD` tests only local edits; `-PlanOnly` prints/writes the plan without executing component tests.
Native checks use the Linux builder image through Docker, or `-NativeInProcess`
when already inside it. Use `-Full` when a broad run is deliberately required.

The runner and release workflow use the same dependency map in
`tools/validation-plan.ps1`. The plan picks host suites, resource fixtures,
and individual Dolphin scenarios. Examples follow.

| Change | Selected checks |
| --- | --- |
| Menu navigation | Menu suite |
| Text wrapping | Text suite |
| Native UV diagnostics | Native UV topology checks |
| Ground texture/import | Terrain importer and core render/texture checks |
| Repacker | Repacker checks |
| Buildings, portals, interiors and journeys | Building, movement, life and save checks; definition/model changes also run the building importer checks |
| Villager navigation/naming | Terrain-life and generation checks |
| Server/database | Backend integration without Dolphin |
| Multiplayer wire codec | Movement and backend integration |
| Multiplayer pose pump | Movement only |
| Memory resource fixture | Its resource project |
| Job delivery UI | Delivery scenario |
| Mail UI | Mail scenario |
| Helipad travel UI | Helipad scenario |
| Shared gameplay/save behavior | Managed domain suites and backend integration |
| Water material/pattern | Terrain/core domain checks and water rendering checks |
| Water surface/depth/renderer | Terrain/core as applicable, water and movement rendering checks |
| Native terrain archive/recycling | Terrain/core, rendering, and native patch fixtures; no database/importer suites |
| Dropped item renderer/plan | Terrain/core domain checks and dropped-item rendering checks |
| Dropped item texture/cooker | Dropped-item asset and rendering checks; terrain/core domain checks |
| Rendering test fixture only | That rendering fixture group |
| Running minimap geometry | UI suite |
| Docs/concept art only | No component tests or local console build |

Large mixed files such as the game entry point still cover the runtime
scenarios they coordinate. Unknown production paths, SDK changes, and broad
build or workflow changes select full coverage. Edits to a test runner inside
one project select that project's suites. Unrecognized test fixtures fall
back to broad host coverage. Register narrow paths in the map as new features
and fixtures are added.

The managed runner accepts `--suites` with comma-separated names: `ui`, `menu`,
`text`, `trim`, `terrain`, `movement`, `dialogue`, `generation`, `life`, `saves`,
`buildings`, `core`. Omitting it still runs everything. Unknown names fail rather than passing
with zero checks.

The rendering runner also accepts `--suites`: `water`, `services`, `movement`,
`ground`, `index`, `visibility`, `attention`, `dropped-item`. No argument runs all eight; empty or
unknown selections fail. The validation plan records these separately in
`rendering`, so camera/terrain changes can retain broad geometry coverage while
material or fixture edits run only their affected groups. Mixed changes take the
union of every file's dependencies, regardless of file order.

The older inline gameplay/render/texture checks remain together
as `core`; they can be split later if their measured cost warrants it.

PR CI compares the head commit to its merge base with the PR base SHA, covering
all commits in the PR without including unrelated unreleased changes on main.
Main-branch CI and the default local runner compare all commits since the
preceding first-parent ancestor release tag
(`vmajor.minor.patch` or branch `vmajor.minor.patch+<hash>`, with legacy
four-part and `build-<hash>` compatibility),
not merely the last pushed commit. Thus an unreleased/failed commit's changes
remain in the next plan. Renames include both old and new paths. Missing history,
an invalid base or manual workflow dispatch selects full validation. Reruns exclude
the current commit's own tag when finding the preceding release.

The release workflow runs **Tests → Build → Release** for main, manual release
runs, and PRs that affect release contents or need a Dolphin candidate. Tests
runs the selected host suites and creates a bootable console candidate.
Build uses that same ISO and Depot app, compiles the backend and tools, and
prepares release files from the same clean commit. Release verifies recorded
identity and hashes before uploading. PRs with host-only tests skip the
console build and publishing. Docs-only PRs skip component checks and the
builder image. PostgreSQL starts when the plan selects backend integration.

Local menu/backend/docs-only checks do not restore the game SDK or copy native
sources. Native archive/recycling checks regenerate the AOT C source bundle and
run `patch-native.ps1` host fixtures; they do not compile a console executable.
Selected native patch fixtures run against the disc candidate during a release
build. Full validation also runs them against the independently built Depot
candidate. Cooked model admission and package verification run for every
release candidate.

Every release includes schema 3 `validation-plan.json` with its source commit,
comparison base, changed paths and selected checks. Its SHA-256 is in SHA256SUMS.txt. Stale
plans and older plan schemas are rejected by build and release scripts. The
selector's routing checks run when the routing or build gates change, or when
full coverage is requested.

Choose Dolphin/hardware scenarios for the behavior actually changed. A concept
sheet, palette adjustment or test-routing edit does not justify repeating every
movement, dialogue, backpack and migration smoke scenario. Re-run a scenario
after a related implementation change, a failure or an unresolved concern.
