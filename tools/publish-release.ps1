param(
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [string]$Repository='capz/AnimalTown',
    [string]$Gh='gh',
    [switch]$PackageOnly
)
$ErrorActionPreference='Stop'
function Get-ReleaseBannerHashes([string]$Opening,[string]$Pal,[object]$NKitInfo,[string]$Version){
    $openingBytes=[IO.File]::ReadAllBytes($Opening)
    $palBytes=[IO.File]::ReadAllBytes($Pal)
    if($openingBytes.Length -ne 6496 -or [Text.Encoding]::ASCII.GetString($openingBytes,0,4) -cne 'BNR1' -or
       $palBytes.Length -ne 8096 -or [Text.Encoding]::ASCII.GetString($palBytes,0,4) -cne 'BNR2'){
        throw 'Release banners must be complete BNR1 and BNR2 files'
    }
    $openingHash=(Get-FileHash -LiteralPath $Opening -Algorithm SHA256).Hash.ToLowerInvariant()
    $record=@($NKitInfo.files | Where-Object { $_.file -ceq 'opening.bnr' })
    if($record.Count -ne 1 -or $record[0].bytes -ne $openingBytes.Length -or $record[0].sha256 -cne $openingHash){
        throw 'Release opening.bnr differs from the verified NKit disc payload'
    }
    $title=[Text.Encoding]::ASCII.GetString($openingBytes,0x1860,64).TrimEnd([char]0)
    if($title -cne "Smalltown Forest / $Version"){throw 'Release banner has stale branding or version metadata'}
    # Bind the PAL alternative to the same verified disc artwork and release
    # metadata; all six slots intentionally contain the English fallback.
    if([Convert]::ToBase64String($openingBytes,4,0x181c) -cne [Convert]::ToBase64String($palBytes,4,0x181c)){
        throw 'PAL banner artwork differs from the verified disc banner'
    }
    $english=[Convert]::ToBase64String($openingBytes,0x1820,0x140)
    for($language=0;$language -lt 6;$language++){
        if([Convert]::ToBase64String($palBytes,(0x1820+$language*0x140),0x140) -cne $english){
            throw 'PAL banner metadata differs from the verified release'
        }
    }
    return [pscustomobject]@{opening=$openingHash;pal=(Get-FileHash -LiteralPath $Pal -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$Root=(Resolve-Path -LiteralPath $Root).Path
$commit=(& git -C $Root rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$'){throw 'Cannot determine source commit'}
if(& git -C $Root status --porcelain --untracked-files=normal){throw 'Release packaging requires a clean source checkout'}
. (Join-Path $PSScriptRoot 'version.ps1')
$identity=Get-BuildVersion $Root
if(!$identity.canPublish){throw 'Main releases must end at a merged PR boundary; direct main commits do not advance the patch'}
Write-VersionMetadata $identity $Root
$version=$identity.version
$versionPattern=[regex]::Escape($version)
$validation=Get-Content (Join-Path $Root 'out/validation-plan.json') -Raw|ConvertFrom-Json
if($validation.schema -ne 2 -or $validation.commit -ne $commit){throw 'Release validation plan must match source commit'}
$tag=$identity.tag
$out=Join-Path $Root 'out'
$revisions=[IO.File]::ReadAllText((Join-Path $out 'build-revisions.txt'))
$server=Join-Path $out 'server'
$serverBuild=[IO.File]::ReadAllText((Join-Path $server 'BUILD.txt'))
if($revisions -notmatch "(?m)^Version: $versionPattern\r?$" -or $serverBuild -notmatch "(?m)^Version: $versionPattern\r?$" -or
   $revisions -notmatch "(?m)^Game source: $commit\r?$" -or $revisions -notmatch '(?m)^Dirty: False\r?$' -or
   $serverBuild -notmatch "(?m)^Server source: $commit\r?$" -or $serverBuild -notmatch '(?m)^Dirty: False\r?$'){
    throw 'Console and backend must both be clean builds of the release commit'
}
$nkit=Join-Path $out "AnimalTown-$version.nkit.iso"
$nkitValidation=Join-Path $out 'nkit-validation.json'
& dotnet run --project (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-build -- verify-nkit-output $Root $nkit $nkitValidation
if($LASTEXITCODE -ne 0){throw 'Built NKit image/report validation failed'}
$nkitInfo=Get-Content $nkitValidation -Raw|ConvertFrom-Json
$openingSource=Join-Path $out 'disc/opening.bnr'
$palSource=Join-Path $out 'brand/opening-pal.bnr'
$bannerHashes=Get-ReleaseBannerHashes -Opening $openingSource -Pal $palSource -NKitInfo $nkitInfo -Version $version
$package=Join-Path $out "releases/$tag"
New-Item -ItemType Directory -Force -Path $package | Out-Null
$titleEvidenceAssets=@()
$titleEvidenceRoot=Join-Path $out 'title-logo'
$titleEvidenceBuild=Join-Path $titleEvidenceRoot 'title-logo-build.json'
if(Test-Path -LiteralPath $titleEvidenceBuild -PathType Leaf){
    $evidence=Get-Content -LiteralPath $titleEvidenceBuild -Raw|ConvertFrom-Json
    if($evidence.schema -ne 1 -or $evidence.commit -cne $commit -or $evidence.version -cne $version -or
       $evidence.tag -cne $tag -or $evidence.isoName -cne [IO.Path]::GetFileName($nkit) -or
       $evidence.isoSha256 -cne $nkitInfo.compactSha256){
        throw 'Title-logo evidence must match the release commit, version and verified NKit image'
    }
    $evidenceNames=@('title-logo.png','title-logo-picker.png','title-logo-name.png','title-logo-return.png','title-logo-report.json','title-logo-build.json')
    foreach($name in @('title-logo-debug.png','title-logo-status.png')){
        if(Test-Path -LiteralPath (Join-Path $titleEvidenceRoot $name) -PathType Leaf){$evidenceNames+=$name}
    }
    foreach($name in $evidenceNames){
        $source=Get-Item -LiteralPath (Join-Path $titleEvidenceRoot $name)
        if($source.PSIsContainer -or $source.Length -le 0){throw "Missing or empty title-logo evidence: $name"}
        $destination=Join-Path $package $name
        Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
        $titleEvidenceAssets+=$destination
    }
}
$serverZip=Join-Path $package "AnimalTown-server-$version.zip"
$toolsDirectory=Join-Path $out 'tools-published'
& dotnet publish (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-self-contained -o $toolsDirectory -v quiet
if($LASTEXITCODE -ne 0){throw 'Offline tool publication failed'}
Copy-Item -LiteralPath (Join-Path $Root 'docs/texture-repacker.md') -Destination (Join-Path $toolsDirectory 'texture-repacker.md') -Force
Copy-Item -LiteralPath (Join-Path $Root 'docs/uv-editor-design.md') -Destination (Join-Path $toolsDirectory 'uv-editor-design.md') -Force
Copy-Item -LiteralPath (Join-Path $out 'updater-provision.py') -Destination (Join-Path $toolsDirectory 'provision-updater.py') -Force
Copy-Item -LiteralPath (Join-Path $out 'updater-setup.md') -Destination (Join-Path $toolsDirectory 'updater-setup.md') -Force
Copy-Item -LiteralPath (Join-Path $out 'updater-THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $toolsDirectory 'updater-THIRD-PARTY-NOTICES.txt') -Force
$toolsZip=Join-Path $package "AnimalTown-tools-$version.zip"
$cliffsZip=Join-Path $package "AnimalTown-cliff-kit-$version.zip"
$buildingsZip=Join-Path $package "AnimalTown-building-kit-$version.zip"
# Rebuilding a draft can replace these generated archives. Disc images are uploaded directly.
foreach($archive in @($serverZip,$toolsZip,$cliffsZip,$buildingsZip)){if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}}
[IO.Compression.ZipFile]::CreateFromDirectory($server,$serverZip,[IO.Compression.CompressionLevel]::Optimal,$false)
[IO.Compression.ZipFile]::CreateFromDirectory($toolsDirectory,$toolsZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$cliffArchive=[IO.Compression.ZipFile]::Open($cliffsZip,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($folder in @('assets/models/terrain/cliffs','assets/textures/environment/cliffs')){
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $Root $folder) -File -Recurse){
            $entry=[IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($cliffArchive,$file.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
        }
    }
}finally{$cliffArchive.Dispose()}
$buildingArchive=[IO.Compression.ZipFile]::Open($buildingsZip,[IO.Compression.ZipArchiveMode]::Create)
try{
    foreach($folder in @('assets/models/buildings','assets/models/interiors','assets/textures/buildings','assets/definitions')){
        foreach($file in Get-ChildItem -LiteralPath (Join-Path $Root $folder) -File -Recurse){
            $entry=[IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\','/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildingArchive,$file.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null
        }
    }
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildingArchive,(Join-Path $Root 'docs/building-interiors.md'),'building-interiors.md',[IO.Compression.CompressionLevel]::Optimal)|Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($buildingArchive,(Join-Path $Root 'docs/town-shop.md'),'town-shop.md',[IO.Compression.CompressionLevel]::Optimal)|Out-Null
}finally{$buildingArchive.Dispose()}
Copy-Item -LiteralPath (Join-Path $out 'build-revisions.txt') -Destination (Join-Path $package 'BUILD.txt') -Force
[IO.File]::AppendAllText((Join-Path $package 'BUILD.txt'),"Opening banner SHA256: $($bannerHashes.opening)`nPAL banner SHA256: $($bannerHashes.pal)`n")
$openingBnr=Join-Path $package 'opening.bnr'
$palBnr=Join-Path $package 'opening-pal.bnr'
Copy-Item -LiteralPath $openingSource -Destination $openingBnr -Force
Copy-Item -LiteralPath $palSource -Destination $palBnr -Force
Copy-Item -LiteralPath (Join-Path $out 'texture-audit.json') -Destination (Join-Path $package 'texture-audit.json') -Force
Copy-Item -LiteralPath (Join-Path $out 'validation-plan.json') -Destination (Join-Path $package 'validation-plan.json') -Force
Copy-Item -LiteralPath (Join-Path $out 'version.json') -Destination (Join-Path $package 'version.json') -Force
Copy-Item -LiteralPath $nkitValidation -Destination (Join-Path $package 'nkit-validation.json') -Force
Copy-Item -LiteralPath (Join-Path $out 'disc/animal-town-updater.dol') -Destination (Join-Path $package 'animal-town-updater.dol') -Force
Copy-Item -LiteralPath (Join-Path $out 'updater-THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $package 'updater-THIRD-PARTY-NOTICES.txt') -Force
$updaterDol=Join-Path $package 'animal-town-updater.dol'
$updaterNotice=Join-Path $package 'updater-THIRD-PARTY-NOTICES.txt'
. (Join-Path $PSScriptRoot 'update-feed.ps1')
$updateFeed=Join-Path $package 'update.json'
Write-UpdateFeed -Iso $nkit -Version $version -Repository $Repository -Destination $updateFeed
$payloadAssets=@($nkit,$updateFeed,$updaterDol,$updaterNotice,$openingBnr,$palBnr,$serverZip,$toolsZip,$cliffsZip,$buildingsZip,(Join-Path $package 'BUILD.txt'),(Join-Path $package 'texture-audit.json'),(Join-Path $package 'validation-plan.json'),(Join-Path $package 'version.json'),(Join-Path $package 'nkit-validation.json'))+$titleEvidenceAssets
$checksums=foreach($file in $payloadAssets){"$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"}
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS.txt'),($checksums -join "`n")+"`n")
$evidenceNotes=if($titleEvidenceAssets.Count){'The title-logo PNGs show the title, villager picker, name entry and return-to-title states captured from this exact release image. Debug and status-message captures are included when produced. title-logo-report.json records the capture checks; title-logo-build.json binds the evidence to the source commit, version and NKit SHA-256.'}else{''}
$notes=@"
AnimalTown $version development build from commit $commit.

**opening.bnr** is the Smalltown Forest BNR1 banner embedded in this release's disc. **opening-pal.bnr** is the matching BNR2 alternative for PAL packaging, with English text in all six language slots. Both contain the same 96x32 artwork and current version/commit identity. The game and banner display Smalltown Forest; the legacy AnimalTown boot-header identity and package filenames remain compatible with the updater. These standalone banners are provided for distribution packaging; GC Loader users only need the NKit ISO.

$evidenceNotes

AnimalTown-building-kit-$version.zip contains the original cottage and town shop kits, hinged doors, cutaway interior parts, palettes, definitions and authoring guides. The shop has a central daily-stock island, 3/5/7 wall sale stands by shop tier, an unlimited essentials cabinet and a service counter. NPCs can buy a display item and leave; player-sale proceeds go to the seller's savings. A at doormats enters/exits; C-stick turns the indoor camera, L recentres. Console online guest transport remains unavailable; atomic guest purchases are supported by the backend. Physical GameCube testing remains outstanding.

AnimalTown-cliff-kit-$version.zip contains eight low-poly cliff GLBs, shared 128/256px soil textures and the ground-material binding guide. The game reuses its existing floor texture and tint for cliff tops/lips. L+R+D-pad Up toggles a rendering-only eight-piece inspection gallery.

**AnimalTown-tools-$version.zip** contains the offline texture/UV repacker and its guide. Requires .NET 8; run `dotnet AnimalTown.Tools.dll repack-textures . INPUT.glb OUTPUT.glb --size256` (or --size128/512/1024/Original). Exact flat colours share UV space; appearance checks reject excessive loss. Original game assets are not automatically repacked.

For GC Loader SD-card storage, download **AnimalTown-$version.nkit.iso** and copy it directly onto the card. No extraction is needed. This build occupies $($nkitInfo.compactBytes) bytes instead of 1,459,978,240 bytes ($([Math]::Round($nkitInfo.savedPercent,2))% smaller). NKit compacts padding; the playable image preserves every game file, title, commit identity, disc banner and save banner/icon. The pipeline restores it to the original full ISO and verifies an exact SHA-256 match, plus the compact image's physical apploader, DOL and file payloads. Physical direct GC Loader testing remains outstanding.

The NKit ISO is the only disc image built for delivery and is published directly, without a ZIP wrapper. Full-size conversion and round-trip images are temporary and deleted after verification. The server is a separate **AnimalTown-server-$version.zip** archive and requires .NET 10 and PostgreSQL. It does not run on the console.

**animal-town-updater.dol** is also embedded in the ISO for the title Update option. Keep a supported official normal Swiss DOL at the GC Loader drive root under any .dol filename; the updater identifies its contents and chooses the newest recognized revision. HTTPS uses embedded public CA trust and fresh on-console entropy; no PC provisioning, roots.pem, entropy.bin or UTC-offset file is required. Set the GameCube clock accurately; certificate validity must cover its full possible UTC range of local time plus or minus 14 hours. Health checks do not prove randomness; physical qualification of the on-console entropy source remains outstanding for this prerelease. updater-THIRD-PARTY-NOTICES.txt accompanies the updater in the ISO and tools archive and as a separate checksummed release asset. The optional provision-updater.py tool is for older images only. The updater scans GC Loader for the highest stable AnimalTown major.minor.patch ISO, ignoring hash-suffixed development builds. It downloads the new image into that directory using GitHub's filename, verifies it, switches GC Loader's hardware mapping and verifies the physical boot-header readback before deleting the selected old ISO. It then hands off to Swiss to launch the new image. No image-path.txt mapping is required. Physical storage, BBA and Swiss relaunch behavior remain unverified.

version.json records the main major.minor.patch version, merged PR boundaries and source commit (branch builds append +hash). BUILD.txt records the version and game, engine and SDK revisions plus both banner SHA-256 hashes. validation-plan.json records the comparison base and selected checks. texture-audit.json records texture source identities, fingerprints and 256-pixel default residency and 128/256/512/1024 model variants. nkit-validation.json records the pinned converter, sizes, per-file hashes and round-trip verification. SHA256SUMS.txt covers the NKit image, both banners, the archives and manifests.

This is an early playable build, not the completed game. Physical GameCube testing is outstanding. See [implementation status](https://github.com/$Repository/blob/$commit/docs/implementation-status.md) and [build instructions](https://github.com/$Repository/blob/$commit/README.md).
"@
$notesPath=Join-Path $package 'release-notes.md'
[IO.File]::WriteAllText($notesPath,$notes)
if($PackageOnly){Write-Host "Release package: $package";Write-Host "Disc image: $nkit";return}
function InvokeGh([string[]]$Arguments){
    $result=& $Gh @Arguments
    if($LASTEXITCODE -ne 0){throw "GitHub CLI failed: $($Arguments[0]) $($Arguments[1])"}
    return $result
}
$assets=$payloadAssets+@((Join-Path $package 'SHA256SUMS.txt'))
$existing=& $Gh release view $tag --repo $Repository --json isDraft,targetCommitish,assets 2>$null
if($LASTEXITCODE -eq 0){
    $release=$existing | ConvertFrom-Json
    if($release.isDraft){
        if($release.targetCommitish -ne $commit){throw 'Draft targets another commit'}
    }else{
        $tagCommit=InvokeGh @('api',"repos/$Repository/commits/$tag",'--jq','.sha')
        if($tagCommit -ne $commit){throw 'Release tag points to another commit'}
        $publishedSums=InvokeGh @('release','download',$tag,'--repo',$Repository,'--pattern','SHA256SUMS.txt','--output','-')
        foreach($asset in $assets){
            $name=[IO.Path]::GetFileName($asset)
            $uploaded=$release.assets | Where-Object name -EQ $name
            if(!$uploaded -or $uploaded.size -le 0){throw "Published release is missing $name"}
            if($name -ne 'SHA256SUMS.txt'){
                $entry=[regex]::Match(($publishedSums -join "`n"),'(?m)^([0-9a-f]{64})  '+[regex]::Escape($name)+'\r?$')
                if(!$entry.Success -or $uploaded.digest -ne 'sha256:'+$entry.Groups[1].Value){throw "Published checksum mismatch: $name"}
            }
        }
        Write-Host "Release already published: https://github.com/$Repository/releases/tag/$tag"
        return
    }
}else{
    $createArguments=@('release','create',$tag,'--repo',$Repository,'--target',$commit,'--title',"AnimalTown $version",'--notes-file',$notesPath,'--draft')
    if(!$identity.isMain){$createArguments+='--prerelease'}
    InvokeGh $createArguments | Out-Null
}
InvokeGh (@('release','upload',$tag,'--repo',$Repository,'--clobber')+$assets) | Out-Null
$remote=(InvokeGh @('release','view',$tag,'--repo',$Repository,'--json','assets') | ConvertFrom-Json).assets
foreach($asset in $assets){
    $file=Get-Item -LiteralPath $asset
    $uploaded=$remote | Where-Object name -EQ $file.Name
    $expected='sha256:'+(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if(!$uploaded -or $uploaded.size -ne $file.Length -or $uploaded.digest -ne $expected){throw "Uploaded asset verification failed: $($file.Name)"}
}
$preview=(!$identity.isMain).ToString().ToLowerInvariant()
InvokeGh @('release','edit',$tag,'--repo',$Repository,'--draft=false',"--prerelease=$preview") | Out-Null
Write-Host "Published: https://github.com/$Repository/releases/tag/$tag"
