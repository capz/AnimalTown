param(
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [string]$Repository='capz/AnimalTown',
    [string]$Gh='gh',
    [switch]$PackageOnly,
    [switch]$PublishOnly
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
function Assert-ReleaseAsset([string]$Path){
    if([IO.Path]::GetFileName($Path) -ceq 'update.json'){return}
    if([IO.Path]::GetExtension($Path) -cnotin @('.zip','.iso')){throw "Forbidden release asset: $Path"}
    if($Path.EndsWith('.zip')){Assert-BinaryArchive $Path}
}
function Assert-ReleaseAssetSet($Release,[string[]]$Names,[switch]$AllowMissingUpdateFeed){
    $actual=@($Release.assets | ForEach-Object name)
    if(@($actual | Select-Object -Unique).Count -ne $actual.Count -or @($actual | Where-Object {$_ -cnotin $Names}).Count){
        throw 'Published release contains unexpected or duplicate assets'
    }
    foreach($name in $Names){
        if($name -cnotin $actual -and !($AllowMissingUpdateFeed -and $name -ceq 'update.json')){throw "Published release is missing $name"}
    }
}
function Assert-ReleaseVersionMetadata($Identity,[string]$Path){
    $saved=Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    foreach($property in $Identity.PSObject.Properties){
        if($property.Name -notin @('changes','requestSummaries') -and $saved.($property.Name) -cne $property.Value){throw "Built version metadata differs: $($property.Name)"}
    }
    if(@($saved.changes).Count -ne @($Identity.changes).Count){throw 'Built version merge history differs'}
    for($i=0;$i -lt @($Identity.changes).Count;$i++){
        if($saved.changes[$i].number -ne $Identity.changes[$i].number -or $saved.changes[$i].commit -cne $Identity.changes[$i].commit){throw 'Built version integration boundary differs'}
    }
    if($null -ne $Identity.requestSummaries){
        if($saved.requestSummaries -isnot [array] -or $saved.requestSummaries.Count -ne $Identity.requestSummaries.Count){
            throw 'Built version request summaries differ'
        }
        for($i=0;$i -lt $Identity.requestSummaries.Count;$i++){
            $source=$Identity.requestSummaries[$i];$built=$saved.requestSummaries[$i]
            if($built.id -cne $source.id -or $built.lines -isnot [array] -or $built.lines.Count -ne 4){throw 'Built version request summary differs'}
            for($line=0;$line -lt 4;$line++){
                if($built.lines[$line] -cne $source.lines[$line]){throw 'Built version request summary differs'}
            }
        }
    }
    # Preserve the exact bytes already embedded in the ISO and app ZIP.
}
function Get-ReleaseNotes($Identity){
    $summarySection=if($Identity.summary){"## What's new`n`n$($Identity.summary)`n`n"}else{''}
    $notes=@"
AnimalTown $($Identity.version) from $($Identity.commit).

${summarySection}Downloads: NKit ISO for GC Loader; Depot ZIP for the standalone SD app; separate server and offline tools ZIPs. Reports, checksums, banners and available captures are inside the Depot ZIP's release/ directory.

update.json provides the compact release feed for older installed updaters. The current ISO updater also reads bounded GitHub release metadata. Saves remain compatible.

Source and authoring kits are excluded from release archives. Clone the repository for source. Physical GameCube qualification remains outstanding.
"@
    if([Text.Encoding]::UTF8.GetByteCount($notes) -gt 1024){throw 'Release notes exceed updater metadata budget'}
    return $notes
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
function Copy-DaylightEvidence([string]$Source,[string]$Destination,[string]$Commit,[string]$Version,[string]$Tag,[string]$IsoName,[string]$IsoSha256){
    $build=Join-Path $Source 'daylight-build.json'
    if(!(Test-Path -LiteralPath $build -PathType Leaf)){return}
    $evidence=Get-Content -LiteralPath $build -Raw|ConvertFrom-Json
    if($evidence.schema -ne 1 -or $evidence.commit -cne $Commit -or $evidence.version -cne $Version -or
       $evidence.tag -cne $Tag -or $evidence.isoName -cne $IsoName -or $evidence.isoSha256 -cne $IsoSha256){
        throw 'Daylight evidence must match the release commit, version and verified NKit image'
    }
    $phases=[ordered]@{noon='12:00';dawn='06:30';dusk='18:00';night='20:00'}
    foreach($phase in $phases.Keys){
        $folder=Join-Path $Source $phase
        $report=Get-Content -LiteralPath (Join-Path $folder 'daylight-report.json') -Raw|ConvertFrom-Json
        if($report.passed -ne $true -or $report.requested_clock -cne $phases[$phase]){throw "Daylight capture did not pass: $phase"}
        foreach($suffix in @('a.png','b.png','report.json')){
            $file=Get-Item -LiteralPath (Join-Path $folder "daylight-$suffix")
            if($file.PSIsContainer -or $file.Length -le 0){throw "Missing or empty daylight evidence: $phase/$suffix"}
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination "daylight-$phase-$suffix") -Force
        }
    }
    Copy-Item -LiteralPath $build -Destination (Join-Path $Destination 'daylight-build.json') -Force
}
function Copy-VideoEvidence([string]$Source,[string]$Destination,[string]$Commit,[string]$Version,[string]$Tag,[string]$IsoName,[string]$IsoSha256){
    $build=Join-Path $Source 'video-options-build.json'
    if(!(Test-Path -LiteralPath $build -PathType Leaf)){return}
    $evidence=Get-Content -LiteralPath $build -Raw|ConvertFrom-Json
    if($evidence.schema -ne 1 -or $evidence.commit -cne $Commit -or $evidence.version -cne $Version -or
       $evidence.tag -cne $Tag -or $evidence.isoName -cne $IsoName -or $evidence.isoSha256 -cne $IsoSha256){
        throw 'Video-options evidence must match the release commit, version and verified NKit image'
    }
    $aa=Get-Content -LiteralPath (Join-Path $Source 'smoothing/aa-option.json') -Raw|ConvertFrom-Json
    if($aa.passed -ne $true -or $aa.restoredAfterRestart -ne $true -or $aa.townUnchanged -ne $true){throw 'Video-options smoothing checks must pass before publication'}
    Copy-Item -LiteralPath $build -Destination $Destination
    foreach($name in @('smoothing/aa-option.json','smoothing/aa-initial-home.png','smoothing/aa-menu-on.png',
                      'smoothing/aa-menu-off.png','smoothing/aa-menu-restored.png','smoothing/aa-576p-rejected.png',
                      'options/title-options.json','options/title-options-load.json','options/options-restored.png',
                      'options/options-delete-default.png','unsupported/title-options-unsupported.json','unsupported/options-unsupported.png')){
        $file=Get-Item -LiteralPath (Join-Path $Source $name)
        if($file.PSIsContainer -or $file.Length -le 0){throw "Missing or empty video-options evidence: $name"}
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination ('video-options-'+$name.Replace('/','-')))
    }
}
function Copy-VillagerVisualEvidence([string]$Source,[string]$Destination,[string]$Commit,[string]$Version,[string]$Tag,[string]$IsoName,[string]$IsoSha256){
    $build=Join-Path $Source 'villager-visuals-build.json'
    if(!(Test-Path -LiteralPath $build -PathType Leaf)){return}
    $evidence=Get-Content -LiteralPath $build -Raw|ConvertFrom-Json
    if($evidence.schema -ne 1 -or $evidence.commit -cne $Commit -or $evidence.version -cne $Version -or
       $evidence.tag -cne $Tag -or $evidence.isoName -cne $IsoName -or $evidence.isoSha256 -cne $IsoSha256){throw 'Villager visual captures must match the release source, version and verified NKit image'}
    $report=Get-Content -LiteralPath (Join-Path $Source 'villager-visuals-report.json') -Raw|ConvertFrom-Json
    if($report.scenario -cne 'villager-visuals' -or $report.passed -ne $true -or $report.committed_records_unchanged -ne $true -or @($report.swaps).Count -ne 7){throw 'Villager visual swaps must pass with unchanged committed records'}
    $actual=@($report.swaps|ForEach-Object{"$($_.actor):$($_.appearance)"}|Sort-Object)
    $expected=@('0:6','0:7','0:8','0:9','0:10','0:11','1:6')|Sort-Object
    if(($actual -join ',') -cne ($expected -join ',')){throw 'Villager visual evidence must cover all six new appearances and one NPC swap'}
    $target=Join-Path $Destination 'scenarios/villager-visuals';New-Item -ItemType Directory -Force -Path $target|Out-Null
    foreach($swap in $report.swaps){foreach($stage in @('selected','applied','world')){
        $name="visuals-$stage-$($swap.actor)-$($swap.appearance).png"
        $file=Get-Item -LiteralPath (Join-Path $Source $name)
        if($file.PSIsContainer -or $file.Length -le 0){throw "Missing villager visual capture: $name"}
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target $name)
    }}
    Copy-Item -LiteralPath $build -Destination $target
    Copy-Item -LiteralPath (Join-Path $Source 'villager-visuals-report.json') -Destination $target
}
function Copy-JobEvidence([string]$Source,[string]$Destination,[string]$Commit,[string]$Version,[string]$Tag,[string]$IsoName,[string]$IsoSha256,[string[]]$Modes=@('delivery','mail')){
    $build=Join-Path $Source 'jobs-build.json'
    if(!(Test-Path -LiteralPath $build -PathType Leaf)){return}
    $evidence=Get-Content -LiteralPath $build -Raw|ConvertFrom-Json
    if($evidence.schema -ne 1 -or $evidence.commit -cne $Commit -or $evidence.version -cne $Version -or
       $evidence.tag -cne $Tag -or $evidence.isoName -cne $IsoName -or $evidence.isoSha256 -cne $IsoSha256){throw 'Job captures must match the release source, version and verified NKit image'}
    $target=Join-Path $Destination 'scenarios/jobs';New-Item -ItemType Directory -Force -Path $target|Out-Null
    foreach($mode in $Modes){
        if($mode -eq 'helipad'){continue}
        if($mode -notin @('delivery','mail','online-review')){throw "Unsupported job evidence mode: $mode"}
        $folder=Join-Path $Source $mode
        if($mode -eq 'online-review'){
            $report=Get-Content -LiteralPath (Join-Path $folder 'online-review-report.json') -Raw|ConvertFrom-Json
            if($report.scenario -cne 'online-review' -or $report.passed -ne $true -or $report.committed_records_unchanged -ne $true -or
               $report.current_world_left -ne $true -or $report.review_ui_right -ne $true -or $report.cancelled_before_networking -ne $true -or
               $report.online_or_physical_transport_qualification -ne $false -or $report.card_sequence_before -le 0 -or
               $report.card_sequence_before -ne $report.card_sequence_after){throw 'Split review must pass with the committed card unchanged and no transport qualification claim'}
            foreach($name in @('online-review-current-town.png','online-review-camera-inspection.png','online-review-cancelled.png','online-review-report.json')){
                $file=Get-Item -LiteralPath (Join-Path $folder $name)
                if($file.PSIsContainer -or $file.Length -le 0){throw "Missing split-review capture: $name"}
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target $name) -Force
            }
            continue
        }
        $report=Get-Content -LiteralPath (Join-Path $folder 'jobs-report.json') -Raw|ConvertFrom-Json
        $state=Get-Content -LiteralPath (Join-Path $folder 'jobs-state.json') -Raw|ConvertFrom-Json
        if($report.scenario -cne 'offline-jobs' -or $report.mode -cne $mode -or $report.controls_completed -ne $true -or $state.mode -cne $mode -or $state.passed -ne $true){throw "Job controller/save verification did not pass: $mode"}
        foreach($name in $report.screenshots){
            if($name -cnotmatch "\Ajobs-$mode-[a-z0-9-]+\.png\z"){throw 'Invalid job screenshot name'}
            $file=Get-Item -LiteralPath (Join-Path $folder $name)
            if($file.PSIsContainer -or $file.Length -le 0){throw "Missing job screenshot: $name"}
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target $name) -Force
        }
        Copy-Item -LiteralPath (Join-Path $folder 'jobs-report.json') -Destination (Join-Path $target "jobs-$mode-report.json") -Force
        Copy-Item -LiteralPath (Join-Path $folder 'jobs-state.json') -Destination (Join-Path $target "jobs-$mode-state.json") -Force
    }
    Copy-Item -LiteralPath $build -Destination (Join-Path $target 'jobs-build.json') -Force
}
function Get-PreparedReleaseReceipt($Identity,[string[]]$Paths){
    $hashes=[ordered]@{}
    foreach($path in $Paths){
        $hashes[[IO.Path]::GetFileName($path)]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [ordered]@{commit=$Identity.commit;version=$Identity.version;tag=$Identity.tag;sha256=$hashes}
}
function Assert-PreparedRelease($Identity,[string[]]$Paths,[string]$ReceiptPath){
    if(!(Test-Path -LiteralPath $ReceiptPath -PathType Leaf)){throw 'Build must prepare the release before publication'}
    $record=Get-Content -LiteralPath $ReceiptPath -Raw|ConvertFrom-Json -AsHashtable
    $actual=Get-PreparedReleaseReceipt $Identity $Paths
    if($record.commit -cne $actual.commit -or $record.version -cne $actual.version -or
       $record.tag -cne $actual.tag -or $record.sha256.Count -ne $actual.sha256.Count){
        throw 'Prepared release identity or asset set differs from the current build'
    }
    foreach($name in $actual.sha256.Keys){
        if($record.sha256[$name] -cne $actual.sha256[$name]){throw "Prepared release asset changed: $name"}
    }
}
if($PackageOnly -and $PublishOnly){throw 'Select either package or publish'}
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
if($validation.schema -ne 3 -or $validation.commit -ne $commit){throw 'Release validation plan must match source commit'}
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
$depot=Join-Path $out "AnimalTown-$version-depot.zip"
$package=Join-Path $out "releases/$tag"
$reports=Join-Path $package 'release-files'
$serverZip=Join-Path $package "AnimalTown-server-$version.zip"
$toolsZip=Join-Path $package "AnimalTown-tools-$version.zip"
$updateFeed=Join-Path $reports 'update.json'
$notesPath=Join-Path $reports 'release-notes.md'
$bundle=Join-Path $PSScriptRoot 'release-bundle.py'
$assets=@($nkit,$depot,$serverZip,$toolsZip,$updateFeed)
$receiptPath=Join-Path $package 'prepared-release.json'
if(!$PublishOnly){
    if(Test-Path -LiteralPath $receiptPath){Remove-Item -LiteralPath $receiptPath}
    $nkitValidation=Join-Path $out 'nkit-validation.json'
    & dotnet run --project (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-build -- verify-nkit-output $Root $nkit $nkitValidation
    if($LASTEXITCODE -ne 0){throw 'Built NKit image/report validation failed'}
    $nkitInfo=Get-Content $nkitValidation -Raw|ConvertFrom-Json
    $openingSource=Join-Path $out 'disc/opening.bnr'
    $palSource=Join-Path $out 'brand/opening-pal.bnr'
    $bannerHashes=Get-ReleaseBannerHashes -Opening $openingSource -Pal $palSource -NKitInfo $nkitInfo -Version $version
    & dotnet run --project (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-build -- verify-depot $Root $depot
    if($LASTEXITCODE -ne 0){throw 'Standalone app package validation failed'}
    New-Item -ItemType Directory -Force -Path $package | Out-Null
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
    Copy-VideoEvidence (Join-Path $out 'video-options') $reports $commit $version $tag ([IO.Path]::GetFileName($nkit)) $nkitInfo.compactSha256
    Copy-VillagerVisualEvidence (Join-Path $out 'villager-visuals') $reports $commit $version $tag ([IO.Path]::GetFileName($nkit)) $nkitInfo.compactSha256
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
    Copy-DaylightEvidence (Join-Path $out 'daylight') $reports $commit $version $tag ([IO.Path]::GetFileName($nkit)) $nkitInfo.compactSha256
    $jobModes=@($validation.emulator|Where-Object {$_ -like 'job-*'}|ForEach-Object {$_.Substring(4)})
    Copy-JobEvidence (Join-Path $out 'jobs') $reports $commit $version $tag ([IO.Path]::GetFileName($nkit)) $nkitInfo.compactSha256 $jobModes
    $toolsDirectory=Join-Path $out 'tools-published-binaries'
    if(Test-Path -LiteralPath $toolsDirectory){Remove-Item -LiteralPath $toolsDirectory -Recurse -Force}
    & dotnet publish (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-self-contained -o $toolsDirectory -v quiet
    if($LASTEXITCODE -ne 0){throw 'Offline tool publication failed'}
    Copy-Item -LiteralPath (Join-Path $Root 'docs/texture-repacker.md') -Destination (Join-Path $toolsDirectory 'texture-repacker.md') -Force
    Copy-Item -LiteralPath (Join-Path $Root 'docs/uv-editor-design.md') -Destination (Join-Path $toolsDirectory 'uv-editor-design.md') -Force
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
    Write-UpdateFeed -Iso $nkit -Version $version -Repository $Repository -Destination $updateFeed
    # Keep GitHub's full release response within the updater's 32 KiB budget.
    # Detailed reports and optional emulator captures live in the Depot ZIP.
    [IO.File]::WriteAllText($notesPath,(Get-ReleaseNotes $identity))
    # The containing Depot ZIP cannot hash itself. GitHub's asset digest covers it.
    $checksumFiles=@($nkit,$serverZip,$toolsZip)+@(Get-ChildItem -LiteralPath $reports -File | ForEach-Object FullName)
    $checksums=foreach($file in $checksumFiles){"$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"}
    [IO.File]::WriteAllText((Join-Path $reports 'SHA256SUMS.txt'),($checksums -join "`n")+"`n")
    Run python3 @($bundle,'attach',$depot,$reports)
    Run dotnet @('run','--project',(Join-Path $Root 'tools/AnimalTown.Tools'),'-c','Release','--no-build','--','verify-depot',$Root,$depot)
    foreach($asset in $assets){Assert-ReleaseAsset $asset}
    $receipt=Get-PreparedReleaseReceipt $identity ($assets+@($notesPath))
    [IO.File]::WriteAllText($receiptPath,(ConvertTo-Json -InputObject $receipt -Depth 4)+"`n")
}
if($PackageOnly){Write-Host "Release package: $package";Write-Host "Disc image: $nkit";return}
Assert-PreparedRelease $identity ($assets+@($notesPath)) $receiptPath
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
        # Releases published during the ZIP/ISO-only interval may lack the feed.
        # Validate their original bytes before adding only that missing asset.
        Assert-ReleaseAssetSet $release $names -AllowMissingUpdateFeed
        # Optional evidence may have extended the published Depot ZIP since the
        # primary build. Verify it and all original reports without discarding it.
        $remoteDepot=Join-Path $package 'published-depot.zip'
        InvokeGh @('release','download',$tag,'--repo',$Repository,'--pattern',([IO.Path]::GetFileName($depot)),'--output',$remoteDepot,'--clobber') | Out-Null
        Run dotnet @('run','--project',(Join-Path $Root 'tools/AnimalTown.Tools'),'-c','Release','--no-build','--','verify-depot',$Root,$remoteDepot)
        $remoteReports=Join-Path $package 'published-reports'
        Run python3 @($bundle,'extract',$remoteDepot,$remoteReports)
        $publishedSums=Get-Content -LiteralPath (Join-Path $remoteReports 'SHA256SUMS.txt') -Raw
        $publishedFeed=Join-Path $remoteReports 'update.json'
        if((Get-FileHash -LiteralPath $publishedFeed).Hash -cne (Get-FileHash -LiteralPath $updateFeed).Hash){throw 'Published update feed differs from the verified ISO'}
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
            if($asset -eq $updateFeed -and !$uploaded.Count){continue}
            if($uploaded.Count -ne 1 -or $uploaded[0].size -le 0 -or $uploaded[0].digest -cne 'sha256:'+$digest){throw "Published checksum mismatch: $name"}
        }
        if(!@($release.assets | Where-Object name -CEQ 'update.json').Count){
            InvokeGh @('release','upload',$tag,'--repo',$Repository,$publishedFeed) | Out-Null
            $release=Read-ReleaseMetadata $tag | ConvertFrom-Json
            Assert-ReleaseAssetSet $release $names
            $uploaded=@($release.assets | Where-Object name -CEQ 'update.json')
            $file=Get-Item -LiteralPath $publishedFeed
            $digest='sha256:'+(Get-FileHash -LiteralPath $publishedFeed).Hash.ToLowerInvariant()
            if($uploaded.Count -ne 1 -or $uploaded[0].size -ne $file.Length -or $uploaded[0].digest -cne $digest){throw 'Restored update feed verification failed'}
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
Assert-ReleaseAssetSet ($metadata | ConvertFrom-Json) @($assets | ForEach-Object {[IO.Path]::GetFileName($_)})
foreach($asset in $assets){
    $file=Get-Item -LiteralPath $asset
    $uploaded=@($remote | Where-Object name -CEQ $file.Name)
    $expected='sha256:'+(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if($uploaded.Count -ne 1 -or $uploaded[0].size -ne $file.Length -or $uploaded[0].digest -cne $expected){throw "Uploaded asset verification failed: $($file.Name)"}
}
$preview=(!$identity.isMain).ToString().ToLowerInvariant()
InvokeGh @('release','edit',$tag,'--repo',$Repository,'--draft=false',"--prerelease=$preview") | Out-Null
Write-Host "Published: https://github.com/$Repository/releases/tag/$tag"
