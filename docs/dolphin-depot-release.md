# AnimalTown Dolphin Depot release plan

Reviewed on 17 November 2026 against AnimalTown main commit
`b2f1f2b0e4f69e3eedac89279bc50904e46f53bb` and DolphinDepot/registry commit
`fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1`. Published-asset inspection used
`v0.44.0` at `a310cb8fde48db9c3ce5fe52a0728ff120b5cc57`.

Main advanced during the review: [PR 105][A21] removed the Sunshine water
renderer and its assets. The implementation inventory below reflects that
removal; do not restore or package the retired resources for Depot.

## Release decision

**AnimalTown is not ready for a standard Dolphin Depot installation yet.** The
main work is to make the game executable run from an installed app directory and
read its accompanying assets. Its present startup requires a game disc, while
the Depot convention requires a runnable `apps/animaltown/default.dol`.

The recommended implementation is an additional DOL and cooked-assets release
package, using the existing native build and asset pipeline. Keep the NKit
distribution available. Establish redistribution terms, add the registry entry,
and complete hardware verification before submitting the release.

This document records requirements and implementation work. It does not change
the current NKit-only delivery instruction, grant a license, implement an app
profile, or certify hardware compatibility. The future packaging change must
deliberately reconcile the existing delivery policy with the additional profile.
See the [current project instructions][A1], [published release][A2], and
[Dolphin Depot distribution guide][D1].

## What Dolphin Depot actually requires

The brief distribution guide describes the installed layout. Submission rules,
catalog metadata, and registry checks come from the linked submission guide and
the registry itself. Keep these distinctions when assessing readiness.

| Requirement | Classification | AnimalTown action |
| --- | --- | --- |
| Install the primary executable at `/apps/{slug}/default.dol`. | Distribution requirement | Ship the actual game DOL as `/apps/animaltown/default.dol`, with working access to all game content. |
| Use a valid, unique slug. | Registry requirement | Use `animaltown`. The guide allows underscores, but the registry validator accepts only `^[a-z0-9]+(-[a-z0-9]+)*$`. |
| Put `opening.bnr` beside the executable. | Strong recommendation | Reuse AnimalTown's existing generated banner and verify it in a launcher. |
| Provide a public, stable download source and permission to redistribute the payload. | Submission requirement for hosted installs | Continue using GitHub Releases and resolve the project's redistribution terms. Depot copies releases to its CDN. |
| Add `registry/animaltown/manifest.json`. | Registry requirement | Define accurate metadata, source selection, and ordered packaging treatments. |
| Run the registry validator and pass the indexer packaging checks. | Submission and CI checks | Validate the entry and inspect the resulting installed file tree. |
| Download through the GC client on a real GameCube and confirm boot. | Submission checklist | Record this exact test; emulator results and manual copying are supporting tests. |
| Supply catalog PNGs. | Optional assets with fixed specifications | Reuse approved artwork; dimensions and filenames are listed below. |

Sources: [distribution guide][D1], [submission guide][D2], [registry README][D4],
[slug validator][D5], [PR checklist][D6], and [PR validation workflow][D9].

The final Depot package is a ZIP assembled by its indexer. An upstream ZIP is a
convenient choice for AnimalTown, not a universal source-format requirement:
Depot also accepts other source types, and its [Spout entry][D10] downloads a raw
DOL. There is no documented treatment that turns AnimalTown's disc-dependent
NKit release into a working app-directory game. Renaming an ISO or the separate
updater DOL to `default.dol` does not solve the runtime dependency.

The guide does not impose an incoming `argv` contract, a save directory, a
return-to-loader API, or a semantic-version scheme. The runtime and persistence
work below is AnimalTown engineering needed for the proposed delivery route.

## Current project gaps

### Startup and asset loading

[`Program.cs`][A3] has a parameterless `Main()` and immediately calls
`Storage.MountDisc()`, throwing if the disc cannot be mounted. The game then
loads runtime configuration and assets from `dvd:/`. A DOL copied out of the
current build would therefore still depend on the disc environment.

Asset access is spread across startup, UI, rendering, model variants, water,
and save artwork. Route all installation-content reads through one content-root
abstraction; changing only `Program.Main` is insufficient.

| Code area | Files to inspect and change |
| --- | --- |
| Initial mount and runtime profile | `game/AnimalTown.GameCube/Program.cs`, `RuntimeOptions.cs` |
| Splash and interface assets | `StartupSplash.cs`, `UiFont.cs`, `TitleScreen.cs`, `ProfilePickerUi.cs`, `OnboardingUi.cs` |
| World and dropped-item resources | `TerrainRenderer.cs`, `TerrainWalls.cs`, `DroppedItemRenderer.cs`, `PathTextureResidency.cs` |
| Model and texture paths | `ModelResources.cs`, `TextureCache.cs`, `shared/AnimalTown.Domain/TextureResolution.cs` |
| Save banner and icon reads | `TownSession.cs`, `CardArchive.cs`, and `Program.cs` |
| Disc-only operations | `UpdaterLaunch.cs` and the code exposing that operation |

Unless a full path is shown, filenames in this table are under
`game/AnimalTown.GameCube/`. The current sources include direct, indirect,
synchronous, and asynchronous loads. Some build audit records and host tests
also use `dvd:/` as a provenance identifier; distinguish those identifiers from
actual runtime paths. See [runtime configuration][A4], [texture policy][A5],
[splash loading][A6], and [terrain resource loading][A7].

### Build and release packaging

[`tools/build-game.ps1`][A8] already produces `out/native/Game.dol` and cooked
resources in `out/disc` before constructing the NKit image. These are the inputs
for the additional app package. The published `v0.44.0` assets include the NKit
image and `animal-town-updater.dol`, but no standalone game app package.

The NKit-only rule is reinforced by `AGENTS.md`, README,
`.github/workflows/releases.yml`, and `tools/publish-release.ps1`. Reconcile these
together when implementing the new profile. Retain existing source-identity,
version, checksum, and NKit verification behavior. The [publisher][A9] and
[release workflow][A10] need an explicit additional asset selection.

### Banner and catalog artwork

Banner generation is already present. The [tooling's `Disc()` method][A11]
creates a 6496-byte BNR1 file containing a 96 by 32 RGB5A3 image, display title,
contributor metadata, and version information. Extract or reuse this generation
step so app packaging can obtain `opening.bnr` without depending on successful
ISO construction. Validate the existing output before planning replacement art.

The launcher banner and catalog images are separate deliverables:

| File | Specification | Location and purpose |
| --- | --- | --- |
| `opening.bnr` | GameCube banner with 96 by 32 image | Beside `default.dol`; recommended launcher metadata. |
| `banner.png` | Exactly 96 by 32, PNG | Optional registry catalog icon. |
| `screenshot0.png` to `screenshot2.png` | Up to three, each exactly 640 by 480, PNG | Optional registry gameplay screenshots. |
| `hero.png` | Exactly 640 by 160, PNG | Optional registry app-detail background. |

Use the existing visual identity and approved assets. Capture screenshots from
the build being submitted. Changes to game UI or newly proposed artwork follow
the project's mockup and approval workflow; this plan itself creates no UI.
Specifications come from the [registry README][D4] and [submission checklist][D6].

### Saves and updates

Current persistence uses memory-card Slot A and identity `AMTE` / `01`. The
consolidated physical file is `AnimalTown`; town and player state, options, and
debug configuration are logical records inside it. Preserve that identity,
record compatibility, and explicit-save behavior. App installation should not
require a new save format or automatic migration to SD. The [consolidated-save
document][A12] and current implementation take precedence over older README
references to separate card files.

Account for the physical slot layout when qualifying storage. An SD Gecko in
slot A occupies the slot currently used for AnimalTown's save memory card. Start
with SD2SP2 plus a memory card in slot A, and SD Gecko in slot B plus a memory
card in slot A, as candidate configurations to test. Slot-A SD Gecko support
with saving needs a separately designed and tested save-slot policy. Do not
advertise unrestricted SD Gecko compatibility while retaining the current
slot-A-only persistence behavior. The slot selection is explicit in
[`UserOptionsStorage.cs`][A18] and [`DevConfigStorage.cs`][A19].

The existing updater is specifically an ISO updater. [`UpdaterLaunch.cs`][A13]
loads its executable from `dvd:/` or GC Loader and hands off the installed
version. Its documented workflow replaces game images on GC Loader storage.
It is not a Depot app updater.

For the first Depot profile, use Depot to update the installed app and gate the
ISO replacement operation by the actual installation type. Preserve the current
updater for the disc profile. Before changing the visible update control, prepare
the required mockup and obtain approval. Do not simply rename or relabel the
existing updater and leave its ISO behavior active. See [updater design][A14].

### Redistribution and feature claims

At the reviewed commit, the [repository tree][A20] has no top-level project license.
Component-specific notices do not establish a release-wide redistribution grant.
Resolve this before a hosted Depot submission: publish the selected project
terms or explicit redistribution permission, and record the rights and required
notices for every shipped code component, model, texture, font, sound, and logo.
This is an unresolved release requirement, not a finding that an asset is
unauthorized. Do not select a license on the owner's behalf.

Depot accepts redistribution permission; it does not require every project to
adopt an open-source license. `NOASSERTION` can describe unresolved licensing in
metadata, but does not itself supply permission. See the [submission guide][D2],
[PR checklist][D6], and [license schema][D7].

The [current README][A15] explicitly says the console has no network client and
the separate server is a foundation. Describe the release accordingly. Completing
online community features is not a Depot packaging requirement. Do not list
multiplayer, online visits, or networking peripherals as implemented game
capabilities until verified in the release being distributed. Depot's own need
for a network connection is separate from AnimalTown's gameplay requirements.

## Implementation work

### Phase 1 Resolve the installation contract

Use `animaltown` as the proposed stable install slug. Keep display title, banner
metadata, manifest name, and release notes consistent. If the public title changes,
settle the first listing's name before submission; changing an established slug
later requires an explicit update and migration plan.

Define two supported modes: the existing disc image and the proposed app
directory. The content root must be selected once before runtime configuration,
splashes, or asset-backed UI is initialized. All subsequent loads must use that
same root, including texture and model residency requests.

For app startup, prefer a verified launcher-provided executable path when
available. Resolve and mount the corresponding device, then use the executable's
directory. Do not assume the working directory points there. For loaders that
provide no usable path, probe only the fixed `apps/animaltown` location on
supported storage devices. Check for a versioned package identity and required
sentinel files. Specify deterministic behavior when more than one valid install
exists; never silently combine assets from different devices or versions.

The disc profile should continue to use a mounted disc. An app install must be
able to boot with no AnimalTown image mounted, and should not accidentally use
an unrelated disc as its content source.

The reviewed game pins DolphinDotNet `0.1.0-preview.372.1`. Verify its incoming
launch-context support and storage drivers before choosing API calls. The
existing `tools/native-launch-arguments.h` validates the libogc argument ABI,
but its presence does not prove an active managed incoming-arguments API. The
native patch explicitly notes that incoming arguments are no longer used for
update-image selection. Changing the C# entrypoint signature alone is not a
verified solution. If the pinned SDK needs launch-context support, implement the
reusable facility in DolphinDotNet and consume it from AnimalTown. See the
[native patch][A16] and [argument helper][A17].

Only claim storage devices supported by the actual SDK and tested build.
Dolphin Depot naming a device in its guide does not establish that AnimalTown
can mount it. Launcher path aliases also need validation against the SDK's
mount names. No exact new SDK API is prescribed by this documentation.

### Phase 2 Make all content reads portable

Introduce one resolver that joins a selected root to a relative cooked filename.
For the first implementation, retaining the existing flat cooked asset names
minimizes changes. The same resource should resolve to `dvd:/<name>` in disc
mode or `<mounted-device>:/apps/animaltown/<name>` in app mode.

Audit the files listed above and all consumers of shared path helpers. Preserve
resource-cache keys, asynchronous reads, texture variants, disposal, and memory
budgets. Avoid repeatedly searching devices or constructing fallback paths in
render loops. Streaming behavior must be tested on the intended SD storage;
an emulator boot does not establish acceptable loading performance there.

Keep writable save state separate. Load save artwork from the selected content
root, but keep the existing card identity and record store. Missing assets must
produce a useful startup failure rather than loading a different build's files.
Reuse existing diagnostics; any proposed new error screen needs UI review.

### Phase 3 Produce an additional release artifact

Proposed upstream asset name: `AnimalTown-<version>-depot.zip`.

| Installed path inside the ZIP | Build input or purpose |
| --- | --- |
| `apps/animaltown/default.dol` | The game executable from `out/native/Game.dol`. |
| `apps/animaltown/opening.bnr` | Shared banner-generation output. |
| `apps/animaltown/runtime.cfg` | The selected runtime profile. |
| `apps/animaltown/<cooked-resource>` | Required production assets using existing flat names. |
| `apps/animaltown/savebanner.rgb5a3` and `saveicon.rgb5a3` | Existing memory-card artwork. |
| `apps/animaltown/BUILD.txt` and applicable version metadata | Source/version identity consistent with the DOL. |
| Applicable license and notice files | Actual terms for the shipped payload. |

This layout is a project proposal; Depot only specifies the primary executable
location and recommends the adjacent banner. Use an explicit runtime payload
list. Exclude the backend, host tools, artist source files, development credentials,
temporary ISOs, retired Sunshine resources, and the ISO updater from the initial app package. Keep notices
for every included component; do not blindly copy or drop all disc-stage files.

Add the artifact to `tools/publish-release.ps1`, `SHA256SUMS.txt`, the release
workflow, installation documentation, and the delivery policy. Preserve the
existing versioning rules and same-source relationship between console builds
and backend packaging. Do not create a second version counter for Depot.

Validate the archive after extraction to a clean directory. Check filename case,
one default executable, required assets, banner validity, source identity, and
absence of an extra wrapper directory. The manifest pattern must match only the
Depot package, never the server, authoring tools, or NKit image.

### Phase 4 Prepare the registry entry

Create the submission under `registry/animaltown/` in a fork of
`DolphinDepot/registry`, then open its PR against `main` when the release is
ready. The AnimalTown implementation/documentation PR and this external registry
submission are separate changes.

Required metadata includes `name`, `author`, `description_short`, `category`,
`license`, and `treatments`; hosted installs also need `source`. Use category
`games`. Keep the short description within 160 characters and the optional long
description within 500. Platform and peripheral arrays must use schema values
and accurately describe tested support. Do not claim compatibility from enum
availability alone. See the [manifest schema][D7].

All manifest string values must be ASCII, including descriptions and notes.
Do not use smart punctuation or assume JSON Unicode escapes bypass validation.
Top-level extra properties are rejected, so do not add conventional fields such
as `$schema`, `version`, `website`, or `icon` unless the schema changes. Some
prose fields also reject leading or trailing whitespace. See the [ASCII
validator][D8] and [schema][D7].

Suggested short description for the current feature scope:

> A village-life game for GameCube with animal villagers, town exploration, and memory-card saves.

The following is a proposed **source and treatments fragment**, not a complete
submission. Metadata and verified licensing still need to be supplied:

```json
{
  "source": {
    "type": "github_release",
    "repository": "capz/AnimalTown",
    "asset_pattern": "AnimalTown-*-depot.zip"
  },
  "treatments": [
    {
      "treatment": "archive.extract",
      "arguments": ["AnimalTown-*-depot.zip", "extracted/"]
    },
    {
      "treatment": "contents.move",
      "arguments": ["extracted/apps/animaltown/", "apps/animaltown/"]
    }
  ]
}
```

`archive.extract` populates the indexer's work directory. `contents.move`
promotes the selected directory contents into its final distribution directory;
the trailing slash is intentional. An extraction-only recipe does not populate
the final package. This proposal assumes `opening.bnr` is already in the upstream
ZIP. If it is instead maintained with the registry entry, add the documented
`registry.copy` operation. See [treatment semantics][D3].

Check release selection before relying on automatic updates. The documented
GitHub source offers `ignore_versions` for exact tags or glob patterns; the
inspected documentation does not establish how prereleases are otherwise
filtered. Verify that the chosen resolver selects the intended public release
and that the matching Depot asset exists. Future releases should retain the
slug and asset naming convention.

An external-only catalog entry is supported for software the registry cannot
redistribute, but it offers a manual download link instead of the normal hosted
installation. It would not complete the standard install experience targeted
by this plan. Do not use it to conceal missing runtime or permission work.

### Phase 5 Validate and submit

Run `npm ci`, `npm run lint:json`, and `npm run validate -- --all` in the prepared
registry checkout. The validator covers manifest existence, JSON/schema, ASCII
values, and slug syntax. Its default change-based selection can miss a newly
created untracked manifest; `--all` includes entry directories directly. Confirm
that AnimalTown appears in the results. Validation does not prove that assets
download, that media dimensions are correct, or that the game boots. See the
[validator's entry selection][D12].

The registry PR workflow also runs the indexer's `test-manifest` command and
reports the resulting distribution contents. Review the actual installed tree
and test the generated payload. Follow the current registry workflow when
performing that test locally; credentials, invocation, and container versions
should come from that workflow. See [registry validation][D9] and the
[indexer test script][D11].

Record hardware evidence for the exact payload downloaded through the GC client:
release tag and checksum, Depot client version, GameCube model, storage device,
launcher, and result. The PR checklist's real-console download-and-boot item
must remain unchecked until that test actually happens. If a first submission
needs a preview package to test through the client, use the maintainer-supported
preview procedure or resolve it during review; do not invent one.

## Acceptance checklist

The following combines Depot submission requirements with AnimalTown's own
release criteria. Items concerning full gameplay, saves, and regression testing
are project acceptance criteria, not claims about the short distribution guide.

### Required for a hosted release

- [ ] Project and bundled-content redistribution permission is documented.
- [ ] A public, stable release contains the intended Depot artifact.
- [ ] The final package has `apps/animaltown/default.dol` containing the game.
- [ ] The game boots from that installation without an AnimalTown image mounted.
- [ ] All required resources load from the selected installation and build.
- [ ] The registry entry has valid metadata, ASCII strings, source, and treatments.
- [ ] Registry validation and indexer packaging checks pass for the submitted entry.
- [ ] A real GameCube downloads the app through the GC client and boots it.
- [ ] Compatibility and feature descriptions match the tested release.

### AnimalTown acceptance and regression checks

- [ ] Cold launch works with usable launcher arguments and with no arguments.
- [ ] Multiple-device and multiple-install handling is deterministic.
- [ ] SD Gecko slot placement is compatible with the documented save-card slot.
- [ ] Slow or absent secondary devices do not cause an unbounded startup search.
- [ ] Splash, title, portraits, onboarding, and existing-town loading succeed.
- [ ] Models, texture variants, ground, cliffs, paths, water, and dropped items load.
- [ ] Memory-card banners/icons load from the selected package.
- [ ] Existing `AMTE` / `01` saves, settings, and debug records remain usable.
- [ ] Updating the app preserves saved progress and unrelated storage content.
- [ ] The Depot install cannot invoke ISO replacement through the old updater.
- [ ] The NKit profile retains startup, gameplay, saves, and its updater behavior.
- [ ] Release artifacts, version metadata, and checksums identify the same source.
- [ ] Hardware results and outstanding device limitations are recorded honestly.

### Recommended presentation

- [ ] `opening.bnr` displays correctly beside the DOL in the tested launcher.
- [ ] Any submitted catalog PNGs have the required names and exact dimensions.
- [ ] Screenshots and descriptions reflect the release users will download.

## Suggested implementation sequence

1. Resolve licensing and agree the additional delivery profile; record the stable
   slug, initial supported storage, and updater behavior.
2. Prove SDK launch context and storage mounting, then implement the shared
   content root and replace disc-bound runtime paths.
3. Add the app artifact, reuse the banner generator, and integrate publishing
   and targeted package validation while preserving NKit delivery.
4. Complete app and disc regression tests, then test the exact app installation
   on the supported hardware.
5. Prepare catalog assets and the registry PR, resolve any review findings, and
   complete the required GC-client installation evidence.

A successful host build or ISO smoke test alone does not close the new app
startup gate. Use `tools/test-changes.ps1` and the repository's change-selection
policy for implementation work, adding only tests that cover the new paths and
concrete regression risks. This documentation-only change needs no console build
under the current `AGENTS.md`.

## Sources

Official pages were read on 17 November 2026. Repository evidence is pinned to
the commits stated at the beginning so that later changes do not silently alter
this assessment.

- [Dolphin Depot distribution guide][D1]
- [Dolphin Depot submission guide][D2]
- [Dolphin Depot manifest reference][D3]
- [Registry README and asset specifications][D4]
- [Registry slug validator][D5]
- [Registry submission checklist][D6]
- [Registry JSON schema][D7]
- [Registry ASCII validator][D8]
- [Registry PR workflow][D9]
- [Registry raw-DOL example][D10]
- [Registry indexer test script][D11]
- [Registry validator entry selection][D12]
- [AnimalTown project instructions][A1]
- [AnimalTown release v0.44.0][A2]
- [AnimalTown startup][A3]
- [AnimalTown runtime configuration][A4]
- [AnimalTown texture-path policy][A5]
- [AnimalTown splash resource loading][A6]
- [AnimalTown terrain resource loading][A7]
- [AnimalTown game build][A8]
- [AnimalTown release publisher][A9]
- [AnimalTown release workflow][A10]
- [AnimalTown banner and disc tooling][A11]
- [AnimalTown consolidated memory-card saves][A12]
- [AnimalTown updater launch][A13]
- [AnimalTown updater documentation][A14]
- [AnimalTown README and feature status][A15]
- [AnimalTown native patch][A16]
- [AnimalTown launch-argument helper][A17]
- [AnimalTown options storage][A18]
- [AnimalTown debug configuration storage][A19]
- [AnimalTown repository tree][A20]
- [AnimalTown removal of retired water resources][A21]

[D1]: https://dolphindepot.org/docs/distribute/
[D2]: https://dolphindepot.org/submit/
[D3]: https://dolphindepot.org/docs/manifest-schema/
[D4]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/README.md
[D5]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/scripts/checks/slug-valid.ts
[D6]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/.github/PULL_REQUEST_TEMPLATE.md
[D7]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/manifest.schema.json
[D8]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/scripts/checks/manifest-ascii.ts
[D9]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/.github/workflows/validate-pr.yml
[D10]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/registry/spout/manifest.json
[D11]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/.github/scripts/test-manifests.sh
[D12]: https://github.com/DolphinDepot/registry/blob/fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1/scripts/validate.ts
[A1]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/AGENTS.md
[A2]: https://github.com/capz/AnimalTown/releases/tag/v0.44.0
[A3]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/Program.cs
[A4]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/RuntimeOptions.cs
[A5]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/shared/AnimalTown.Domain/TextureResolution.cs
[A6]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/StartupSplash.cs
[A7]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/TerrainRenderer.cs
[A8]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/tools/build-game.ps1
[A9]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/tools/publish-release.ps1
[A10]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/.github/workflows/releases.yml
[A11]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/tools/AnimalTown.Tools/Program.cs
[A12]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/docs/consolidated-card-save.md
[A13]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/UpdaterLaunch.cs
[A14]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/docs/updater-build.md
[A15]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/README.md
[A16]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/tools/patch-native.ps1
[A17]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/tools/native-launch-arguments.h
[A18]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/UserOptionsStorage.cs
[A19]: https://github.com/capz/AnimalTown/blob/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb/game/AnimalTown.GameCube/DevConfigStorage.cs
[A20]: https://github.com/capz/AnimalTown/tree/b2f1f2b0e4f69e3eedac89279bc50904e46f53bb
[A21]: https://github.com/capz/AnimalTown/pull/105
