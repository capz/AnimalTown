param(
    [Parameter(Mandatory)][string]$Iso,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$Report,
    [string]$Root=(Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference='Stop'
$Root=(Resolve-Path $Root).Path;$Iso=(Resolve-Path -LiteralPath $Iso).Path
$Destination=[IO.Path]::GetFullPath($Destination);$Report=[IO.Path]::GetFullPath($Report)
if([IO.Path]::GetExtension($Iso) -ine '.iso' -or $Iso.EndsWith('.nkit.iso',[StringComparison]::OrdinalIgnoreCase) -or
   !$Destination.EndsWith('.nkit.iso',[StringComparison]::OrdinalIgnoreCase) -or $Destination -ieq $Iso -or $Report -ieq $Iso -or $Report -ieq $Destination){throw 'Use a full source ISO and distinct NKit image/JSON report destinations'}
if((Get-Item -LiteralPath $Iso).Length -ne 1459978240){throw 'NKit packaging requires a full-size source ISO'}
New-Item -ItemType Directory -Force -Path (Split-Path $Destination -Parent),(Split-Path $Report -Parent)|Out-Null
. (Join-Path $Root 'build/nkit.ps1')
$tool=Get-NKitTool
$stage=Join-Path $Root ('out/nkit-work/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force|Out-Null
$before=(Get-FileHash -LiteralPath $Iso -Algorithm SHA256).Hash.ToLowerInvariant()
try{
    $packed=Join-Path $stage 'packed';$expanded=Join-Path $stage 'expanded';$temporary=Join-Path $stage 'temp'
    & $tool.Path convert --no-config --no-archives --format nkitiso --verify y --log-level none --output $packed --temp $temporary $Iso
    if($LASTEXITCODE){throw 'NKit conversion failed'}
    $images=@(Get-ChildItem -LiteralPath $packed -File -Recurse -Filter '*.nkit.iso')
    if($images.Count -ne 1){throw 'NKit did not produce exactly one compact image'}
    $image=$images[0].FullName
    & $tool.Path expand --no-config --no-archives --verify y --log-level none --output $expanded --temp $temporary $image
    if($LASTEXITCODE){throw 'NKit round-trip expansion failed'}
    $restored=@(Get-ChildItem -LiteralPath $expanded -File -Recurse -Filter '*.iso')
    if($restored.Count -ne 1 -or $restored[0].Length -ne 1459978240 -or (Get-FileHash $restored[0].FullName -Algorithm SHA256).Hash -ne $before){throw 'NKit round-trip SHA-256 does not match the original ISO'}
    $layout=Join-Path $stage 'layout.json'
    & dotnet run --project (Join-Path $Root 'tools/AnimalTown.Tools') -c Release --no-build -- verify-nkit $Root $Iso $image $layout
    if($LASTEXITCODE){throw 'Compact boot layout/payload verification failed'}
    if((Get-FileHash -LiteralPath $Iso -Algorithm SHA256).Hash -ne $before){throw 'Canonical ISO changed during packaging'}
    $validation=Get-Content $layout -Raw|ConvertFrom-Json -AsHashtable
    $validation.compactFile=[IO.Path]::GetFileName($Destination)
    $validation.sourceSha256=$before;$validation.compactSha256=(Get-FileHash $image -Algorithm SHA256).Hash.ToLowerInvariant()
    $validation.roundTripSha256=$before;$validation.roundTripVerified=$true
    $validation.converter=@{name='NKit';version=$tool.Version;platform=$tool.Platform;archiveSha256=$tool.ArchiveSha256;executableSha256=$tool.ExecutableSha256;source=$tool.Source}
    $stagedReport=Join-Path $stage 'nkit-validation.json'
    [IO.File]::WriteAllText($stagedReport,($validation|ConvertTo-Json -Depth 8)+"`n")
    # Publish only after both reversibility and the physical playable layout
    # pass. Move replaces an output link, never writes through it to the source.
    [IO.File]::Move($image,$Destination,$true)
    [IO.File]::Move($stagedReport,$Report,$true)
    Write-Host "Verified NKit image: $Destination ($($validation.compactBytes) bytes, $([Math]::Round($validation.savedPercent,2))% smaller)"
}finally{
    if(Test-Path -LiteralPath $stage){Remove-Item -LiteralPath $stage -Recurse -Force}
}
