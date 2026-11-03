# Build delivery

Publish build artifacts in GitHub Releases, not only as local output paths or
temporary Actions artifacts. Use `tools/publish-release.ps1` after clean game and
backend builds of the same commit. The main-branch release workflow does this
automatically after its checks pass.

Every console build must produce the full-size bootable ISO with game title,
commit identity, disc banner and save banner/icon. Release its compressed ISO,
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
