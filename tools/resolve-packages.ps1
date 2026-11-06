# Single resolver for compiler/native support; never writes to the NuGet cache.
$ErrorActionPreference='Stop'
$packageProject=Join-Path (Split-Path $PSScriptRoot -Parent) 'game/AnimalTown.GameCube/AnimalTown.GameCube.csproj'
& dotnet restore $packageProject --locked-mode
if($LASTEXITCODE){throw 'Pinned package restore failed'}
$packageRoot=(& dotnet msbuild $packageProject -nologo -getProperty:_DndPackageRoot).Trim()
if($LASTEXITCODE -or !(Test-Path "$packageRoot/tools/compiler/DolphinDotNet.Compiler.dll")){throw 'Packaged compiler not resolved'}
$sdkRoot=$packageRoot
$sdkNative=Join-Path (Split-Path $PSScriptRoot -Parent) 'out/sdk-native'
New-Item -ItemType Directory -Force -Path $sdkNative|Out-Null
foreach($folder in @('include','source')){Copy-Item -LiteralPath "$sdkRoot/tools/compiler/native/$folder" -Destination $sdkNative -Recurse -Force}

$packageCache=(& dotnet msbuild $packageProject -nologo -getProperty:NuGetPackageRoot).Trim()
if($LASTEXITCODE){throw 'NuGet cache resolution failed'}
[xml]$gameProject=Get-Content $packageProject -Raw
$engineVersion=([string]($gameProject.Project.ItemGroup.PackageReference|Where-Object Include -eq 'Capz.Hypercube.GameCube').Version).Trim('[',']')
$engineRoot=Join-Path $packageCache "capz.hypercube.gamecube/$engineVersion"
[xml]$engineMetadata=Get-Content (Join-Path $engineRoot 'capz.hypercube.gamecube.nuspec') -Raw
[xml]$sdkMetadata=Get-Content (Join-Path $sdkRoot 'dolphindotnet.sdk.nuspec') -Raw
$engineCommit="$($engineMetadata.package.metadata.id) $($engineMetadata.package.metadata.version) ($($engineMetadata.package.metadata.repository.commit))"
$sdkCommit="$($sdkMetadata.package.metadata.id) $($sdkMetadata.package.metadata.version) ($($sdkMetadata.package.metadata.repository.commit))"
$sdkContentHash=(Get-Content (Join-Path $sdkRoot "dolphindotnet.sdk.$($sdkMetadata.package.metadata.version).nupkg.sha512") -Raw).Trim()
$engineContentHash=(Get-Content (Join-Path $engineRoot "capz.hypercube.gamecube.$engineVersion.nupkg.sha512") -Raw).Trim()
