param([string]$Root=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
Set-Location $Root
. ./tools/version.ps1
$identity=Get-BuildVersion (Get-Location).Path
Write-VersionMetadata $identity (Get-Location).Path
dotnet publish server/AnimalTown.Server -c Release -o out/server
if ($LASTEXITCODE -ne 0) { throw 'Server publish failed' }
Copy-Item -LiteralPath tools/online-poc.ps1 -Destination out/server/online-poc.ps1 -Force
Copy-Item -LiteralPath tools/online-account.ps1 -Destination out/server/online-account.ps1 -Force
$publishedCompose=[IO.File]::ReadAllText((Join-Path $Root 'build/online.compose.yml'))
$sourceBuild="    build:`n      context: ..`n      dockerfile: server/AnimalTown.Server/Dockerfile"
$runtime="    image: mcr.microsoft.com/dotnet/aspnet:10.0`n    working_dir: /app`n    volumes: ['./:/app:ro']`n    command: [dotnet, AnimalTown.Server.dll]"
if(!$publishedCompose.Replace("`r",'').Contains($sourceBuild)){throw 'Published backend compose template changed'}
[IO.File]::WriteAllText((Join-Path $Root 'out/server/online.compose.yml'),$publishedCompose.Replace("`r",'').Replace($sourceBuild,$runtime))
[IO.File]::WriteAllText((Join-Path $Root 'out/server/ONLINE-PLAY.txt'),@'
AnimalTown online PoC backend

Extract this complete backend ZIP. In PowerShell 7 run:
  ./online-poc.ps1 -EnablePlay

Docker runs persistent PostgreSQL, this exact compiled backend and the free
Cloudflare tunnel. No domain or router forwarding is needed. Keep the computer
and tunnel running for unattended guests. Enter the printed hostname using
Online > Link account / service on the console and approve its /link page.
Create an account and establish mutual friends using the included
online-account.ps1; it prompts for a password and never saves account tokens:
  ./online-account.ps1 -Origin https://YOUR-SERVICE -Name Mira -Action Register
  ./online-account.ps1 -Origin https://YOUR-SERVICE -Name Mira -Action AddFriend -Target OTHER-ACCOUNT-ID
Each friend adds the other's account ID. Use -Action Friends to check mutual
status. The same tool supports Block/Unblock and Devices/RevokeDevice.

Where the container runtime image cannot be downloaded, install the .NET 10
runtime and cloudflared, then run:
  ./online-poc.ps1 -HostRuntime -Cloudflared cloudflared -EnablePlay
Keep that terminal open. Ctrl+C stops its backend and child tunnel.

Private settings and backups live in AnimalTown-online-data beside this
extracted directory. Keep that folder and the Docker volume across updates.
Use -Action Stop to stop without erasing data; -Action Status reports it.
Use -Action Backup, then verify with -Action Restore -BackupFile PATH
-RestoreDatabase animaltown_restore_check. Restore refuses an existing database
and never replaces the running one. See the repository's online-poc-running.md
for selecting restored data and the exact acceptance/hardware requirements.

Without -EnablePlay, player mutation commands remain disabled. A new tunnel
hostname requires explicit configuration and relinking. Keep pending card
recovery records: never erase them to work around a connection failure.
'@)
$commit=(& git rev-parse HEAD).Trim()
$dirty=[bool](& git status --porcelain --untracked-files=normal)
[IO.File]::WriteAllText((Join-Path (Get-Location) 'out/server/BUILD.txt'),"Version: $($identity.version)`nServer source: $commit`nDirty: $dirty`nRuntime: .NET 10`nBuild image: $env:ANIMALTOWN_BUILD_IMAGE`n")
