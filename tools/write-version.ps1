param([string]$Root=(Split-Path $PSScriptRoot -Parent),[string]$GitHubOutput='')
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot 'version.ps1')
$identity=Get-BuildVersion $Root
Write-VersionMetadata $identity $Root
if($GitHubOutput){
    Add-Content -LiteralPath $GitHubOutput -Value "version=$($identity.version)`ntag=$($identity.tag)"
}
Write-Host "Version: $($identity.version); dirty=$($identity.dirty)"
