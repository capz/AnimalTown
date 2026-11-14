# Build delivery

Use incremental `major.minor.patch` main versions; see `docs/versioning.md`.
The frozen baseline is `0.11.38` at `7e3dd9599709f553d13d538a320a13f3d410e861`;
each PR merged to main increments minor once and resets patch to zero,
regardless of title, merge method or implementation commit count. Major
and the baseline only change at the user's explicit request. PR builds use the
current main numeric version plus `+<12-character feature-head hash>`. Preserve
the same source/main snapshot for console, backend and release packaging.
Conventional PR titles remain useful descriptions but never determine versions.

Publish build artifacts in GitHub Releases, not only as local output paths or
temporary Actions artifacts. Use `tools/publish-release.ps1` after clean game and
backend builds of the same commit. The main-branch release workflow does this
automatically after its checks pass.

Every console build must deliver only the compact bootable NKit ISO with game
title, commit identity, disc banner and save banner/icon. Full-size source and
round-trip ISOs are temporary conversion files and must be deleted after
verification, including on failure. Publish the NKit ISO directly, without a ZIP
wrapper, alongside the
separate backend archive, revision manifest and SHA-256 checksums. Keep development
builds marked as prereleases and report hardware validation honestly.

# World art

The user has now explicitly authorized generated low-poly trees for performance,
superseding the previous supplied-model-only restriction. Use shared generated
deciduous/palm/pine meshes; keep original tree GLBs as reference but do not load
or package them in the active game. Preserve tree collision, planting and radial
world parenting. Keep geometry bounded and reuse native indexed buffers.

World asset requests must stay within GameCube/3DS-era Animal Crossing types:
natural ramps, river/pond/shore banks, cliffs, waterfalls and bridges, with original
designs. See `docs/world-asset-requests.md`; do not introduce New Horizons-only
terraforming or placeable incline-kit scope.

# Change validation

Use `tools/test-changes.ps1` for local validation; `-PlanOnly` shows the selected
checks. Run only suites and emulator scenarios relevant to the change. Broaden
coverage for related failures, unknown dependencies, or validation/build-gate
changes, rather than routinely repeating all tests. See `docs/change-validation.md`.
Docs/concept-art-only changes need no console build. When a release is built,
the same-commit ISO/backend delivery requirements above still apply.
