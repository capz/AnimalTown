# River/coast validation

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
