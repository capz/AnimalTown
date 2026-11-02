# Build delivery

Publish build artifacts in GitHub Releases, not only as local output paths or
temporary Actions artifacts. Use `tools/publish-release.ps1` after clean game and
backend builds of the same commit. The main-branch release workflow does this
automatically after its checks pass.

Every console build must produce the full-size bootable ISO with game title,
commit identity, disc banner and save banner/icon. Release its compressed ISO,
separate backend archive, revision manifest and SHA-256 checksums. Keep development
builds marked as prereleases and report hardware validation honestly.
