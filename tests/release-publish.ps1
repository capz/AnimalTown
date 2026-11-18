$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot '../tools/publish-release.ps1'),[ref]$tokens,[ref]$errors)
if($errors){throw 'Publisher syntax is invalid'}
foreach($name in @('InvokeGh','Read-ReleaseMetadata','Assert-BinaryArchive','Assert-ReleaseVersionMetadata','Assert-ReleaseAsset','Assert-ReleaseAssetSet')){
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
    foreach($name in @('program.dll','source.cs','provision.py','model.glb','source.tar.gz','source.zip')){
        $zip=Join-Path $directory 'fixture.zip'
        $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
        try{$stream=$archive.CreateEntry($name).Open();$stream.Dispose()}finally{$archive.Dispose()}
        if($name -eq 'program.dll'){Assert-BinaryArchive $zip;Assert $true 'Compiled output allowed'}
        else{Reject {Assert-BinaryArchive $zip} "Source file must fail: $name"}
        Remove-Item -LiteralPath $zip
    }
}finally{Remove-Item -LiteralPath $directory -Recurse -Force}
Write-Host "$checks release publisher checks passed: compact-feed asset policy, frozen build identity, draft lookup, response budget and source exclusion"
