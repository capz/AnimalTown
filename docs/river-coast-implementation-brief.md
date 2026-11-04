# Approved river and coast changes

The user approved implementation after reviewing top-down/oblique and cross-section mockups. This brief records the decisions for continuation in Codex Cloud.

- Add distinct river and sea handling, rather than assigning every water cell the same geometry behavior.
- Lower both the river ground and its visible opaque water surface by 0.50 m relative to the local land level. The user chose visible-surface lowering, not a hidden-bed-only change.
- Flank river-to-land edges with continuous 0.50 m banks/walls using the existing soil/cliff art language. Close corners and bridge approaches; do not put walls between adjacent water cells.
- Keep sea water at its existing height. Do not apply river lowering or retaining banks to the sea.
- Where sea meets beach, provide a visible sand/wet-sand/surf transition.
- A sea cell's dry neighbors must be beach or a land cliff facing the water.
- Coastal cliff tiles split their footprint in half: the landward half is regular grass-topped land/cliff, the seaward half is sea. The user explicitly confirmed footprint, not the lower half of the vertical cliff face. Fit the existing 2 m cliff/section language and world-aligned material bindings.
- Keep the existing opaque GX material, two independently scrolling layers, tint/depth variation and restrained optional foam. No vertex wave simulation.
- Preserve possessions, structures, progress and edited terrain when migrating saves. Do not regenerate saved towns.

## Implementation

Implemented on the handoff branch: explicit sea/coast IDs, lowered inland water,
pooled bank geometry, half-footprint coastal sections, wet-sand beach transitions,
watertight 0.44 m opaque estuary curtains, and versioned save migration without
regeneration. See [water-rendering.md](water-rendering.md) for geometry, migration
policy, regression coverage and the read-only Dolphin inspection tour.

The initial checkpoint passed terrain/generation/save/core checks. Its first
native build exposed missing nested-container proxy settings in the cloud setup;
the session build wrapper supplies the inherited proxy and trusted CA. The .NET
10 SDK was installed for the matching backend. These are environment setup
changes outside the repository.

## Versioning and delivery

Use the branch's `xx.yy.zz.bb` workflow. `xx` remains 0: the user's earlier “use 1” referred to the water-height option, not a major-version request. Features use `feat:` and fixes use `fix:`; counters are independent. Read AGENTS.md and docs/versioning.md.

Existing open PRs: #1 is the original scrolling-water material; #2 is versioning. This branch contains their changes for implementation convenience. Do not merge PRs or push main without user authorization. Complete the approved changes on the handoff branch, run the relevant checks, commit, push, open/update a review PR and publish matching development build artifacts. Report physical hardware validation honestly.
