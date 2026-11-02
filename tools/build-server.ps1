param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
Set-Location $Root
dotnet publish server/AnimalTown.Server -c Release -o out/server
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed' }
$commit=(& git rev-parse HEAD).Trim()
$dirty=[bool](& git status --porcelain --untracked-files=normal)
[IO.File]::WriteAllText((Join-Path (Get-Location) 'out/server/BUILD.txt'),"Server source: $commit`nDirty: $dirty`nRuntime: .NET 10`n")
