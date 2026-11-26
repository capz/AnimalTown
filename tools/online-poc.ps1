param([ValidateSet('Start','Stop','Status','Backup','Restore')][string]$Action='Start',[switch]$NoTunnel,[switch]$HostRuntime,[switch]$EnablePlay,[string]$Cloudflared='cloudflared',[ValidateRange(1024,65535)][int]$Port=5190,[string]$BackupFile='',[ValidatePattern('^animaltown_restore_[a-z0-9]{1,24}$')][string]$RestoreDatabase='animaltown_restore_check')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $root 'out/online'
$settings=Join-Path $directory 'private.env'
New-Item -ItemType Directory -Path $directory -Force|Out-Null
if(!(Test-Path -LiteralPath $settings)){
    if($Action -ne 'Start'){throw 'Start the PoC before using this action'}
    $secret=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    [IO.File]::WriteAllText($settings,"ONLINE_POC_PASSWORD=$secret`nONLINE_POC_PORT=$Port`nONLINE_POC_DATABASE=animaltown`n")
    if(!$IsWindows){[IO.File]::SetUnixFileMode($settings,[IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)}
}
$compose=@('compose','--env-file',$settings,'-f',(Join-Path $root 'build/online.compose.yml'))
function RunDocker([string[]]$Arguments){& docker @Arguments;if($LASTEXITCODE){throw 'PoC Docker command failed'}}
$configured=@{}
foreach($line in Get-Content -LiteralPath $settings){$pair=$line.Split('=',2);if($pair.Length -eq 2){$configured[$pair[0]]=$pair[1]}}
$databaseName=if($configured.ONLINE_POC_DATABASE){$configured.ONLINE_POC_DATABASE}else{'animaltown'}
if($databaseName -notmatch '^(animaltown|animaltown_restore_[a-z0-9]{1,24})$'){throw 'Invalid PoC database name'}
if($HostRuntime -and $Action -eq 'Start'){
    RunDocker ($compose+@('up','-d','database'))
    & dotnet publish (Join-Path $root 'server/AnimalTown.Server') -c Release -o (Join-Path $directory 'server') --nologo
    if($LASTEXITCODE){throw 'Host backend publish failed'}
    $portNumber=[int]$configured.ONLINE_POC_PORT
    $oldConnection=$env:ConnectionStrings__Game;$oldWebSockets=$env:OnlineGameplay__WebSockets;$oldPlay=$env:OnlineGameplay__EnablePlay
    $tunnel=$null
    try{
        $env:ConnectionStrings__Game='Host=127.0.0.1;Port=55440;Database='+$databaseName+';Username=animaltown;Password='+$configured.ONLINE_POC_PASSWORD
        $env:OnlineGameplay__WebSockets='true'
        $env:OnlineGameplay__EnablePlay=$EnablePlay.IsPresent.ToString().ToLowerInvariant()
        if(!$NoTunnel){
            $connector=Get-Command $Cloudflared -ErrorAction Stop
            $tunnel=Start-Process $connector.Source -ArgumentList @('tunnel','--no-autoupdate','--protocol','http2','--url',"http://127.0.0.1:$portNumber") -PassThru -RedirectStandardOutput (Join-Path $directory 'tunnel.log') -RedirectStandardError (Join-Path $directory 'tunnel-error.log')
            $address=$null
            for($attempt=0;$attempt -lt 40;$attempt++){
                $logs=Get-Content -LiteralPath (Join-Path $directory 'tunnel-error.log') -Raw -ErrorAction SilentlyContinue
                $found=[regex]::Match([string]::Concat($logs),'https://[a-z0-9-]+\.trycloudflare\.com')
                if($found.Success){$address=$found.Value;break}
                if($tunnel.HasExited){throw 'Tunnel exited; inspect out/online/tunnel-error.log'}
                Start-Sleep -Milliseconds 500
            }
            if(!$address){throw 'Tunnel has not supplied an endpoint; inspect out/online/tunnel-error.log'}
            [IO.File]::WriteAllText((Join-Path $directory 'online-origin.txt'),$address+"`n")
            Write-Host "Public HTTPS/WSS endpoint: $address"
        }
        Write-Host 'Host backend runs in this terminal. Ctrl+C stops it; town data is persistent.'
        & dotnet (Join-Path $directory 'server/AnimalTown.Server.dll') --urls "http://127.0.0.1:$portNumber"
        if($LASTEXITCODE){throw 'Host backend exited with an error'}
    }finally{
        if($tunnel -and !$tunnel.HasExited){Stop-Process -Id $tunnel.Id}
        $env:ConnectionStrings__Game=$oldConnection;$env:OnlineGameplay__WebSockets=$oldWebSockets;$env:OnlineGameplay__EnablePlay=$oldPlay
    }
    return
}
switch($Action){
    'Stop'{RunDocker ($compose+@('--profile','internet','stop'));Write-Host 'Stopped. Town data and local credentials are retained.'}
    'Backup'{
        RunDocker ($compose+@('exec','-T','database','pg_dump','-U','animaltown','-d',$databaseName,'-Fc','-f','/tmp/online-backup.dump'))
        $backup=Join-Path $directory ('town-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.dump')
        RunDocker ($compose+@('cp','database:/tmp/online-backup.dump',$backup))
        if(!$IsWindows){[IO.File]::SetUnixFileMode($backup,[IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)}
        Write-Host "Backup: $backup"
    }
    'Restore'{
        if(!$BackupFile -or !(Test-Path -LiteralPath $BackupFile -PathType Leaf)){throw 'Supply an existing custom-format dump with -BackupFile'}
        RunDocker ($compose+@('up','-d','database'))
        RunDocker ($compose+@('exec','-T','database','createdb','-U','animaltown','-T','template0',$RestoreDatabase))
        try{
            RunDocker ($compose+@('cp',(Resolve-Path -LiteralPath $BackupFile).Path,'database:/tmp/online-restore.dump'))
            RunDocker ($compose+@('exec','-T','database','pg_restore','-U','animaltown','-d',$RestoreDatabase,'--no-owner','--no-acl','--exit-on-error','/tmp/online-restore.dump'))
        }finally{& docker ($compose+@('exec','-T','database','rm','-f','/tmp/online-restore.dump'))|Out-Null}
        Write-Host "Restored into new database $RestoreDatabase. The running database has not been replaced."
        Write-Host 'To select the restored data, stop the backend, set ONLINE_POC_DATABASE in out/online/private.env, then start again.'
    }
    'Start'{
        $profile=if($NoTunnel){@()}else{@('--profile','internet')}
        $oldPlay=$env:ONLINE_POC_ENABLE_PLAY
        try{$env:ONLINE_POC_ENABLE_PLAY=$EnablePlay.IsPresent.ToString().ToLowerInvariant();RunDocker ($compose+$profile+@('up','-d','--build'))}finally{$env:ONLINE_POC_ENABLE_PLAY=$oldPlay}
        Write-Host 'Backend binds only to loopback. Internet clients use the authenticated HTTPS/WSS tunnel.'
    }
}
if($Action -in @('Start','Status')){
    RunDocker ($compose+@('--profile','internet','ps'))
    if(!$NoTunnel){
        $address=$null
        for($attempt=0;$attempt -lt 40;$attempt++){
            $logs=& docker ($compose+@('logs','--no-color','--tail','200','tunnel')) 2>&1
            $found=[regex]::Match(($logs -join "`n"),'https://[a-z0-9-]+\.trycloudflare\.com')
            if($found.Success){$address=$found.Value;break}
            if($Action -eq 'Status'){break}
            Start-Sleep -Milliseconds 500
        }
        if($address){
            [IO.File]::WriteAllText((Join-Path $directory 'online-origin.txt'),$address+"`n")
            Write-Host "Public HTTPS/WSS endpoint: $address"
            Write-Host 'A new tunnel hostname requires explicit client reconfiguration and relinking.'
        }elseif($Action -eq 'Start'){throw 'Tunnel has not supplied an endpoint. Check its logs; local town data is retained.'}
    }
}
