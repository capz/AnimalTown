param([string]$Root=(Split-Path $PSScriptRoot -Parent),[string]$GitHubOutput='',[switch]$Refresh)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot 'version.ps1')
$identity=Get-BuildVersion $Root -Refresh:$Refresh
Write-VersionMetadata $identity $Root
if($GitHubOutput){
    Add-Content -LiteralPath $GitHubOutput -Value "version=$($identity.version)`ntag=$($identity.tag)`ncommit=$($identity.commit)`ncanPublish=$($identity.canPublish.ToString().ToLowerInvariant())"
}
Write-Host "Version: $($identity.version); dirty=$($identity.dirty)"
