function Write-UpdateFeed {
    param([string]$Iso,[string]$Version,[string]$Repository,[string]$Destination)
    if($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:\+[0-9a-f]{12})?$'){throw 'Invalid update version'}
    if($Repository -cnotmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid update repository'}
    $name=[IO.Path]::GetFileName($Iso)
    if($name -cne "AnimalTown-$Version.nkit.iso"){throw 'Update ISO name must match version'}
    $size=(Get-Item -LiteralPath $Iso).Length
    if($size -lt 1280 -or $size -gt 1459978240){throw 'Invalid update ISO size'}
    $tag=[Uri]::EscapeDataString("v$Version");$asset=[Uri]::EscapeDataString($name)
    $entry=[ordered]@{tag_name="v$Version";draft=$false;assets=@([ordered]@{
        name=$name;browser_download_url="https://github.com/$Repository/releases/download/$tag/$asset"
        size=$size;digest='sha256:'+(Get-FileHash -LiteralPath $Iso -Algorithm SHA256).Hash.ToLowerInvariant()
    })}
    $json=($entry|ConvertTo-Json -Depth 5 -Compress)+"`n"
    if([Text.Encoding]::UTF8.GetByteCount($json) -gt 2048){throw 'Update feed exceeds 2 KiB budget'}
    [IO.File]::WriteAllText($Destination,$json,[Text.UTF8Encoding]::new($false))
}
