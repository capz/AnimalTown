$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$serverRoot=(Get-Location).Path
$container='animaltown-integration-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$api='http://127.0.0.1:5189'
$process=$null
function Request([string]$Method,[string]$Path,$Body,$Token) {
    $headers=@{}; if($Token){$headers.Authorization="Bearer $Token"}
    $parameters=@{Method=$Method;Uri="$api$Path";Headers=$headers;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$parameters.Body=($Body|ConvertTo-Json -Compress);$parameters.ContentType='application/json'}
    $response=Invoke-WebRequest @parameters
    return @{Status=[int]$response.StatusCode;Body=if($response.Content){$response.Content|ConvertFrom-Json}else{$null}}
}
function Assert($Condition,[string]$Message){if(!$Condition){throw $Message}}
try {
    docker run --rm -d --name $container -e POSTGRES_PASSWORD=animaltown-test-only -p 127.0.0.1:55439:5432 postgres:16-alpine | Out-Null
    if($LASTEXITCODE){throw 'Test database failed'}
    $databaseReady=$false
    for($i=0;$i -lt 30;$i++){docker exec $container pg_isready -U postgres 2>&1 | Out-Null;if(!$LASTEXITCODE){$databaseReady=$true;break};Start-Sleep -Milliseconds 300}
    Assert $databaseReady 'Database did not become ready'
    & ./tools/build-server.ps1
    $oldConnection=$env:ConnectionStrings__Game
    $env:ConnectionStrings__Game='Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Password=animaltown-test-only'
    $launch=@{FilePath='dotnet';ArgumentList=@('out/server/AnimalTown.Server.dll','--urls',$api);WorkingDirectory=$serverRoot;PassThru=$true;RedirectStandardOutput="$serverRoot/out/server-test.log";RedirectStandardError="$serverRoot/out/server-test-error.log"}
    if($IsWindows){$launch.WindowStyle='Hidden'}
    $process=Start-Process @launch
    $env:ConnectionStrings__Game=$oldConnection
    $ready=$false
    for($i=0;$i -lt 40;$i++){try{if((Request GET '/health' $null $null).Status -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    Assert $ready 'Server did not become ready'
    Assert ((Request GET '/v1/me' $null $null).Status -eq 401) 'Unauthenticated request accepted'
    $a=(Request POST '/v1/auth/register' @{name='RowanTest';password='test-password-long'} $null).Body
    $b=(Request POST '/v1/auth/register' @{name='MossTest';password='test-password-long'} $null).Body
    Assert ($a.token -and $b.token) 'Registration failed'
    Assert ((Request POST '/v1/auth/token' @{name='RowanTest';password='incorrect-password'} $null).Status -eq 401) 'Bad password accepted'
    Assert ((Request GET "/v1/towns/$($a.id)" $null $b.token).Status -eq 403) 'Foreign save exposed'
    $op=@{id=[Guid]::NewGuid().ToString();kind='sell';slot=0}
    $first=Request POST '/v1/inventory/ops' $op $a.token
    $retry=Request POST '/v1/inventory/ops' $op $a.token
    Assert ($first.Status -eq 200 -and $retry.Body.wallet -eq 50) 'Idempotent operation failed'
    $op.slot=1;Assert ((Request POST '/v1/inventory/ops' $op $a.token).Status -eq 409) 'Reused operation id with different body accepted'
    $before=(Request GET '/v1/me' $null $a.token).Body
    Assert ((Request POST '/v1/inventory/ops' @{id=[Guid]::NewGuid().ToString();kind='withdraw';amount=5000} $a.token).Status -eq 409) 'Overdraw accepted'
    $after=(Request GET '/v1/me' $null $a.token).Body
    Assert ($after.wallet -eq $before.wallet -and $after.revision -eq $before.revision) 'Rejected operation mutated state'
    Assert ((Request POST '/v1/visits' @{host=$a.id} $b.token).Status -eq 403) 'Stranger visit accepted'
    Request POST "/v1/friends/$($b.id)" @{} $a.token | Out-Null
    Request POST "/v1/friends/$($a.id)" @{} $b.token | Out-Null
    Request POST '/v1/gate' @{} $a.token | Out-Null
    $visit=Request POST '/v1/visits' @{host=$a.id} $b.token
    Assert ($visit.Status -eq 200) 'Mutual friend visit reservation failed'
    $listing=[Guid]::NewGuid().ToString()
    Assert ((Request POST '/v1/shops/listings' @{id=$listing;slot=1;price=10} $a.token).Status -eq 200) 'Escrow listing failed'
    Request POST '/v1/inventory/ops' @{id=[Guid]::NewGuid().ToString();kind='sell';slot=0} $b.token | Out-Null
    $purchase=Request POST "/v1/shops/listings/$listing/purchase" @{} $b.token
    Assert ($purchase.Status -eq 200) 'Purchase failed'
    Request POST "/v1/shops/listings/$listing/purchase" @{} $b.token | Out-Null
    $buyerAfter=(Request GET '/v1/me' $null $b.token).Body
    Assert ($buyerAfter.wallet -eq 40) "Purchase retry balance incorrect: $($buyerAfter.wallet)"
    Assert ((Request GET '/v1/me' $null $a.token).Body.savings -eq 10) 'Seller credit failed'
    $grant=(Request GET '/v1/grants' $null $b.token).Body[0]
    Assert ($grant.id) 'Purchase grant missing'
    Request POST "/v1/grants/$($grant.id)/claim" @{} $b.token | Out-Null
    Request POST "/v1/grants/$($grant.id)/claim" @{} $b.token | Out-Null
    Assert ((Request GET '/v1/grants' $null $b.token).Body.Count -eq 0) 'Grant retry duplicated item'
    Request POST "/v1/blocks/$($b.id)" @{} $a.token | Out-Null
    Assert ((Request POST '/v1/visits' @{host=$a.id} $b.token).Status -eq 403) 'Blocked visitor accepted'
    Write-Host 'Server integration checks passed: authentication, ownership, idempotency, rollback, friendship, blocking, escrow, purchase and grants.'
}
finally {
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id}
    docker stop $container 2>&1 | Out-Null
}
