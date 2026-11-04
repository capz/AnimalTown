param([Parameter(Mandatory)][string]$Title)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'version.ps1')
$kind=Get-ChangeKind $Title
Write-Host "Change classification: $kind"
