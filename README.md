# AnimalTown

GameCube village life game built on [hypercube-engine](https://github.com/capz/hypercube-engine).
No Unity or WebRTC. Current state: early implementation; see the explicit
[implementation status](docs/implementation-status.md) and [technical design](docs/technical-design.md).
The product brief is [design-doc.md](design-doc.md).

## Downloads and releases

Build artifacts are delivered through [GitHub Releases](https://github.com/capz/AnimalTown/releases).
Each successful push to `main` builds and checks the console and backend, then
publishes a commit-tagged development prerelease. The release contains a compressed
full-size GameCube ISO, a separate .NET 10 server ZIP, `BUILD.txt` with the engine
and SDK revisions, and `SHA256SUMS.txt`. Extract the ISO before loading it through
GCLoader/Swiss or writing it to suitable media. Development status and physical
hardware validation are recorded in the release notes.

To publish a locally verified build, use a clean checkout, run both build commands
below, then run `./tools/publish-release.ps1` with an authenticated GitHub CLI.
`-PackageOnly` prepares archives without publishing. The publisher rejects dirty
or mismatched console/backend revisions, verifies the ISO structure, uploads into
a draft, checks uploaded asset hashes, and only then publishes the prerelease.
The workflow can also be rerun from GitHub Actions using **Build and release**.

## Build the GameCube ISO

Windows requirements: Git, .NET 8 and 10 SDKs, PowerShell 7 and Docker Desktop
with Linux containers running. Native tools are pinned by image digest.

```powershell
git submodule update --init --recursive
./tools/build-game.ps1
```

A successful console build always produces `out/AnimalTown-<commit>.iso`
(1,459,978,240 bytes), with bootable homebrew apploader, DOL, assets, opening.bnr,
save artwork and BUILD.txt. Boot this ISO directly in Dolphin/GCLoader/Swiss.
Physical hardware validation remains outstanding. Rebuilds can use
`-SkipAssets` only when converted assets already exist for the same sources.
Dirty source is marked in all build identities. Generated assets/ISOs stay out
of Git. Original asset sources are retained in `assets/models`.

Controls: stick walk, B run/cancel, A interact/confirm, X backpack, Y map, Z equip
tool, Start save/settings; D-pad selects and L/R changes menu tabs. Memory-card
saves use Slot A, `AMTE/01`, two alternating files, banner and icon. Missing-card
play works; save errors are shown for retry.

## Build the separate server

```powershell
./tools/build-server.ps1
$env:ConnectionStrings__Game='Host=localhost;Database=animaltown;Username=animaltown;Password=YOUR_LOCAL_PASSWORD'
dotnet out/server/AnimalTown.Server.dll --urls http://127.0.0.1:5189
```

Use TLS ingress before exposing the service. The current console has no network
client; this service is an independently tested foundation. Do not deploy it as
a finished online game. Build a server image with
`docker build -f server/AnimalTown.Server/Dockerfile -t animaltown-server .`.

## Checks

```powershell
dotnet run --project tests/AnimalTown.Checks -c Release
./tests/server-integration.ps1
```

The integration command starts and removes its own local PostgreSQL test
container. It uses ports 55439 and 5189. To inspect a direct ISO boot in a Linux
container:

```powershell
docker build -t animaltown-dolphin -f vendor/hypercube-engine/tools/Dockerfile.dolphin vendor/hypercube-engine
docker run --rm --init --shm-size=512m --mount "type=bind,src=$PWD,dst=/work" -w /work animaltown-dolphin python3 tools/iso-smoke.py out/AnimalTown-COMMIT.iso out/smoke --play
```

Inspect screenshots and logs; smoke script capture alone is not a gameplay
success assertion. Runtime uses the pinned SDK's custom AOT APIs, so host build
success alone is not console validation. `tools/patch-native.ps1` adds the small
RTC bridge, sector-aligned card reader fix and card diagnostics to the pinned native bundle, configures the managed heap, and hides startup diagnostics;
it fails if the expected AOT signature changes.

The native IPL font API is supplied by [DolphinDotNet PR #145](https://github.com/capz/DolphinDotNet/pull/145), pinned by the SDK submodule. Its CI checks pass. Firmware font data is read on the console and is never bundled on disc.
