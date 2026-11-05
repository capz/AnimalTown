param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
Set-Location $Root
. ./tools/version.ps1
$identity=Get-BuildVersion (Get-Location).Path
Write-VersionMetadata $identity (Get-Location).Path
dotnet publish server/AnimalTown.Server -c Release -o out/server
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed' }
$commit=(& git rev-parse HEAD).Trim()
$dirty=[bool](& git status --porcelain --untracked-files=normal)
[IO.File]::WriteAllText((Join-Path (Get-Location) 'out/server/BUILD.txt'),"Version: $($identity.version)`nServer source: $commit`nDirty: $dirty`nRuntime: .NET 10`nBuild image: $env:ANIMALTOWN_BUILD_IMAGE`n")
