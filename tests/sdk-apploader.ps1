$ErrorActionPreference='Stop'
$gameRoot=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('animaltown-sdk-boot-'+[Guid]::NewGuid().ToString('N'))
$script:downloads=0;$script:badDownload=$false
$script:image=[Text.Encoding]::ASCII.GetBytes('verified test SDK image payload')
$script:license=[Text.Encoding]::ASCII.GetBytes('test SDK license')
function Hash([byte[]]$bytes){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
function Assert([bool]$condition,[string]$message){if(!$condition){throw $message}}
function Invoke-WebRequest([string]$Uri,[string]$OutFile){
    $script:downloads++
    $bytes=if($script:badDownload){[byte[]]@(1,2,3)}elseif($Uri.EndsWith('/LICENSE')){$script:license}else{$script:image}
    [IO.File]::WriteAllBytes($OutFile,$bytes)
}
function Resolve-Fixture { . (Join-Path $fixture 'tools/resolve-apploader.ps1') }
try{
    New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'tools'),(Join-Path $fixture 'build')|Out-Null
    Copy-Item (Join-Path $gameRoot 'tools/resolve-apploader.ps1') (Join-Path $fixture 'tools/resolve-apploader.ps1')
    $commit='a'*40;$cache=Join-Path $fixture "out/sdk-apploader/$commit"
    New-Item -ItemType Directory -Force -Path $cache|Out-Null
    $pin=@{schema=1;repository='capz/DolphinDotNet';commit=$commit;tag="apploader-$($commit.Substring(0,12))";files=@{'apploader.img'=@{bytes=$script:image.Length;sha256=(Hash $script:image)};LICENSE=@{bytes=$script:license.Length;sha256=(Hash $script:license)}}}
    $lock=Join-Path $fixture 'build/apploader.lock.json';$pin|ConvertTo-Json -Depth 5|Set-Content $lock
    [IO.File]::WriteAllBytes((Join-Path $cache 'apploader.img'),$script:image);[IO.File]::WriteAllBytes((Join-Path $cache 'LICENSE'),$script:license)
    Resolve-Fixture
    Assert ($script:downloads -eq 0) 'Verified cache must not download again'
    $output=Join-Path $fixture 'out/apploader.img'
    Assert ((Get-FileHash $output).Hash.ToLowerInvariant() -ceq (Hash $script:image)) 'Wrong image published from verified cache'
    Assert (Test-Path (Join-Path $fixture 'out/disc/sdk-apploader-LICENSE.txt')) 'SDK license did not enter disc assets'
    [IO.File]::WriteAllBytes((Join-Path $cache 'apploader.img'),[byte[]]@(8,9));$script:badDownload=$true;$failed=$false
    try{Resolve-Fixture}catch{$failed=$true}
    Assert $failed 'Corrupt downloaded image was accepted'
    Assert ((Get-FileHash $output).Hash.ToLowerInvariant() -ceq (Hash $script:image)) 'Failed verification replaced the previous output'
    Assert (@(Get-ChildItem $cache -Filter '*.tmp').Count -eq 0) 'Failed download left an incoming file behind'
    $script:badDownload=$false;Resolve-Fixture
    Assert ($script:downloads -eq 2) 'Corrupt cache was not repaired through verified download'
    $pin.tag='latest';$pin|ConvertTo-Json -Depth 5|Set-Content $lock;$failed=$false
    try{Resolve-Fixture}catch{$failed=$true}
    Assert $failed 'Mutable or mismatched SDK release tag was accepted'
    Write-Host 'SDK boot resolver: immutable identity, cached hashes, license, corrupt download rejection, atomic output preservation and cache repair passed'
}finally{if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}}
