$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '../tools/publish-release.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw 'Publisher syntax is invalid'}
foreach($name in @('InvokeGh','Read-ReleaseMetadata','Assert-BinaryArchive','Assert-ReleaseVersionMetadata','Assert-ReleaseAsset','Assert-ReleaseAssetSet','Copy-DaylightEvidence','Copy-VideoEvidence','Copy-VillagerVisualEvidence','Copy-JobEvidence','Get-ReleaseNotes','Get-PreparedReleaseReceipt','Assert-PreparedRelease')){
    $function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
    . ([scriptblock]::Create($function.Extent.Text))
}
$Repository='capz/AnimalTown';$Gh='Invoke-MockGh';$calls=[Collections.Generic.List[string]]::new()
$apiUrl='https://api.github.com/repos/capz/AnimalTown/releases/12345'
$response='{"draft":true,"assets":[]}'
function Invoke-MockGh {
    $calls.Add(($args -join '|'));$global:LASTEXITCODE=0
    if($args[0] -eq 'release'){return $apiUrl}
    if($args[0] -eq 'api' -and $args[1] -ceq $apiUrl){return $response}
    throw 'Unexpected GitHub command'
}
$checks=0
function Assert($condition,[string]$message){$script:checks++;if(!$condition){throw $message}}
function Reject([scriptblock]$action,[string]$message){$refused=$false;try{& $action}catch{$refused=$true};Assert $refused $message}
$names=@('AnimalTown-1.2.3.nkit.iso','AnimalTown-1.2.3-depot.zip','AnimalTown-server-1.2.3.zip','AnimalTown-tools-1.2.3.zip','update.json')
function Release([string[]]$Names){return [pscustomobject]@{assets=@($Names | ForEach-Object {[pscustomobject]@{name=$_}})}}
Assert-ReleaseAssetSet (Release $names) $names
Assert-ReleaseAssetSet (Release $names[0..3]) $names -AllowMissingUpdateFeed
Reject {Assert-ReleaseAssetSet (Release $names[0..3]) $names} 'New releases must include the compact update feed'
Reject {Assert-ReleaseAssetSet (Release $names[1..4]) $names -AllowMissingUpdateFeed} 'Feed restoration must not excuse a missing ISO'
Reject {Assert-ReleaseAssetSet (Release ($names+@('version.json'))) $names} 'Other loose JSON remains forbidden'
Reject {Assert-ReleaseAssetSet (Release ($names+@('AnimalTown-cliff-kit-1.2.3.zip'))) $names} 'A separate cliff authoring kit must not enter the release asset set'
Reject {Assert-ReleaseAssetSet (Release ($names+@('update.json'))) $names} 'Duplicate update feeds must fail'
foreach($path in @('/release/update.json','AnimalTown-1.2.3.nkit.iso')){Assert-ReleaseAsset $path}
foreach($path in @('version.json','UPDATE.JSON','source.tar.gz','animal-town-updater.dol')){
    Reject {Assert-ReleaseAsset $path} 'The compact update feed is the only non-ZIP/ISO release asset'
}
Assert ((Read-ReleaseMetadata 'v1.2.3+abcdef123456') -ceq $response) 'Draft metadata was not returned'
Assert ($calls[0] -ceq 'release|view|v1.2.3+abcdef123456|--repo|capz/AnimalTown|--json|apiUrl|--jq|.apiUrl') 'Draft must use gh draft-aware lookup'
Assert ($calls[1] -ceq 'api|https://api.github.com/repos/capz/AnimalTown/releases/12345') 'Draft must use its numeric REST URL, not a nonexistent tag endpoint'
foreach($url in @('https://api.github.com/repos/evil/other/releases/12345','https://example.com/repos/capz/AnimalTown/releases/12345','https://api.github.com/repos/capz/AnimalTown/releases/RE_node_id')){
    $apiUrl=$url;Reject {Read-ReleaseMetadata 'v1.2.3'} 'Foreign or GraphQL release URL must fail'
}
$apiUrl='https://api.github.com/repos/capz/AnimalTown/releases/12345'
$response='x'*32769;Reject {Read-ReleaseMetadata 'v1.2.3'} 'Oversized metadata must fail before publication'
$response=[string][char]0xE9;$response=$response*16385;Reject {Read-ReleaseMetadata 'v1.2.3'} 'Metadata budget must count UTF-8 bytes'
$directory=Join-Path ([IO.Path]::GetTempPath()) ('release-publish-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $directory | Out-Null
try{
    $identity=[pscustomobject]@{schema=2;version='1.2.3';commit=('a'*40);mainCommit=('b'*40);dirty=$false;changes=@([pscustomobject]@{number=112;commit=('c'*40)})}
    $versionPath=Join-Path $directory 'version.json'
    # Equivalent JSON with a different key order must be verified, never rewritten.
    $saved=@{changes=@(@{commit=('c'*40);number=112});dirty=$false;mainCommit=('b'*40);commit=('a'*40);version='1.2.3';schema=2}
    [IO.File]::WriteAllText($versionPath,($saved | ConvertTo-Json -Depth 5)+"`n`n")
    $original=(Get-FileHash -LiteralPath $versionPath).Hash
    Assert-ReleaseVersionMetadata $identity $versionPath
    Assert ((Get-FileHash -LiteralPath $versionPath).Hash -ceq $original) 'Publication must retain embedded version metadata byte-for-byte'
    foreach($field in @('commit','mainCommit','version','dirty')){
        $changed=$saved.Clone();$changed[$field]=if($field -eq 'dirty'){$true}else{'wrong'}
        [IO.File]::WriteAllText($versionPath,($changed | ConvertTo-Json -Depth 5))
        Reject {Assert-ReleaseVersionMetadata $identity $versionPath} "Stale built identity must fail: $field"
    }
    $changed=$saved.Clone();$changed.changes=@(@{number=113;commit=('d'*40)})
    [IO.File]::WriteAllText($versionPath,($changed | ConvertTo-Json -Depth 5))
    Reject {Assert-ReleaseVersionMetadata $identity $versionPath} 'Changed integration history must fail'
    $identity|Add-Member -NotePropertyName summary -NotePropertyValue 'See the latest changes.'
    $identity|Add-Member -NotePropertyName requestSummaries -NotePropertyValue @([pscustomobject]@{id='latest';lines=@($identity.summary,'First detail.','Second detail.','Third detail.')})
    $saved.summary=$identity.summary;$saved.requestSummaries=@([ordered]@{lines=$identity.requestSummaries[0].lines;id='latest'})
    [IO.File]::WriteAllText($versionPath,($saved | ConvertTo-Json -Depth 5))
    Assert-ReleaseVersionMetadata $identity $versionPath
    $saved.requestSummaries[0].lines=@($identity.summary,'Different detail.','Second detail.','Third detail.')
    [IO.File]::WriteAllText($versionPath,($saved | ConvertTo-Json -Depth 5))
    Reject {Assert-ReleaseVersionMetadata $identity $versionPath} 'Changed request details must fail publication'
    $notes=Get-ReleaseNotes $identity
    Assert ($notes.Contains("## What's new`n`nSee the latest changes.`n`n") -and [Text.Encoding]::UTF8.GetByteCount($notes) -le 1024) 'Release notes carry the bounded updater section'
    $identity.summary=''
    Assert (!(Get-ReleaseNotes $identity).Contains("## What's new")) 'Older identities omit an empty updater section'
    foreach($name in @('program.dll','source.cs','provision.py','model.glb','source.tar.gz','source.zip')){
        $zip=Join-Path $directory 'fixture.zip'
        $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
        try{$stream=$archive.CreateEntry($name).Open();$stream.Dispose()}finally{$archive.Dispose()}
        if($name -eq 'program.dll'){Assert-BinaryArchive $zip;Assert $true 'Compiled output allowed'}
        else{Reject {Assert-BinaryArchive $zip} "Source file must fail: $name"}
        Remove-Item -LiteralPath $zip
    }
    $capture=Join-Path $directory 'daylight';$reports=Join-Path $directory 'reports'
    New-Item -ItemType Directory -Path $capture,$reports|Out-Null
    Copy-DaylightEvidence $capture $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)
    $preparedFiles=@((Join-Path $directory 'prepared.iso'),(Join-Path $directory 'prepared.zip'))
    foreach($file in $preparedFiles){[IO.File]::WriteAllText($file,'built bytes')}
    $receiptPath=Join-Path $directory 'prepared-release.json'
    Reject {Assert-PreparedRelease $identity $preparedFiles $receiptPath} 'Publication requires a prepared package receipt'
    $receipt=Get-PreparedReleaseReceipt $identity $preparedFiles
    [IO.File]::WriteAllText($receiptPath,(ConvertTo-Json -InputObject $receipt -Depth 4))
    Assert-PreparedRelease $identity $preparedFiles $receiptPath
    Assert $true 'Release accepts the exact files that Build prepared'
    [IO.File]::AppendAllText($preparedFiles[1],'unexpected bytes')
    Reject {Assert-PreparedRelease $identity $preparedFiles $receiptPath} 'Publication refuses a modified build asset'

    Assert (@(Get-ChildItem $reports).Count -eq 0) 'Unselected daylight captures remain optional'
    $evidence=@{schema=1;commit=('a'*40);version='1.2.3';tag='v1.2.3';isoName='AnimalTown-1.2.3.nkit.iso';isoSha256=('b'*64)}
    $build=Join-Path $capture 'daylight-build.json';$evidence|ConvertTo-Json|Set-Content $build
    foreach($entry in ([ordered]@{noon='12:00';dawn='06:30';dusk='18:00';night='20:00'}).GetEnumerator()){
        $folder=Join-Path $capture $entry.Key;New-Item -ItemType Directory -Path $folder|Out-Null
        @{passed=$true;requested_clock=$entry.Value}|ConvertTo-Json|Set-Content (Join-Path $folder 'daylight-report.json')
        foreach($shot in @('a','b')){[IO.File]::WriteAllBytes((Join-Path $folder "daylight-$shot.png"),[byte[]]@(137,80,78,71,13,10,26,10))}
    }
    Copy-DaylightEvidence $capture $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)
    Assert (@(Get-ChildItem $reports).Count -eq 13) 'All four phase reports/screenshots and their source identity are included in release reports'
    Reject {Copy-DaylightEvidence $capture $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('c'*64)} 'Captures from another ISO must not ship'
    @{passed=$false;requested_clock='20:00'}|ConvertTo-Json|Set-Content (Join-Path $capture 'night/daylight-report.json')
    Reject {Copy-DaylightEvidence $capture $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)} 'Failed night captures must not ship'
    @{passed=$true;requested_clock='12:00'}|ConvertTo-Json|Set-Content (Join-Path $capture 'night/daylight-report.json')
    Reject {Copy-DaylightEvidence $capture $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)} 'A mislabeled phase must not ship'
    $visuals=Join-Path $directory 'villager-visuals';New-Item -ItemType Directory -Path $visuals|Out-Null
    $evidence|ConvertTo-Json|Set-Content (Join-Path $visuals 'villager-visuals-build.json')
    $swaps=@(6..13|ForEach-Object{@{actor=0;appearance=$_}})+@(@{actor=1;appearance=6})
    $visualReport=@{scenario='villager-visuals';passed=$true;committed_records_unchanged=$true;swaps=$swaps}
    $visualReport|ConvertTo-Json -Depth 4|Set-Content (Join-Path $visuals 'villager-visuals-report.json')
    foreach($swap in $swaps){foreach($stage in @('selected','applied','world')){
        [IO.File]::WriteAllBytes((Join-Path $visuals "visuals-$stage-$($swap.actor)-$($swap.appearance).png"),[byte[]]@(137,80,78,71,13,10,26,10))
    }}
    Copy-VillagerVisualEvidence $visuals $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64) 14
    Assert (@(Get-ChildItem (Join-Path $reports 'scenarios/villager-visuals') -File).Count -eq 29) 'All eight additional appearances, NPC swap, unchanged-save report and exact build identity ship together'
    $visualReport.swaps=@($swaps|Where-Object{$_.appearance -lt 12})
    $visualReport|ConvertTo-Json -Depth 4|Set-Content (Join-Path $visuals 'villager-visuals-report.json')
    Reject {Copy-VillagerVisualEvidence $visuals $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64) 14} 'Old six-appearance reports cannot omit the crocodile and capybara'
    $visualReport.swaps=@($swaps|ForEach-Object{if($_.appearance -eq 13){@{actor=0;appearance=12}}else{$_}})
    $visualReport|ConvertTo-Json -Depth 4|Set-Content (Join-Path $visuals 'villager-visuals-report.json')
    Reject {Copy-VillagerVisualEvidence $visuals $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64) 14} 'A duplicated crocodile capture cannot stand in for the capybara'
    $visualReport.swaps=$swaps
    $visualReport|ConvertTo-Json -Depth 4|Set-Content (Join-Path $visuals 'villager-visuals-report.json')
    Reject {Copy-VillagerVisualEvidence $visuals $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('c'*64) 14} 'Stale visual captures from another executable must not ship'
    $visualReport.committed_records_unchanged=$false
    $visualReport|ConvertTo-Json -Depth 4|Set-Content (Join-Path $visuals 'villager-visuals-report.json')
    Reject {Copy-VillagerVisualEvidence $visuals $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64) 14} 'Visual evidence that changed committed records must not ship'
    $video=Join-Path $directory 'video';$videoReports=Join-Path $directory 'video-reports'
    New-Item -ItemType Directory -Path $video,$videoReports|Out-Null
    foreach($group in @('smoothing','options','unsupported')){New-Item -ItemType Directory -Path (Join-Path $video $group)|Out-Null}
    $evidence|ConvertTo-Json|Set-Content (Join-Path $video 'video-options-build.json')
    @{passed=$true;restoredAfterRestart=$true;townUnchanged=$true}|ConvertTo-Json|Set-Content (Join-Path $video 'smoothing/aa-option.json')
    foreach($name in @('options/title-options.json','options/title-options-load.json','unsupported/title-options-unsupported.json')){
        '{"passed":true}'|Set-Content (Join-Path $video $name)
    }
    foreach($name in @('smoothing/aa-initial-home.png','smoothing/aa-menu-on.png','smoothing/aa-menu-off.png','smoothing/aa-menu-restored.png','smoothing/aa-576p-rejected.png','options/options-restored.png','options/options-delete-default.png','unsupported/options-unsupported.png')){
        [IO.File]::WriteAllBytes((Join-Path $video $name),[byte[]]@(137,80,78,71,13,10,26,10))
    }
    Copy-VideoEvidence $video $videoReports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)
    Assert (@(Get-ChildItem $videoReports -File).Count -eq 13) 'Video reports, screenshots and build identity are copied together'
    # Exercise the real publisher output through the actual bundle writer.
    $videoZip=Join-Path $directory 'video-depot.zip';$runtimeBytes=[Text.Encoding]::UTF8.GetBytes('compiled fixture executable')
    $runtimeHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($runtimeBytes)).ToLowerInvariant()
    $appManifest=@{Schema=1;Profile='sd-app';Commit=('a'*40);Version='1.2.3';Files=@{'default.dol'=$runtimeHash}}
    $archive=[IO.Compression.ZipFile]::Open($videoZip,[IO.Compression.ZipArchiveMode]::Create)
    try{
        $stream=$archive.CreateEntry('apps/animaltown/default.dol').Open();try{$stream.Write($runtimeBytes)}finally{$stream.Dispose()}
        $stream=$archive.CreateEntry('apps/animaltown/depot-package.json').Open();try{$stream.Write([Text.Encoding]::UTF8.GetBytes(($appManifest|ConvertTo-Json -Depth 4)))}finally{$stream.Dispose()}
    }finally{$archive.Dispose()}
    & python3 (Join-Path $PSScriptRoot '../tools/release-bundle.py') attach $videoZip $videoReports
    Assert ($LASTEXITCODE -eq 0) 'The release bundle must accept every file emitted by the video publisher'
    & python3 (Join-Path $PSScriptRoot '../tools/release-bundle.py') verify $videoZip
    Assert ($LASTEXITCODE -eq 0) 'Video evidence bundle hashes and unchanged app payload verify'
    Reject {Copy-VideoEvidence $video $videoReports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('c'*64)} 'Video evidence from another ISO must not ship'
    @{passed=$false;restoredAfterRestart=$true;townUnchanged=$true}|ConvertTo-Json|Set-Content (Join-Path $video 'smoothing/aa-option.json')
    Reject {Copy-VideoEvidence $video $videoReports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)} 'Failed video checks must not ship'
    $jobs=Join-Path $directory 'jobs';New-Item -ItemType Directory -Path $jobs|Out-Null
    Copy-JobEvidence $jobs $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)
    $evidence|ConvertTo-Json|Set-Content (Join-Path $jobs 'jobs-build.json')
    foreach($mode in @('delivery','mail')){
        $folder=Join-Path $jobs $mode;New-Item -ItemType Directory -Path $folder|Out-Null
        @{scenario='offline-jobs';mode=$mode;controls_completed=$true;screenshots=@("jobs-$mode-result.png")}|ConvertTo-Json|Set-Content (Join-Path $folder 'jobs-report.json')
        @{mode=$mode;passed=$true}|ConvertTo-Json|Set-Content (Join-Path $folder 'jobs-state.json')
        [IO.File]::WriteAllBytes((Join-Path $folder "jobs-$mode-result.png"),[byte[]]@(137,80,78,71,13,10,26,10))
    }
    Copy-JobEvidence $jobs $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)
    Assert (@(Get-ChildItem (Join-Path $reports 'scenarios/jobs') -File).Count -eq 7) 'Job captures, independently verified save state and exact build identity ship together'
    Reject {Copy-JobEvidence $jobs $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('c'*64)} 'Job evidence from another executable image must not ship'
    @{mode='delivery';passed=$false}|ConvertTo-Json|Set-Content (Join-Path $jobs 'delivery/jobs-state.json')
    Reject {Copy-JobEvidence $jobs $reports ('a'*40) '1.2.3' 'v1.2.3' 'AnimalTown-1.2.3.nkit.iso' ('b'*64)} 'Screenshots without a completed real saved errand must not ship'
}finally{Remove-Item -LiteralPath $directory -Recurse -Force}
Write-Host "$checks release publisher checks passed: compact-feed asset policy, frozen build identity, summaries, draft lookup, response budget and source exclusion"
