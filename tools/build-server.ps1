$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet publish server/AnimalTown.Server -c Release -o out/server
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed' }
