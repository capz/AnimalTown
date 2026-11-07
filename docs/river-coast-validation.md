# River/coast validation

Inland banks now lean 0.35m into the blocked water footprint. Cardinal slopes
share concave miters; triangular bevels close convex corners, and height-step
junctions are closed separately. The narrow upper border reuses the adjacent
land's texture, seasonal tint and cloud material. Soil uses the existing shared
cliff texture and pooled geometry. No terrain IDs, saved layouts, land heights,
water levels or bridge approach widths change.

Foam follows the new waterline at the bank toe, with non-overlapping corner
bands. Players and villagers still use the water collision rules; exact segment
clipping also rejects very short diagonal cuts through a wet corner. Managed
checks exercise all 256 local shore topologies, convex/concave joins, narrow
channels, different-height banks, bridge clearance and movement blocking.

The handoff integration checkpoint was checked before implementation. Its
terrain, generation, save and core suites passed (133,106 checks); the first
native attempt exposed missing proxy routing in nested cloud containers. The
session supplies the inherited proxy and trusted CA to those containers, and
installs the .NET 10 SDK required by the backend. No SDK/engine source changes
were needed. Main's diagonal-cliff commit bd6488c is integrated, retaining its
collision, saved elevation modes and ground-cap bindings alongside water.

`tools/test-changes.ps1 -NativeInProcess` passes the selected full dependency set:
582,272 managed checks, validation-routing/versioning checks, terrain importer and
texture repacker checks, native memory-card/texture/UV/dialogue checks, Python
scenario syntax and PostgreSQL/backend integration. The regressions include:

- exact river/sea levels and bridge water continuation;
- all four half-footprint coast orientations and sea-neighbor constraints;
- a 68-seed connected-town/topology sweep;
- v5 combined / v1 split migration without regeneration or loss of retained edits,
  progress, possessions or elevations;
- new-ID round trips and invalid legacy/current terrain-ID rejection.

The direct-ISO Dolphin tour captures river banks, corners, bridge approaches,
wet sand, sea-facing coastal cliffs, the estuary, return to normal play and menu
material state. Its river animation assertion compares stationary captures eight
seconds apart. Initial visual review found an under-bridge sky gap; the surface
now continues beneath bridge decks. The updated tour visually verifies closure; its stationary river captures contain
47,978 water pixels, with 45,925 changing over eight seconds.

Build delivery uses the existing clean-commit release pipeline, explicitly enabled
for `feat/river-coast-terrain`. It packages the full 1,459,978,240-byte bootable ISO,
matching .NET server, tools, cliff kit, revision/version/validation manifests and
SHA-256 checksums as a GitHub prerelease. The root major remains zero.

Physical GameCube testing has not been performed. The mouth is a stylized static
0.44 m tidal step with opaque water closure, not a flow simulation. Historical
dry shore edits remain authored terrain and are not repainted during migration.
