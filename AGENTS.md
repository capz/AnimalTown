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

Trees must use only the user-provided tree model assets. Never generate tree
artwork or procedural tree stand-ins, impostors, replacement canopies or trunks.
Ask the user for any additional tree species, seasonal assets or lower-poly LODs,
with explicit dimensions, triangle/texture budgets and pivots. Do not silently
invent them. Seasonal floor textures do not authorize changing tree artwork.

World asset requests must stay within GameCube/3DS-era Animal Crossing types:
natural ramps, river/pond/shore banks, cliffs, waterfalls and bridges, with original
designs. See `docs/world-asset-requests.md`; do not introduce New Horizons-only
terraforming or placeable incline-kit scope.
