# SDK-owned, runtime-independent boot component. Never builds or patches C here.
$ErrorActionPreference='Stop'
$gameRoot=Split-Path $PSScriptRoot -Parent
$pin=Get-Content (Join-Path $gameRoot 'build/apploader.lock.json') -Raw|ConvertFrom-Json
if($pin.schema -ne 1 -or $pin.repository -ne 'capz/DolphinDotNet' -or $pin.commit -cnotmatch '^[0-9a-f]{40}$' -or $pin.tag -cne "apploader-$($pin.commit.Substring(0,12))"){
    throw 'Invalid SDK apploader source identity'
}
$apploaderCommit=$pin.commit
$cache=Join-Path $gameRoot "out/sdk-apploader/$apploaderCommit"
New-Item -ItemType Directory -Force -Path $cache|Out-Null
foreach($name in @('apploader.img','LICENSE')){
    $expected=$pin.files.$name
    if($expected.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $expected.bytes -le 0){throw "Invalid SDK apploader pin: $name"}
    $file=Join-Path $cache $name
    $valid=(Test-Path -LiteralPath $file -PathType Leaf) -and (Get-Item -LiteralPath $file).Length -eq $expected.bytes -and (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $expected.sha256
    if(!$valid){
        $temporary=$file+'.'+[Guid]::NewGuid().ToString('N')+'.tmp'
        try{
            Invoke-WebRequest -Uri "https://github.com/$($pin.repository)/releases/download/$($pin.tag)/$name" -OutFile $temporary
            if((Get-Item -LiteralPath $temporary).Length -ne $expected.bytes -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected.sha256){throw "SDK apploader download identity mismatch: $name"}
            Move-Item -LiteralPath $temporary -Destination $file -Force
        }finally{if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
    }
}
$apploaderContentHash=$pin.files.'apploader.img'.sha256
Copy-Item -LiteralPath (Join-Path $cache 'apploader.img') -Destination (Join-Path $gameRoot 'out/apploader.img') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $gameRoot 'out/disc')|Out-Null
Copy-Item -LiteralPath (Join-Path $cache 'LICENSE') -Destination (Join-Path $gameRoot 'out/disc/sdk-apploader-LICENSE.txt') -Force
