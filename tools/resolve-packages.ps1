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
$sdkVersion=[string]$sdkMetadata.package.metadata.version
$assetVersion=(Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) '.config/dotnet-tools.json') -Raw|ConvertFrom-Json).tools.'dolphindotnet.assets'.version
[xml]$toolsProject=Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'tools/AnimalTown.Tools/AnimalTown.Tools.csproj') -Raw
if($assetVersion -ne $sdkVersion -or [string]$toolsProject.Project.PropertyGroup.DndAssetsVersion -ne $sdkVersion){throw 'Compiler, Assets tool and Imaging package must use the same DolphinDotNet version'}
$runtimeDependency=$engineMetadata.package.metadata.dependencies.group.dependency|Where-Object id -eq 'DolphinDotNet.GameCube'
if([string]$runtimeDependency.version -ne "[$sdkVersion]"){
    # Hypercube 28's SDK dependency is exact. The additive 318 alpha APIs are
    # verified by rendering checks against the engine's existing public surface.
    # Require this explicit, reviewed override rather than accepting drift.
    $runtimeOverride=$gameProject.Project.ItemGroup.PackageReference|Where-Object Include -eq 'DolphinDotNet.GameCube'
    if($engineVersion -ne '0.1.0-preview.28.1' -or
       [string]$runtimeDependency.version -ne '[0.1.0-preview.315.1]' -or
       $sdkVersion -ne '0.1.0-preview.318.1' -or
       [string]$runtimeOverride.Version -ne "[$sdkVersion]"){
        throw 'Engine/runtime mismatch without a verified explicit override'
    }
}
$engineCommit="$($engineMetadata.package.metadata.id) $($engineMetadata.package.metadata.version) ($($engineMetadata.package.metadata.repository.commit))"
$sdkCommit="$($sdkMetadata.package.metadata.id) $($sdkMetadata.package.metadata.version) ($($sdkMetadata.package.metadata.repository.commit))"
$sdkContentHash=(Get-Content (Join-Path $sdkRoot "dolphindotnet.sdk.$($sdkMetadata.package.metadata.version).nupkg.sha512") -Raw).Trim()
$engineContentHash=(Get-Content (Join-Path $engineRoot "capz.hypercube.gamecube.$engineVersion.nupkg.sha512") -Raw).Trim()
