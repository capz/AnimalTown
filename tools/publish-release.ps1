param(
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [string]$Repository='capz/AnimalTown',
    [string]$Gh='gh',
    [switch]$PackageOnly
)
$ErrorActionPreference='Stop'
function Run([string]$Command,[string[]]$Arguments){
    & $Command @Arguments
    if($LASTEXITCODE -ne 0){throw "$Command failed"}
}
function Assert-BinaryArchive([string]$Path){
    $archive=[IO.Compression.ZipFile]::OpenRead($Path)
    try{
        foreach($entry in $archive.Entries){
            if($entry.FullName -match '(?i)\.(cs|csproj|sln|py|ps1|sh|c|cpp|h|glb|gltf|blend|fbx|zip|tar|gz)$'){
                throw "Source/authoring file in release archive: $($entry.FullName)"
            }
        }
    }finally{$archive.Dispose()}
}
function Assert-ReleaseVersionMetadata($Identity,[string]$Path){
    $saved=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach($property in $Identity.PSObject.Properties){
        if($property.Name -ne 'changes' -and $saved.($property.Name) -cne $property.Value){throw "Built version metadata differs: $($property.Name)"}
    }
    if(@($saved.changes).Count -ne @($Identity.changes).Count){throw 'Built version merge history differs'}
    for($i=0;$i -lt @($Identity.changes).Count;$i++){
        if($saved.changes[$i].number -ne $Identity.changes[$i].number -or $saved.changes[$i].commit -cne $Identity.changes[$i].commit){throw 'Built version integration boundary differs'}
    }
    # Preserve the exact bytes already embedded in the ISO and app ZIP.
}
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
Assert-ReleaseVersionMetadata $identity (Join-Path $Root 'out/version.json')
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
$depot=Join-Path $out "AnimalTown-$version-depot.zip"
& dotnet run --project (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-build -- verify-depot $Root $depot
if($LASTEXITCODE -ne 0){throw 'Standalone app package validation failed'}
$package=Join-Path $out "releases/$tag"
New-Item -ItemType Directory -Force -Path $package | Out-Null
$reports=Join-Path $package 'release-files'
if(Test-Path -LiteralPath $reports){Remove-Item -LiteralPath $reports -Recurse -Force}
New-Item -ItemType Directory -Path $reports | Out-Null
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
        $destination=Join-Path $reports $name
        Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
        $titleEvidenceAssets+=$destination
    }
}
$terrainEvidenceAssets=@()
$terrainEvidenceRoot=Join-Path $out 'terrain-materials'
$terrainEvidenceBuild=Join-Path $terrainEvidenceRoot 'terrain-materials-build.json'
if(Test-Path -LiteralPath $terrainEvidenceBuild -PathType Leaf){
    $evidence=Get-Content -LiteralPath $terrainEvidenceBuild -Raw|ConvertFrom-Json
    if($evidence.schema -ne 1 -or $evidence.commit -cne $commit -or $evidence.version -cne $version -or
       $evidence.tag -cne $tag -or $evidence.isoName -cne [IO.Path]::GetFileName($nkit) -or
       $evidence.isoSha256 -cne $nkitInfo.compactSha256){
        throw 'Terrain-material evidence must match the release commit, version and verified NKit image'
    }
    $report=Get-Content -LiteralPath (Join-Path $terrainEvidenceRoot 'terrain-materials-report.json') -Raw|ConvertFrom-Json
    if($report.scenario -cne 'terrain-materials' -or $report.passed -ne $true){throw 'Terrain-material capture must pass before publication'}
    $screenshots=@($report.screenshots)
    foreach($name in @('terrain-materials-normal.png','terrain-materials-mips-auto.png','terrain-materials-menu-on.png')){
        if($name -cnotin $screenshots){throw "Missing required terrain-material capture: $name"}
    }
    if(@($screenshots|Select-Object -Unique).Count -ne $screenshots.Count){throw 'Duplicate terrain-material capture names'}
    foreach($name in $screenshots){if($name -cnotmatch '^terrain-materials-[a-z0-9-]+\.png$'){throw 'Invalid terrain-material capture filename'}}
    foreach($name in $screenshots+@('terrain-materials-report.json','terrain-materials-build.json')){
        $source=Get-Item -LiteralPath (Join-Path $terrainEvidenceRoot $name)
        if($source.PSIsContainer -or $source.Length -le 0){throw "Missing or empty terrain-material evidence: $name"}
        $destination=Join-Path $reports $name
        Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
        $terrainEvidenceAssets+=$destination
    }
}
$serverZip=Join-Path $package "AnimalTown-server-$version.zip"
$toolsDirectory=Join-Path $out 'tools-published-binaries'
if(Test-Path -LiteralPath $toolsDirectory){Remove-Item -LiteralPath $toolsDirectory -Recurse -Force}
& dotnet publish (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-self-contained -o $toolsDirectory -v quiet
if($LASTEXITCODE -ne 0){throw 'Offline tool publication failed'}
Copy-Item -LiteralPath (Join-Path $Root 'docs/texture-repacker.md') -Destination (Join-Path $toolsDirectory 'texture-repacker.md') -Force
Copy-Item -LiteralPath (Join-Path $Root 'docs/uv-editor-design.md') -Destination (Join-Path $toolsDirectory 'uv-editor-design.md') -Force
$toolsZip=Join-Path $package "AnimalTown-tools-$version.zip"
# Rebuilding a draft can replace these generated archives. Disc images are uploaded directly.
foreach($archive in @($serverZip,$toolsZip)){if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}}
[IO.Compression.ZipFile]::CreateFromDirectory($server,$serverZip,[IO.Compression.CompressionLevel]::Optimal,$false)
[IO.Compression.ZipFile]::CreateFromDirectory($toolsDirectory,$toolsZip,[IO.Compression.CompressionLevel]::Optimal,$false)
Copy-Item -LiteralPath (Join-Path $out 'build-revisions.txt') -Destination (Join-Path $reports 'BUILD.txt') -Force
[IO.File]::AppendAllText((Join-Path $reports 'BUILD.txt'),"Opening banner SHA256: $($bannerHashes.opening)`nPAL banner SHA256: $($bannerHashes.pal)`n")
$openingBnr=Join-Path $reports 'opening.bnr'
$palBnr=Join-Path $reports 'opening-pal.bnr'
Copy-Item -LiteralPath $openingSource -Destination $openingBnr -Force
Copy-Item -LiteralPath $palSource -Destination $palBnr -Force
foreach($name in @('texture-audit.json','validation-plan.json','version.json','nkit-validation.json','updater-setup.md','updater-THIRD-PARTY-NOTICES.txt')){
    Copy-Item -LiteralPath (Join-Path $out $name) -Destination (Join-Path $reports $name) -Force
}
# The updater executable belongs to the ISO only; Depot has no Update action.
. (Join-Path $PSScriptRoot 'update-feed.ps1')
$updateFeed=Join-Path $reports 'update.json'
Write-UpdateFeed -Iso $nkit -Version $version -Repository $Repository -Destination $updateFeed
# Keep GitHub's full release response within the updater's 32 KiB budget.
# Detailed reports and optional emulator captures live in the Depot ZIP.
$notes=@"
AnimalTown $version from $commit.

Downloads: NKit ISO for GC Loader; Depot ZIP for the standalone SD app; separate server and offline tools ZIPs. Reports, checksums, banners and available captures are inside the Depot ZIP's release/ directory.

The ISO updater now reads bounded GitHub release metadata. Older installed updater builds need one manual ISO upgrade because loose update.json assets are no longer published. Saves remain compatible.

Source and authoring kits are excluded from release archives. Clone the repository for source. Physical GameCube qualification remains outstanding.
"@
if([Text.Encoding]::UTF8.GetByteCount($notes) -gt 1024){throw 'Release notes exceed updater metadata budget'}
$notesPath=Join-Path $reports 'release-notes.md'
[IO.File]::WriteAllText($notesPath,$notes)
# The containing Depot ZIP cannot hash itself. GitHub's asset digest covers it.
$checksumFiles=@($nkit,$serverZip,$toolsZip)+@(Get-ChildItem -LiteralPath $reports -File | ForEach-Object FullName)
$checksums=foreach($file in $checksumFiles){"$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"}
[IO.File]::WriteAllText((Join-Path $reports 'SHA256SUMS.txt'),($checksums -join "`n")+"`n")
$bundle=Join-Path $PSScriptRoot 'release-bundle.py'
Run python3 @($bundle,'attach',$depot,$reports)
Run dotnet @('run','--project',(Join-Path $Root 'tools/AnimalTown.Tools'),'-c','Release','--no-build','--','verify-depot',$Root,$depot)
$assets=@($nkit,$depot,$serverZip,$toolsZip)
foreach($asset in $assets){
    if([IO.Path]::GetExtension($asset) -cnotin @('.zip','.iso')){throw "Forbidden release asset: $asset"}
    if($asset.EndsWith('.zip')){Assert-BinaryArchive $asset}
}
if($PackageOnly){Write-Host "Release package: $package";Write-Host "Disc image: $nkit";return}
function InvokeGh([string[]]$Arguments){
    $result=& $Gh @Arguments
    if($LASTEXITCODE -ne 0){throw "GitHub CLI failed: $($Arguments[0]) $($Arguments[1])"}
    return $result
}
function Read-ReleaseMetadata([string]$Tag){
    # A draft may not have a Git tag yet. Resolve its REST URL through gh's
    # draft-aware lookup; /releases/tags/TAG is only reliable after publication.
    $apiUrl=(InvokeGh @('release','view',$Tag,'--repo',$Repository,'--json','apiUrl','--jq','.apiUrl')) -join ''
    if($apiUrl -cnotmatch ('^https://api\.github\.com/repos/'+[regex]::Escape($Repository)+'/releases/[1-9][0-9]*$')){throw 'Invalid release API URL'}
    $metadata=(InvokeGh @('api',$apiUrl)) -join "`n"
    if([Text.Encoding]::UTF8.GetByteCount($metadata) -gt 32768){throw 'Release metadata exceeds updater 32 KiB budget'}
    return $metadata
}
$existing=& $Gh release view $tag --repo $Repository --json isDraft,targetCommitish,assets 2>$null
if($LASTEXITCODE -eq 0){
    $release=$existing | ConvertFrom-Json
    if($release.isDraft){
        if($release.targetCommitish -ne $commit){throw 'Draft targets another commit'}
        $expectedNames=@($assets | ForEach-Object {[IO.Path]::GetFileName($_)})
        foreach($asset in $release.assets){
            if($asset.name -cnotin $expectedNames){InvokeGh @('release','delete-asset',$tag,$asset.name,'--repo',$Repository,'--yes') | Out-Null}
        }
        InvokeGh @('release','edit',$tag,'--repo',$Repository,'--notes-file',$notesPath) | Out-Null
    }else{
        $tagCommit=InvokeGh @('api',"repos/$Repository/commits/$tag",'--jq','.sha')
        if($tagCommit -ne $commit){throw 'Release tag points to another commit'}
        $names=@($assets | ForEach-Object {[IO.Path]::GetFileName($_)})
        if($release.assets.Count -ne $assets.Count -or @($release.assets | Where-Object {$_.name -cnotin $names}).Count){throw 'Published release asset set differs from the ZIP/ISO policy'}
        # Optional evidence may have extended the published Depot ZIP since the
        # primary build. Verify it and all original reports without discarding it.
        $remoteDepot=Join-Path $package 'published-depot.zip'
        InvokeGh @('release','download',$tag,'--repo',$Repository,'--pattern',([IO.Path]::GetFileName($depot)),'--output',$remoteDepot,'--clobber') | Out-Null
        Run dotnet @('run','--project',(Join-Path $Root 'tools/AnimalTown.Tools'),'-c','Release','--no-build','--','verify-depot',$Root,$remoteDepot)
        $remoteReports=Join-Path $package 'published-reports'
        Run python3 @($bundle,'extract',$remoteDepot,$remoteReports)
        $publishedSums=Get-Content -LiteralPath (Join-Path $remoteReports 'SHA256SUMS.txt') -Raw
        foreach($asset in $assets){
            $name=[IO.Path]::GetFileName($asset)
            # Fresh ZIP timestamps/captures may differ on a rerun. Check the
            # original verified release, rather than replacing its evidence.
            if($asset -eq $depot){$digest=(Get-FileHash -LiteralPath $remoteDepot).Hash.ToLowerInvariant()}
            else{
                $entry=[regex]::Match($publishedSums,'(?m)^([0-9a-f]{64})  '+[regex]::Escape($name)+'\r?$')
                if(!$entry.Success){throw "Published checksum is missing: $name"}
                $digest=$entry.Groups[1].Value
            }
            $uploaded=@($release.assets | Where-Object name -CEQ $name)
            if($uploaded.Count -ne 1 -or $uploaded[0].size -le 0 -or $uploaded[0].digest -cne 'sha256:'+$digest){throw "Published checksum mismatch: $name"}
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
$metadata=Read-ReleaseMetadata $tag
$remote=($metadata | ConvertFrom-Json).assets
if($remote.Count -ne $assets.Count){throw 'Release must contain exactly the four binary ZIP/ISO assets'}
foreach($asset in $assets){
    $file=Get-Item -LiteralPath $asset
    $uploaded=@($remote | Where-Object name -CEQ $file.Name)
    $expected='sha256:'+(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if($uploaded.Count -ne 1 -or $uploaded[0].size -ne $file.Length -or $uploaded[0].digest -cne $expected){throw "Uploaded asset verification failed: $($file.Name)"}
}
$preview=(!$identity.isMain).ToString().ToLowerInvariant()
InvokeGh @('release','edit',$tag,'--repo',$Repository,'--draft=false',"--prerelease=$preview") | Out-Null
Write-Host "Published: https://github.com/$Repository/releases/tag/$tag"
