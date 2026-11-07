# Linux build image

The GitHub release workflow builds a `linux/amd64` toolchain image, publishes it
as `ghcr.io/capz/animaltown-build:<full-source-commit>`, and runs all compilation,
asset conversion, tests and release packaging inside that image. The runner only
needs Docker/Compose and Git checkout; it does not install host .NET, PowerShell,
devkitPPC or graphics libraries. CI uses the published image's immutable digest
and records it in the console and server build revision files.

`build/Dockerfile` pins the libogc2/devkitPPC, .NET 8, .NET 10 and PowerShell base
images by digest. GitHub CLI is versioned and its download is SHA-256 checked.
The image contains compilers/tools only: no game assets, source checkout,
credentials or firmware font bytes. Source is mounted at `/workspace`.

## Local build with the same image

Use a clean clone; packages restore from NuGet.org, and Linux containers in Docker.
These commands also work from PowerShell on Windows; no Windows SDK is needed.

```sh
docker compose -f build/compose.yml build builder
docker compose -f build/compose.yml run --rm builder
docker compose -f build/compose.yml down --volumes --remove-orphans
```

Compose starts a disposable PostgreSQL database on its private network, with no
published database port or Docker socket mounted into the builder. Test data is
kept in a temporary filesystem and removed when the database container stops.
Always run the cleanup command after success or failure. Packaging requires clean
game and backend builds of the same commit; local source edits can still be
checked with the independent `game` or `server` target before committing.

The default `all` target:

1. Converts all supplied models, fonts, icons and seasonal terrain.
2. Runs terrain import, gameplay and native memory-card regression checks.
3. Compiles the game to PowerPC, builds the apploader, produces and validates the
   full-size bootable ISO, including title/commit/disc and save banners.
4. Independently publishes the .NET 10 backend and exercises it against PostgreSQL.
5. Prepares direct full-size ISO and compact NKit ISO downloads, separate backend
   ZIP, revisions and SHA-256 checksums. Disc images have no ZIP wrappers.

Outputs remain under the mounted checkout's `out/`. CI then publishes these
packages to GitHub Releases. The source SHA tag identifies the image; `BUILD.txt`
records the exact image digest used by CI. GHCR access follows the package's
configured permissions; alternatively build the image locally from its Dockerfile.

## Separate targets

```sh
docker compose -f build/compose.yml run --rm --no-deps builder pwsh -NoProfile -File tools/build-linux.ps1 -Target game
docker compose -f build/compose.yml run --rm --no-deps builder pwsh -NoProfile -File tools/build-linux.ps1 -Target server
```

`package` packages existing clean outputs. `publish` publishes existing outputs
using an authenticated GitHub CLI or `GH_TOKEN`; `release` performs the complete
build/check/publish sequence. CI passes the token only to the final publishing
container, after checks pass. It does not expose the token to compilation/tests.

To reuse a published toolchain, set `ANIMALTOWN_BUILD_IMAGE` to its full
`ghcr.io/capz/animaltown-build@sha256:...` digest and run Compose with `--pull always`.
The regular host PowerShell build commands remain supported; their native stages
use the original libogc2 container. `-NativeInProcess` is for the combined Linux
image and runs those stages directly, with no nested Docker daemon.
