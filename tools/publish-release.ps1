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
$short=$commit.Substring(0,12)
$tag="build-$short"
$out=Join-Path $Root 'out'
$revisions=[IO.File]::ReadAllText((Join-Path $out 'build-revisions.txt'))
$server=Join-Path $out 'server'
$serverBuild=[IO.File]::ReadAllText((Join-Path $server 'BUILD.txt'))
if($revisions -notmatch "(?m)^Game source: $commit\r?$" -or $revisions -notmatch '(?m)^Dirty: False\r?$' -or
   $serverBuild -notmatch "(?m)^Server source: $commit\r?$" -or $serverBuild -notmatch '(?m)^Dirty: False\r?$'){
    throw 'Console and backend must both be clean builds of the release commit'
}
$iso=Join-Path $out "AnimalTown-$short.iso"
if((Get-Item -LiteralPath $iso).Length -ne 1459978240){throw 'Release requires a full-size GameCube ISO'}
& dotnet run --project (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-build -- verify $Root $iso
if($LASTEXITCODE -ne 0){throw 'ISO validation failed'}
$package=Join-Path $out "releases/$tag"
New-Item -ItemType Directory -Force -Path $package | Out-Null
$isoZip=Join-Path $package "AnimalTown-$short.iso.zip"
$serverZip=Join-Path $package "AnimalTown-server-$short.zip"
# These two exact paths are generated archives; rebuilding a draft can replace them.
foreach($archive in @($isoZip,$serverZip)){if(Test-Path -LiteralPath $archive){Remove-Item -LiteralPath $archive}}
$zip=[IO.Compression.ZipFile]::Open($isoZip,[IO.Compression.ZipArchiveMode]::Create)
try{[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$iso,[IO.Path]::GetFileName($iso),[IO.Compression.CompressionLevel]::Optimal) | Out-Null}finally{$zip.Dispose()}
[IO.Compression.ZipFile]::CreateFromDirectory($server,$serverZip,[IO.Compression.CompressionLevel]::Optimal,$false)
Copy-Item -LiteralPath (Join-Path $out 'build-revisions.txt') -Destination (Join-Path $package 'BUILD.txt') -Force
Copy-Item -LiteralPath (Join-Path $out 'texture-audit.json') -Destination (Join-Path $package 'texture-audit.json') -Force
$checksums=foreach($file in @($iso,$isoZip,$serverZip,(Join-Path $package 'BUILD.txt'),(Join-Path $package 'texture-audit.json'))){"$((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($file))"}
[IO.File]::WriteAllText((Join-Path $package 'SHA256SUMS.txt'),($checksums -join "`n")+"`n")
$notes=@"
AnimalTown development build from commit $commit.

Download **AnimalTown-$short.iso.zip** and extract the full 1,459,978,240-byte ISO. The disc includes the game title, commit identity, disc banner and memory-card banner/icon. The server is a separate **AnimalTown-server-$short.zip** archive and requires .NET 10 and PostgreSQL. It does not run on the console.

BUILD.txt records the game, engine and SDK revisions. texture-audit.json records texture source identities, fingerprints and 256-pixel default residency and 128/256/512/1024 model variants. SHA256SUMS.txt covers the archives, manifests and the extracted ISO.

This is an early playable build, not the completed game. Physical GameCube testing is outstanding. See [implementation status](https://github.com/$Repository/blob/$commit/docs/implementation-status.md) and [build instructions](https://github.com/$Repository/blob/$commit/README.md).
"@
$notesPath=Join-Path $package 'release-notes.md'
[IO.File]::WriteAllText($notesPath,$notes)
if($PackageOnly){Write-Host "Release package: $package";return}
function InvokeGh([string[]]$Arguments){
    $result=& $Gh @Arguments
    if($LASTEXITCODE -ne 0){throw "GitHub CLI failed: $($Arguments[0]) $($Arguments[1])"}
    return $result
}
$assets=@($isoZip,$serverZip,(Join-Path $package 'BUILD.txt'),(Join-Path $package 'texture-audit.json'),(Join-Path $package 'SHA256SUMS.txt'))
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
    InvokeGh @('release','create',$tag,'--repo',$Repository,'--target',$commit,'--title',"AnimalTown $short",'--notes-file',$notesPath,'--prerelease','--draft') | Out-Null
}
InvokeGh (@('release','upload',$tag,'--repo',$Repository,'--clobber')+$assets) | Out-Null
$remote=(InvokeGh @('release','view',$tag,'--repo',$Repository,'--json','assets') | ConvertFrom-Json).assets
foreach($asset in $assets){
    $file=Get-Item -LiteralPath $asset
    $uploaded=$remote | Where-Object name -EQ $file.Name
    $expected='sha256:'+(Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    if(!$uploaded -or $uploaded.size -ne $file.Length -or $uploaded.digest -ne $expected){throw "Uploaded asset verification failed: $($file.Name)"}
}
InvokeGh @('release','edit',$tag,'--repo',$Repository,'--draft=false') | Out-Null
Write-Host "Published: https://github.com/$Repository/releases/tag/$tag"
