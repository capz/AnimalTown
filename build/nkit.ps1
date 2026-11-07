param([string]$CacheRoot='')
$ErrorActionPreference='Stop'
function Get-NKitTool([string]$CacheRoot) {
    $lock=Get-Content (Join-Path $PSScriptRoot 'nkit.lock.json') -Raw|ConvertFrom-Json -AsHashtable
    $os=if($IsWindows){'win'}elseif($IsLinux){'linux'}elseif($IsMacOS){'osx'}else{throw 'NKit packaging requires Windows, Linux or macOS'}
    $rid=$os+'-'+[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $platform=$lock.platforms[$rid];if(!$platform){throw "No pinned NKit binary for $rid"}
    if(!$CacheRoot){$CacheRoot=if($env:ANIMALTOWN_NKIT_CACHE){$env:ANIMALTOWN_NKIT_CACHE}else{Join-Path $PSScriptRoot '../out/tool-cache/nkit'}}
    $cache=[IO.Path]::GetFullPath((Join-Path $CacheRoot "$($lock.version)/$rid"))
    New-Item -ItemType Directory -Force -Path $cache|Out-Null
    $archive=Join-Path $cache $platform.asset
    $url="https://github.com/$($lock.repository)/releases/download/v$($lock.version)/$($platform.asset)"
    if(!(Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash -ne $platform.sha256){
        $download=$archive+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
        try{
            Invoke-WebRequest -Uri $url -OutFile $download
            if((Get-FileHash $download -Algorithm SHA256).Hash -ne $platform.sha256){throw 'NKit archive checksum mismatch'}
            [IO.File]::Move($download,$archive,$true)
        }finally{if(Test-Path $download){Remove-Item -LiteralPath $download}}
    }
    $name=if($IsWindows){'nkit.exe'}else{'nkit'};$executable=Join-Path $cache $name
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try{
        $entries=@($zip.Entries|Where-Object { $_.FullName -ieq $name })
        if($entries.Count -ne 1){throw 'Pinned NKit archive must contain one root executable'}
        $stream=$entries[0].Open();try{$expected=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))}finally{$stream.Dispose()}
        if(!(Test-Path $executable) -or (Get-FileHash $executable -Algorithm SHA256).Hash -ne $expected){
            $temporary=$executable+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
            try{[IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0],$temporary);[IO.File]::Move($temporary,$executable,$true)}finally{if(Test-Path $temporary){Remove-Item -LiteralPath $temporary}}
        }
    }finally{$zip.Dispose()}
    $notice=Join-Path $PSScriptRoot 'NKit.LICENSE';$cachedNotice=Join-Path $cache 'LICENSE-NKit.txt'
    if(!(Test-Path $cachedNotice) -or (Get-FileHash $cachedNotice -Algorithm SHA256).Hash -ne (Get-FileHash $notice -Algorithm SHA256).Hash){Copy-Item -LiteralPath $notice -Destination $cachedNotice -Force}
    if(!$IsWindows){
        $execute=[IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherExecute
        if(([IO.File]::GetUnixFileMode($executable) -band $execute) -ne $execute){& chmod a+x $executable;if($LASTEXITCODE){throw 'Cannot mark NKit executable'}}
    }
    [pscustomobject]@{Path=$executable;Version=$lock.version;Platform=$rid;ArchiveSha256=$platform.sha256;ExecutableSha256=$expected.ToLowerInvariant();Source=$url}
}
if($MyInvocation.InvocationName -ne '.') {
    $tool=Get-NKitTool -CacheRoot $CacheRoot
    & $tool.Path --version
    if($LASTEXITCODE){throw 'Pinned NKit executable cannot run on this platform'}
    $tool|Format-List
}
