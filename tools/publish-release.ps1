param(
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [string]$Repository='capz/AnimalTown',
    [string]$Gh='gh',
    [switch]$PackageOnly
)
$ErrorActionPreference='Stop'
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
$package=Join-Path $out "releases/$tag"
New-Item -ItemType Directory -Force -Path $package | Out-Null
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
}finally{$buildingArchive.Dispose()}
Copy-Item -LiteralPath (Join-Path $out 'build-revisions.txt') -Destination (Join-Path $package 'BUILD.txt') -Force
Copy-Item -LiteralPath (Join-Path $out 'texture-audit.json') -Destination (Join-Path $package 'texture-audit.json') -Force
Copy-Item -LiteralPath (Join-Path $out 'validation-plan.json') -Destination (Join-Path $package 'validation-plan.json') -Force
Copy-Item -LiteralPath (Join-Path $out 'version.json') -Destination (Join-Path $package 'version.json') -Force
Copy-Item -LiteralPath $nkitValidation -Destination (Join-Path $package 'nkit-validation.json') -Force
Copy-Item -LiteralPath (Join-Path $out 'disc/animal-town-updater.dol') -Destination (Join-Path $package 'animal-town-updater.dol') -Force
Copy-Item -LiteralPath (Join-Path $out 'updater-THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $package 'updater-THIRD-PARTY-NOTICES.txt') -Force
$updaterDol=Join-Path $package 'animal-town-updater.dol'
$updaterNotice=Join-Path $package 'updater-THIRD-PARTY-NOTICES.txt'
$checksums=foreach($file in @($nkit,$updaterDol,$updaterNotice,$serverZip,$toolsZip,$cliffsZip,$buildingsZip,(Join-Path $package 'BUILD.txt'),(Join-Path $package 'texture-audit.json'),(Join-Path $package 'validation-plan.json'),(Join-Path $package 'version.json'),(Join-Path $package 'nkit-validation.json'))){"$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"}
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS.txt'),($checksums -join "`n")+"`n")
$notes=@"
AnimalTown $version development build from commit $commit.

AnimalTown-building-kit-$version.zip contains the original cottage exterior, hinged door, cutaway interior parts, atlas, definitions and authoring guide. A at doormats enters/exits; C-stick turns the indoor camera, L recentres. NPCs visit homes and resume their journeys even while the player is indoors. Physical GameCube testing remains outstanding.

AnimalTown-cliff-kit-$version.zip contains eight low-poly cliff GLBs, shared 128/256px soil textures and the ground-material binding guide. The game reuses its existing floor texture and tint for cliff tops/lips. L+R+D-pad Up toggles a rendering-only eight-piece inspection gallery.

**AnimalTown-tools-$version.zip** contains the offline texture/UV repacker and its guide. Requires .NET 8; run `dotnet AnimalTown.Tools.dll repack-textures . INPUT.glb OUTPUT.glb --size256` (or --size128/512/1024/Original). Exact flat colours share UV space; appearance checks reject excessive loss. Original game assets are not automatically repacked.

For GC Loader SD-card storage, download **AnimalTown-$version.nkit.iso** and copy it directly onto the card. No extraction is needed. This build occupies $($nkitInfo.compactBytes) bytes instead of 1,459,978,240 bytes ($([Math]::Round($nkitInfo.savedPercent,2))% smaller). NKit compacts padding; the playable image preserves every game file, title, commit identity, disc banner and save banner/icon. The pipeline restores it to the original full ISO and verifies an exact SHA-256 match, plus the compact image's physical apploader, DOL and file payloads. Physical direct GC Loader testing remains outstanding.

The NKit ISO is the only disc image built for delivery and is published directly, without a ZIP wrapper. Full-size conversion and round-trip images are temporary and deleted after verification. The server is a separate **AnimalTown-server-$version.zip** archive and requires .NET 10 and PostgreSQL. It does not run on the console.

**animal-town-updater.dol** is also embedded in the ISO for the title Update option. Keep a supported official normal Swiss DOL at the GC Loader drive root under any .dol filename; the updater identifies its contents and chooses the newest recognized revision. HTTPS uses embedded public CA trust and fresh on-console entropy; no PC provisioning, roots.pem, entropy.bin or UTC-offset file is required. Set the GameCube clock accurately; certificate validity must cover its full possible UTC range of local time plus or minus 14 hours. Health checks do not prove randomness; physical qualification of the on-console entropy source remains outstanding for this prerelease. updater-THIRD-PARTY-NOTICES.txt accompanies the updater in the ISO and tools archive and as a separate checksummed release asset. The optional provision-updater.py tool is for older images only. The updater scans GC Loader for the highest stable AnimalTown major.minor.patch ISO, ignoring hash-suffixed development builds. It downloads the new image into that directory using GitHub's filename, verifies it, switches GC Loader's hardware mapping and verifies the physical boot-header readback before deleting the selected old ISO. It then hands off to Swiss to launch the new image. No image-path.txt mapping is required. Physical storage, BBA and Swiss relaunch behavior remain unverified.

version.json records the main major.minor.patch version, merged PR boundaries and source commit (branch builds append +hash). BUILD.txt records the version and game, engine and SDK revisions. validation-plan.json records the comparison base and selected checks. texture-audit.json records texture source identities, fingerprints and 256-pixel default residency and 128/256/512/1024 model variants. nkit-validation.json records the pinned converter, sizes, per-file hashes and round-trip verification. SHA256SUMS.txt covers the NKit image, the archives and manifests.

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
$assets=@($nkit,$updaterDol,$updaterNotice,$serverZip,$toolsZip,$cliffsZip,$buildingsZip,(Join-Path $package 'BUILD.txt'),(Join-Path $package 'texture-audit.json'),(Join-Path $package 'validation-plan.json'),(Join-Path $package 'version.json'),(Join-Path $package 'nkit-validation.json'),(Join-Path $package 'SHA256SUMS.txt'))
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
