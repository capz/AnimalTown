param([string]$DatabaseConnection='',[switch]$PublicTunnel,[switch]$OnlineOnly)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$serverRoot=(Get-Location).Path
$container='animaltown-integration-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$api='http://127.0.0.1:5189'
$process=$null
$tunnelContainer=''
$oldTestConnection=$env:ANIMALTOWN_TEST_CONNECTION
$ownsDatabase=!$DatabaseConnection
function Request([string]$Method,[string]$Path,$Body,$Token) {
    $headers=@{}; if($Token){$headers.Authorization="Bearer $Token"}
    $parameters=@{Method=$Method;Uri="$api$Path";Headers=$headers;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$parameters.Body=($Body|ConvertTo-Json -Compress);$parameters.ContentType='application/json'}
    $response=Invoke-WebRequest @parameters
    return @{Status=[int]$response.StatusCode;Body=if($response.Content){$response.Content|ConvertFrom-Json}else{$null}}
}
function UploadSave([string]$Path,[byte[]]$Bytes,[string]$Token,[string]$Method='POST') {
    $response=Invoke-WebRequest -Method $Method -Uri "$api$Path" -Headers @{Authorization="Bearer $Token"} -Body $Bytes -ContentType 'application/octet-stream' -SkipHttpErrorCheck
    return @{Status=[int]$response.StatusCode;Body=if($response.Content){$response.Content|ConvertFrom-Json}else{$null}}
}
function Assert($Condition,[string]$Message){if(!$Condition){throw $Message}}
try {
    if($ownsDatabase){
    docker run --rm -d --name $container -e POSTGRES_PASSWORD=animaltown-test-only -p 127.0.0.1:55439:5432 postgres:16-alpine | Out-Null
    if($LASTEXITCODE){throw 'Test database failed'}
    $databaseReady=$false
    for($i=0;$i -lt 30;$i++){docker exec $container pg_isready -U postgres 2>&1 | Out-Null;if(!$LASTEXITCODE){$databaseReady=$true;break};Start-Sleep -Milliseconds 300}
    Assert $databaseReady 'Database did not become ready'
    $DatabaseConnection='Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Password=animaltown-test-only'
    }
    & ./tools/build-server.ps1
    $env:ANIMALTOWN_TEST_CONNECTION=$DatabaseConnection
    dotnet build tests/AnimalTown.ServerChecks -c Release -v quiet
    if($LASTEXITCODE){throw 'Server storage checks build failed'}
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll seed-legacy
    if($LASTEXITCODE){throw 'Legacy server fixture failed'}
    $oldConnection=$env:ConnectionStrings__Game
    $env:ConnectionStrings__Game=$DatabaseConnection
    $launch=@{FilePath='dotnet';ArgumentList=@('out/server/AnimalTown.Server.dll','--urls',$api,'--OnlineGameplay:WebSockets=true','--OnlineGameplay:EnablePlay=true');WorkingDirectory=$serverRoot;PassThru=$true;RedirectStandardOutput="$serverRoot/out/server-test.log";RedirectStandardError="$serverRoot/out/server-test-error.log"}
    if($IsWindows){$launch.WindowStyle='Hidden'}
    $process=Start-Process @launch
    $env:ConnectionStrings__Game=$oldConnection
    $ready=$false
    for($i=0;$i -lt 40;$i++){try{if((Request GET '/health' $null $null).Status -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    Assert $ready 'Server did not become ready'
    if($OnlineOnly){
        if($PublicTunnel){throw 'Use the complete fixture for public-route qualification'}
        dotnet run --project tests/AnimalTown.OnlineChecks -c Release -- $api
        if($LASTEXITCODE){throw 'Focused online integration checks failed'}
        Write-Host 'Focused online integration checks passed.'
        return
    }
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
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll seed-shop $a.id $b.id
    if($LASTEXITCODE){throw 'Shop fixture failed'}
    $path="/v1/towns/$($a.id)/shop"
    $shop=(Request GET $path $null $b.token).Body
    Assert ($shop.display.Count -eq 6 -and $shop.consignment.Count -eq 3) 'Small town shop must expose exactly three villager sale slots'
    Assert ($shop.cabinet.Count -eq 10 -and $shop.cabinet[8].item -eq 26 -and $shop.cabinet[-1].item -eq 34) 'Essentials cabinet includes the cherry blossom sapling and real writing paper'
    $locked=@{id=[Guid]::NewGuid().ToString();kind='list';source='consignment';slot=3;revision=$shop.revision;pocket=0;price=1200}
    Assert ((Request POST "$path/ops" $locked $a.token).Status -eq 409) 'Small shop allowed a fourth villager sale listing'
    $sale=@{id=[Guid]::NewGuid().ToString();kind='list';source='consignment';slot=0;revision=$shop.revision;pocket=0;price=1200}
    Assert ((Request POST "$path/ops" $sale $b.token).Status -eq 403) 'Guest stocked a local-villager stand'
    Assert ((Request POST "$path/ops" $sale $a.token).Status -eq 200) 'Local shop listing failed'
    $shop=(Request GET $path $null $b.token).Body
    $buy=@{id=[Guid]::NewGuid().ToString();kind='buy';source='consignment';slot=0;revision=$shop.revision}
    $bought=Request POST "$path/ops" $buy $b.token
    Assert ($bought.Status -eq 200 -and $bought.Body.wallet -eq 8800) 'Guest could not buy from actual town consignment'
    Assert ((Request POST "$path/ops" $buy $b.token).Body.wallet -eq 8800) 'Town shop retry charged twice'
    Assert ((Request GET '/v1/me' $null $a.token).Body.savings -eq 1210) 'Town shop credited the wrong seller'
    $buy.slot=1;Assert ((Request POST "$path/ops" $buy $b.token).Status -eq 409) 'Shop idempotency key reused for a different stand'
    $shop=(Request GET $path $null $b.token).Body
    # Two funded customers race for the same finite physical display.
    $http=[System.Net.Http.HttpClient]::new()
    try {
        $pending=@()
        foreach($actor in @($a,$b)){
            $body=@{id=[Guid]::NewGuid().ToString();kind='buy';source='display';slot=0;revision=$shop.revision}|ConvertTo-Json -Compress
            $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,"$api$path/ops")
            $request.Headers.Authorization=[System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$actor.token)
            $request.Content=[System.Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
            $pending+=@{Request=$request;Task=$http.SendAsync($request)}
        }
        $codes=@($pending|ForEach-Object{[int]$_.Task.GetAwaiter().GetResult().StatusCode}|Sort-Object)
        Assert ($codes[0] -eq 200 -and $codes[1] -eq 409) 'Concurrent display purchases did not have exactly one winner'
        foreach($entry in $pending){$entry.Task.Result.Dispose();$entry.Request.Dispose()}
    }finally{$http.Dispose()}
    $shop=(Request GET $path $null $b.token).Body
    Assert ($shop.display[0].item -eq 0) 'Sold display remained stocked'
    $before=(Request GET '/v1/me' $null $b.token).Body
    foreach($attempt in 1..2){
        $cabinet=@{id=[Guid]::NewGuid().ToString();kind='buy';source='cabinet';slot=1;revision=$shop.revision}
        Assert ((Request POST "$path/ops" $cabinet $b.token).Status -eq 200) 'Unlimited cabinet exhausted'
    }
    $after=(Request GET '/v1/me' $null $b.token).Body
    Assert ($after.wallet -eq $before.wallet-160 -and @($after.pockets|Where-Object{$_ -eq 2}).Count -eq 2) 'Cabinet repeated purchase did not deliver and charge exactly twice'
    Assert ((Request POST "$path/ops" @{id=[Guid]::NewGuid().ToString();kind='buy';source='cabinet';slot=10;revision=$shop.revision} $b.token).Status -eq 409) 'Cabinet sold nonessential goods'

    # Start a new service lifetime with the same durable database and tokens.
    # This also keeps the growing suites within the production per-IP rate budget.
    Stop-Process -Id $process.Id;$process.WaitForExit()
    $env:ConnectionStrings__Game=$DatabaseConnection
    try{$process=Start-Process @launch}finally{$env:ConnectionStrings__Game=$oldConnection}
    $ready=$false
    for($i=0;$i -lt 40;$i++){try{if((Request GET '/health' $null $null).Status -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    Assert $ready 'Server did not restart for custody checks'
    $persisted=(Request GET '/v1/me' $null $b.token).Body
    Assert ($persisted.wallet -eq $after.wallet -and ($persisted.pockets -join ',') -eq ($after.pockets -join ',')) 'Permanent shop purchases did not survive restart'
    Assert ((Request GET $path $null $b.token).Body.display[0].item -eq 0) 'Restart restored purchased shop stock'

    $seed=dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll seed-koban $a.id $b.id
    if($LASTEXITCODE){throw 'Koban fixture failed'}
    $kobanSeed=$seed|ConvertFrom-Json
    $itemPath="/v1/towns/$($a.id)/items"
    $lostPath="/v1/towns/$($a.id)/lost-found"
    $items=(Request GET $itemPath $null $b.token).Body
    Assert ($null -ne $items.revision) 'Authorized visitor cannot inspect editable town items'
    Assert ((Request GET $lostPath $null $b.token).Status -eq 403) 'Visitor could inspect private lost-and-found inventory'
    $lost=(Request GET $lostPath $null $a.token).Body
    Assert ($lost.resident -eq 1 -and $lost.entries.Count -ge 1) 'Custody did not bind to the active local human resident'
    $spoof=@{id=[Guid]::NewGuid().ToString();kind='claim';source='lost-found';revision=$lost.revision;index=$lost.entries[0].index;actor=1;isResident=$true}
    Assert ((Request POST "$itemPath/ops" $spoof $b.token).Status -eq 403) 'Visitor spoofed resident claim authorization'
    Assert ((Request POST "$itemPath/ops" @{id=[Guid]::NewGuid().ToString();kind='pickup';source='ground';revision=$items.revision;index=0} $b.token).Status -eq 400) 'Visitor pickup acquisition was exposed'
    $guestBefore=(Request GET '/v1/me' $null $b.token).Body
    $add=@{id=[Guid]::NewGuid().ToString();kind='add';source='ground';revision=$items.revision;playerRevision=$guestBefore.revision;pocket=$kobanSeed.pocket;x=$kobanSeed.x;z=$kobanSeed.z;wrapped=$true}
    $added=Request POST "$itemPath/ops" $add $b.token
    Assert ($added.Status -eq 200) 'Authorized visitor could not add their own item'
    Assert ((Request POST "$itemPath/ops" $add $b.token).Body.revision -eq $added.Body.revision) 'Retried addition mutated town twice'
    $conflict=$add.Clone();$conflict.x=$kobanSeed.moveX;$conflict.z=$kobanSeed.moveZ
    Assert ((Request POST "$itemPath/ops" $conflict $b.token).Status -eq 409) 'Item operation id was reused for different content'
    $stalePocket=$conflict.Clone();$stalePocket.id=[Guid]::NewGuid().ToString();$stalePocket.revision=$added.Body.revision
    Assert ((Request POST "$itemPath/ops" $stalePocket $b.token).Status -eq 409) 'Stale visitor pocket revision was accepted'
    $items=(Request GET $itemPath $null $b.token).Body
    $target=@($items.ground|Where-Object{$_.x -eq $kobanSeed.x -and $_.z -eq $kobanSeed.z})
    Assert ($target.Count -eq 1 -and $target[0].item -eq 12 -and $target[0].wrapped) 'Visitor addition lost item identity or wrapping'
    $move=@{id=[Guid]::NewGuid().ToString();kind='move';source='ground';revision=$items.revision;index=$target[0].index;x=$kobanSeed.moveX;z=$kobanSeed.moveZ}
    $moved=Request POST "$itemPath/ops" $move $b.token
    Assert ($moved.Status -eq 200) 'Visitor could not modify a town item'
    $remove=@{id=[Guid]::NewGuid().ToString();kind='remove';source='ground';revision=$items.revision;index=$target[0].index}
    Assert ((Request POST "$itemPath/ops" $remove $b.token).Status -eq 409) 'Stale town index revision was accepted'
    $remove.revision=$moved.Body.revision
    $removed=Request POST "$itemPath/ops" $remove $b.token
    Assert ($removed.Status -eq 200) 'Visitor removal did not enter custody'
    Assert ((Request POST "$itemPath/ops" $remove $b.token).Body.revision -eq $removed.Body.revision) 'Removal retry duplicated custody'
    $lost=(Request GET $lostPath $null $a.token).Body
    $recovery=@($lost.entries|Where-Object{$_.item -eq 12 -and $_.recoveryId -gt 0})
    Assert ($recovery.Count -eq 1 -and $recovery[0].wrapped) 'Removal lost its unique recovery record or wrapping'
    $restore=@{id=[Guid]::NewGuid().ToString();kind='restore';source='lost-found';revision=$lost.revision;recoveryId=$recovery[0].recoveryId}
    Assert ((Request POST "$itemPath/ops" $restore $b.token).Status -eq 403) 'Visitor restored protected recovery'
    $restored=Request POST "$itemPath/ops" $restore $a.token
    Assert ($restored.Status -eq 200) 'Local resident could not restore a visitor removal'
    Assert ((Request POST "$itemPath/ops" $restore $a.token).Body.revision -eq $restored.Body.revision) 'Restore retry duplicated the original world item'
    $items=(Request GET $itemPath $null $b.token).Body
    $target=@($items.ground|Where-Object{$_.x -eq $kobanSeed.moveX -and $_.z -eq $kobanSeed.moveZ})
    Assert ($target.Count -eq 1 -and $target[0].item -eq 12 -and $target[0].wrapped) 'Restoration did not recover the exact moved item'
    $remove=@{id=[Guid]::NewGuid().ToString();kind='remove';source='ground';revision=$items.revision;index=$target[0].index}
    Assert ((Request POST "$itemPath/ops" $remove $b.token).Status -eq 200) 'Restored item could not return to custody'
    $lost=(Request GET $lostPath $null $a.token).Body
    $recovery=@($lost.entries|Where-Object{$_.item -eq 12 -and $_.recoveryId -gt 0})[0]
    $guestAfter=(Request GET '/v1/me' $null $b.token).Body
    $expectedPockets=@($guestBefore.pockets);$expectedPockets[$kobanSeed.pocket]=0
    Assert ($guestAfter.wallet -eq $guestBefore.wallet -and ($guestAfter.pockets -join ',') -eq ($expectedPockets -join ',')) 'Visitor acquired a removed item or lost a permanent purchase'

    $http=[System.Net.Http.HttpClient]::new()
    try{
        $pending=@()
        foreach($attempt in 1..2){
            $claim=@{id=[Guid]::NewGuid().ToString();kind='claim';source='lost-found';revision=$lost.revision;index=$recovery.index;actor=0}
            $request=[System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post,"$api$itemPath/ops")
            $request.Headers.Authorization=[System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$a.token)
            $request.Content=[System.Net.Http.StringContent]::new(($claim|ConvertTo-Json -Compress),[Text.Encoding]::UTF8,'application/json')
            $pending+=@{Request=$request;Task=$http.SendAsync($request);Claim=$claim}
        }
        $codes=@($pending|ForEach-Object{[int]$_.Task.GetAwaiter().GetResult().StatusCode}|Sort-Object)
        Assert ($codes[0] -eq 200 -and $codes[1] -eq 409) 'Concurrent custody claims did not have exactly one winner'
        $winner=@($pending|Where-Object{[int]$_.Task.Result.StatusCode -eq 200})[0].Claim
        Assert ((Request POST "$itemPath/ops" $winner $a.token).Status -eq 200) 'Successful custody claim could not replay its receipt'
        foreach($entry in $pending){$entry.Task.Result.Dispose();$entry.Request.Dispose()}
    }finally{$http.Dispose()}
    $local=(Request GET '/v1/me' $null $a.token).Body
    Assert ($local.name -eq 'LocalKoban' -and @($local.pockets|Where-Object{$_ -eq 12}).Count -eq 1) 'Claim credited an actor integer instead of the active local resident'
    $lost=(Request GET $lostPath $null $a.token).Body
    $restore.id=[Guid]::NewGuid().ToString();$restore.revision=$lost.revision;$restore.recoveryId=$recovery.recoveryId
    Assert ((Request POST "$itemPath/ops" $restore $a.token).Status -eq 409) 'Claimed property was restored a second time'
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll seed-deferred-custody $a.id
    if($LASTEXITCODE){throw 'Deferred custody fixture failed'}
    $lost=(Request GET $lostPath $null $a.token).Body
    $deferred=@($lost.entries|Where-Object{$_.index -ge 512})
    Assert ($deferred.Count -eq 1 -and $deferred[0].item -eq 13 -and $deferred[0].recoveryId -eq 0 -and !$deferred[0].wrapped) 'Durable deferred custody was missing or exposed as a removable world item'
    $deferredClaim=@{id=[Guid]::NewGuid().ToString();kind='claim';source='lost-found';revision=$lost.revision;index=$deferred[0].index}
    Assert ((Request POST "$itemPath/ops" $deferredClaim $b.token).Status -eq 403) 'Visitor claimed a deferred town reward'
    $claimed=Request POST "$itemPath/ops" $deferredClaim $a.token
    Assert ($claimed.Status -eq 200 -and $claimed.Body.lostCount -eq ($lost.entries.Count-1)) 'Local resident could not claim deferred custody'
    Assert ((Request POST "$itemPath/ops" $deferredClaim $a.token).Body.revision -eq $claimed.Body.revision) 'Deferred custody claim retry applied twice'
    $local=(Request GET '/v1/me' $null $a.token).Body
    Assert (@($local.pockets|Where-Object{$_ -eq 13}).Count -eq 1) 'Deferred custody did not reach the active local pockets exactly once'
    $lost=(Request GET $lostPath $null $a.token).Body
    Assert (@($lost.entries|Where-Object{$_.index -ge 512}).Count -eq 0) 'Claimed deferred custody remained available'
    $deferredClaim.id=[Guid]::NewGuid().ToString();$deferredClaim.revision=$lost.revision
    Assert ((Request POST "$itemPath/ops" $deferredClaim $a.token).Status -eq 409) 'A fresh operation claimed deferred custody twice'
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll fill-koban-pockets $a.id
    if($LASTEXITCODE){throw 'Full custody pockets fixture failed'}
    $lost=(Request GET $lostPath $null $a.token).Body
    $full=@{id=[Guid]::NewGuid().ToString();kind='claim';source='lost-found';revision=$lost.revision;index=$lost.entries[0].index}
    Assert ((Request POST "$itemPath/ops" $full $a.token).Status -eq 409) 'Full resident pockets accepted a custody claim'
    $unchanged=(Request GET $lostPath $null $a.token).Body
    Assert ($unchanged.revision -eq $lost.revision -and $unchanged.entries.Count -eq $lost.entries.Count) 'Rejected custody claim changed durable state'
    Request POST "/v1/blocks/$($b.id)" @{} $a.token | Out-Null
    Assert ((Request GET $path $null $b.token).Status -eq 403) 'Blocked guest accessed town shop'
    Assert ((Request GET $itemPath $null $b.token).Status -eq 403) 'Blocked guest accessed town items'
    $remove.id=[Guid]::NewGuid().ToString();$remove.revision=$unchanged.revision
    Assert ((Request POST "$itemPath/ops" $remove $b.token).Status -eq 403) 'Blocked guest mutated town items'
    Assert ((Request POST '/v1/visits' @{host=$a.id} $b.token).Status -eq 403) 'Blocked visitor accepted'
    # Hosted placement review is a staging-only surface. Start a fresh
    # process to enable it independently and reset the per-IP test budget.
    Stop-Process -Id $process.Id;$process.WaitForExit()
    $env:ConnectionStrings__Game=$DatabaseConnection
    $oldPlacements=$env:Multiplayer__EnablePlacements
    $env:Multiplayer__EnablePlacements='true'
    try{$process=Start-Process @launch}finally{
        $env:ConnectionStrings__Game=$oldConnection
        $env:Multiplayer__EnablePlacements=$oldPlacements
    }
    $ready=$false
    for($i=0;$i -lt 40;$i++){try{if((Request GET '/health' $null $null).Status -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    Assert $ready 'Server did not restart for hosted review checks'
    $card=(Request POST '/v1/auth/register' @{name='CardTownTest';password='test-password-long'} $null).Body
    $third=(Request POST '/v1/auth/register' @{name='ThirdGuestTest';password='test-password-long'} $null).Body
    $pair=(Request POST '/v1/auth/link/start' @{town='435244544F574E01'} $null).Body
    Assert ($pair.code -cmatch '^[0-9]{8}$' -and $pair.secret.Length -eq 64) 'Pairing response did not separate user code from device proof'
    Assert ((Invoke-WebRequest -Uri "$api$($pair.verificationPath)" -SkipHttpErrorCheck).StatusCode -eq 200) 'Pairing web page unavailable'
    Assert ((Request POST '/v1/auth/link/poll' @{secret=$pair.secret} $null).Body.status -eq 'pending') 'Unapproved device received a session'
    Assert ((Request POST '/v1/account/link' @{code=$pair.code} $null).Status -eq 401) 'Anonymous pairing approval accepted'
    Assert ((Request POST '/v1/account/link' @{code=$pair.code} $card.token).Status -eq 200) 'Account could not approve pairing'
    Assert ((Request POST '/v1/account/link' @{code=$pair.code} $third.token).Status -eq 404) 'Another account took over an approved code'
    $linked=(Request POST '/v1/auth/link/poll' @{secret=$pair.secret} $null).Body
    Assert ($linked.id -eq $card.id -and $linked.town -eq '435244544F574E01' -and $linked.generation -eq 1) 'Pairing returned the wrong account or town'
    Assert ($linked.deviceId -and $linked.token.StartsWith('d2_') -and $linked.refreshToken.StartsWith('r2_')) 'Pairing did not issue separate device credentials'
    Assert ((Request POST '/v1/auth/link/poll' @{secret=$pair.secret} $null).Body.token -eq $linked.token) 'Pairing retry minted another token'
    Assert ((Request GET '/v1/me' $null $linked.token).Body.id -eq $card.id) 'Device token could not authenticate'
    Assert ((Request GET '/v1/account/devices' $null $linked.token).Status -eq 401) 'Device gained browser account privileges'
    Assert ((Request GET '/v1/account/devices' $null $card.token).Body.device.id -eq $linked.deviceId) 'Browser could not identify its linked device'
    $rotation=@{deviceId=$linked.deviceId;refreshToken=$linked.refreshToken;rotationId=[Guid]::NewGuid().ToString()}
    $renewed=(Request POST '/v1/auth/device/renew' $rotation $null).Body
    Assert ($renewed.deviceId -eq $linked.deviceId -and $renewed.generation -eq 2 -and $renewed.token -ne $linked.token -and $renewed.refreshToken -ne $linked.refreshToken) 'Rotation did not replace credentials'
    $rotationRetry=(Request POST '/v1/auth/device/renew' $rotation $null).Body
    Assert ($rotationRetry.token -eq $renewed.token -and $rotationRetry.refreshToken -eq $renewed.refreshToken) 'Rotation retry minted another credential'
    $reuse=$rotation.Clone();$reuse.rotationId=[Guid]::NewGuid().ToString()
    Assert ((Request POST '/v1/auth/device/renew' $reuse $null).Status -eq 401) 'Superseded refresh token could start a new rotation'
    Assert ((Request GET '/v1/me' $null $linked.token).Status -eq 401) 'Superseded access token could still authenticate'
    Assert ((Request POST '/v1/auth/link/poll' @{secret=$pair.secret} $null).Status -eq 404) 'Original pairing proof returned rotated credentials'
    $linked=$renewed
    $fixture=Join-Path $serverRoot 'out/initial-town-fixture.bin'
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll write-initial-save $fixture
    if($LASTEXITCODE){throw 'Initial-save fixture failed'}
    try {
        [byte[]]$bytes=[IO.File]::ReadAllBytes($fixture)
        $importPath="/v1/towns/$($card.id)/initial-save"
        [byte[]]$truncated=$bytes[0..($bytes.Length-2)]
        Assert ((UploadSave $importPath $truncated $card.token).Status -eq 400) 'Truncated initial save accepted'
        Assert ((UploadSave $importPath $bytes $third.token).Status -eq 403) 'Wrong account imported a town'
        $uploads="/v1/towns/$($card.id)/checkpoint-uploads"
        $offer=@{length=$bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))}
        Assert ((Request POST $uploads $offer $card.token).Status -eq 401) 'Account token could stage a device checkpoint'
        $staged=Request POST $uploads $offer $linked.token
        Assert ($staged.Status -eq 200 -and $staged.Body.offset -eq 0) 'Device could not offer checkpoint'
        $uploadPath="$uploads/$($staged.Body.id)"
        $stageBytes=16*1024
        Assert ($bytes.Length -gt $stageBytes) 'Checkpoint fixture does not cover a resume boundary'
        [byte[]]$firstChunk=$bytes[0..($stageBytes-1)]
        $partial=UploadSave "${uploadPath}?offset=0" $firstChunk $linked.token PUT
        Assert ($partial.Status -eq 400 -and $partial.Body.error -eq 'checkpoint-incomplete') 'Short body finalized a checkpoint'
        Assert ((Request GET $uploadPath $null $linked.token).Body.offset -eq $stageBytes) 'Status lost committed resume offset'
        [byte[]]$remainder=$bytes[$stageBytes..($bytes.Length-1)]
        Assert ((UploadSave "${uploadPath}?offset=0" $remainder $linked.token PUT).Status -eq 409) 'Stale offset overwrote staged content'
        $committed=UploadSave "${uploadPath}?offset=$stageBytes" $remainder $linked.token PUT
        Assert ($committed.Status -eq 200 -and $committed.Body.completed -and $committed.Body.revision -eq 1) "Checkpoint resume failed: $($committed.Body.error)"
        $replayed=Request GET $uploadPath $null $linked.token
        Assert ($replayed.Body.completed -and $replayed.Body.offset -eq $bytes.Length -and $replayed.Body.revision -eq 1) 'Completed upload receipt was lost'
        $import=UploadSave $importPath $bytes $card.token
        Assert ($import.Status -eq 200 -and $import.Body.revision -eq 1) "Valid card checkpoint rejected: $($import.Body.error)"
        Assert ((UploadSave $importPath $bytes $card.token).Body.revision -eq 1) 'Initial save retry changed its checkpoint'
        Assert ((UploadSave "/v1/towns/$($third.id)/initial-save" $bytes $third.token).Status -eq 409) 'Same town uploaded to two accounts'
        $imported=(Request GET '/v1/me' $null $linked.token).Body
        Assert ($imported.wallet -eq 4356 -and $imported.savings -eq 9021 -and $imported.revision -eq 1) 'Initial save failed to preserve local balances'
        dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll write-initial-save $fixture 12 4876
        if($LASTEXITCODE){throw 'Offline checkpoint fixture failed'}
        [byte[]]$offline=[IO.File]::ReadAllBytes($fixture)
        $offlineOffer=@{parentRevision=1;length=$offline.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($offline))}
        $staged=Request POST $uploads $offlineOffer $linked.token
        Assert ($staged.Status -eq 200 -and $staged.Body.offset -eq 0) 'Could not stage progress after the first checkpoint'
        Request POST '/v1/gate' @{} $card.token | Out-Null
        $merged=UploadSave "$uploads/$($staged.Body.id)?offset=0" $offline $linked.token PUT
        Assert ($merged.Status -eq 200 -and $merged.Body.revision -eq 2) 'Unattended hosting prevented compatible offline progress'
        Request DELETE '/v1/gate' $null $card.token | Out-Null
        $staged=Request POST $uploads $offlineOffer $linked.token
        $synced=UploadSave "$uploads/$($staged.Body.id)?offset=0" $offline $linked.token PUT
        Assert ($synced.Status -eq 200 -and $synced.Body.completed -and $synced.Body.revision -eq 2) "Offline progress with an unchanged hosted parent failed: $($synced.Body.error)"
        Assert ((Request POST $uploads $offlineOffer $linked.token).Body.revision -eq 2) 'Offline upload retry created a different revision'
        $saved=(Request GET '/v1/me' $null $linked.token).Body
        Assert ($saved.wallet -eq 4876 -and $saved.savings -eq 9021 -and $saved.revision -eq 2) 'Offline progress did not preserve local gains'
    } finally {
        Remove-Item -LiteralPath $fixture -ErrorAction SilentlyContinue
    }
    foreach($guest in @($a,$b,$third)){
        Request POST "/v1/friends/$($guest.id)" @{} $card.token | Out-Null
        Request POST "/v1/friends/$($card.id)" @{} $guest.token | Out-Null
    }
    Assert ((Request POST '/v1/gate' @{} $card.token).Body.open -eq $true) 'Open gate did not report open'
    $seat1=(Request POST '/v1/visits' @{host=$card.id} $a.token).Body
    $seat2=(Request POST '/v1/visits' @{host=$card.id} $b.token).Body
    Assert ($seat1.id -and $seat2.id) 'Two guests could not reserve seats'
    Assert ((Request POST '/v1/visits' @{host=$card.id} $a.token).Body.id -eq $seat1.id) 'Visit retry allocated another seat'
    Assert ((Request POST '/v1/visits' @{host=$card.id} $third.token).Status -eq 409) 'Town accepted a third guest'
    $heartbeat="/v1/visits/$($seat1.id)/heartbeat"
    Assert ((Request POST $heartbeat @{} $a.token).Body.reconnectSeconds -eq 60) 'Guest reconnect grace did not begin'
    Assert ((Request POST $heartbeat @{} $third.token).Status -eq 404) 'Unrelated guest renewed the seat'
    Assert ((Request POST '/v1/visits' @{host=$card.id} $third.token).Status -eq 409) 'Reconnect seat did not count toward capacity'
    Assert ((Request POST $heartbeat @{} $a.token).Status -eq 200) 'Returning guest could not renew the same seat'
    $unsafeOp=@{id=[Guid]::NewGuid().ToString();kind='remove';source='ground';revision=1;index=0}
    Assert ((Request GET "/v1/towns/$($card.id)/items" $null $a.token).Body.error -eq 'live-town-view-required') 'Legacy snapshot exposed a card-managed town to a guest'
    Assert ((Request POST "/v1/towns/$($card.id)/items/ops" $unsafeOp $a.token).Body.error -eq 'online-review-required') 'Legacy removal could target an imported town'
    Assert ((Request POST "/v1/visits/$($seat1.id)/end" @{} $card.token).Status -eq 200) 'Host could not send a guest home'
    Request POST '/v1/gate' @{} $card.token | Out-Null
    Assert ((Request POST '/v1/visits' @{host=$card.id} $a.token).Status -eq 403) 'Removed guest rejoined without a gate reset'
    Assert ((Request POST '/v1/visits' @{host=$card.id} $third.token).Status -eq 200) 'Removing one guest did not free a seat'
    Assert ((Request DELETE '/v1/gate' $null $card.token).Body.open -eq $false) 'Closing the gate did not report closed'
    Assert ((Request POST "/v1/visits/$($seat2.id)/heartbeat" @{} $b.token).Status -eq 403) 'Guest retained access after the gate closed'
    Request POST '/v1/gate' @{} $card.token | Out-Null
    Assert ((Request POST '/v1/visits' @{host=$card.id} $a.token).Status -eq 200) 'Closing and reopening did not clear removals'
    $reviewSeed=dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll seed-placement-review $card.id $a.id
    if($LASTEXITCODE){throw 'Placement review fixture failed'}
    $reviewSeed=$reviewSeed|ConvertFrom-Json
    Assert ((Request POST '/v1/review' @{} $card.token).Status -eq 401) 'Browser account could open a gameplay review'
    $review=(Request POST '/v1/review' @{} $linked.token).Body
    Assert ($review.lease -and $review.expiresIn -eq 10) 'Device could not acquire a bounded review lease'
    $reviewPath="/v1/review/$($review.lease)"
    $page=(Request GET "$reviewPath/placements" $null $linked.token).Body
    Assert ($page.entries.Count -eq 2) 'Review lost pending placements'
    $decision=@{id=[Guid]::NewGuid().ToString();placement=$reviewSeed.keep;version=2;kind='keep'}
    Assert ((Request POST "$reviewPath/placements/ops" $decision $linked.token).Body.error -eq 'placement-changed') 'Review accepted a stale placement version'
    $decision.version=3
    $kept=Request POST "$reviewPath/placements/ops" $decision $linked.token
    Assert ($kept.Status -eq 200 -and $kept.Body.decision -eq 'keep') 'Could not keep the selected placement'
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll expire-placement-review $card.id
    if($LASTEXITCODE){throw 'Could not expire review lease'}
    Assert ((Request POST "$reviewPath/heartbeat" @{} $linked.token).Body.error -eq 'review-lease-ended') 'Heartbeat renewed an expired review lease'
    Assert ((Request POST "$reviewPath/placements/ops" $decision $linked.token).Body.revision -eq $kept.Body.revision) 'Review receipt was lost after interruption'
    $review=(Request POST '/v1/review' @{} $linked.token).Body
    $reviewPath="/v1/review/$($review.lease)"
    $page=(Request GET "$reviewPath/placements" $null $linked.token).Body
    Assert ($page.entries.Count -eq 1 -and $page.entries[0].id -eq $reviewSeed.koban) 'Resumed review lost or reintroduced a placement'
    $decision=@{id=[Guid]::NewGuid().ToString();placement=$reviewSeed.koban;version=1;kind='koban'}
    Assert ((Request POST "$reviewPath/placements/ops" $decision $linked.token).Body.decision -eq 'koban') 'Could not transfer reviewed placement to the koban'
    Assert ((Request DELETE $reviewPath $null $linked.token).Status -eq 200) 'Could not release review pause'
    Assert ((Request POST '/v1/review' @{} $linked.token).Body.error -eq 'review-empty') 'An empty review could pause visitors'
    Assert ((Request POST '/v1/inventory/ops' @{id=[Guid]::NewGuid().ToString();kind='sell';slot=0} $card.token).Body.error -eq 'card-settlement-required') 'Legacy inventory route mutated a card-managed town'
    $items=(Request GET "/v1/towns/$($card.id)/items" $null $card.token).Body
    Assert ($items.placed.Count -eq 1 -and $items.placed[0].item -eq 10) 'Review did not persist exactly one kept placement'
    $custody=(Request GET "/v1/towns/$($card.id)/lost-found" $null $card.token).Body
    Assert (@($custody.entries|Where-Object item -eq 11).Count -eq 1) 'Koban review lost or duplicated an item'
    Assert ((Request POST '/v1/auth/token' @{name='LegacySplit';password='test-password-long'} $null).Status -eq 200) 'Migrated login failed'
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll verify
    if($LASTEXITCODE){throw 'Backend split storage or migration failed'}
    # Keep the public per-address abuse limits enabled. A fresh service process
    # gives the independent three-player fixture its own request budget.
    Stop-Process -Id $process.Id
    $env:ConnectionStrings__Game=$DatabaseConnection
    try{$process=Start-Process @launch}finally{$env:ConnectionStrings__Game=$oldConnection}
    $ready=$false
    for($i=0;$i -lt 40;$i++){try{if((Request GET '/health' $null $null).Status -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
    Assert $ready 'Online fixture server did not become ready'
    dotnet run --project tests/AnimalTown.OnlineChecks -c Release -- $api
    if($LASTEXITCODE){throw 'Online framing and three-client integration checks failed'}
    if($PublicTunnel){
        Stop-Process -Id $process.Id
        $env:ConnectionStrings__Game=$DatabaseConnection
        try{$process=Start-Process @launch}finally{$env:ConnectionStrings__Game=$oldConnection}
        $ready=$false
        for($i=0;$i -lt 40;$i++){try{if((Request GET '/health' $null $null).Status -eq 200){$ready=$true;break}}catch{};Start-Sleep -Milliseconds 250}
        Assert $ready 'Public-route fixture server did not become ready'
        $tunnelContainer='animaltown-public-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
        docker run --rm -d --name $tunnelContainer --network host cloudflare/cloudflared@sha256:55c7e3e8d89d5e8d20b75a82f3602cbf98e06db406e73e282073cfdc5a9a3af6 tunnel --no-autoupdate --protocol http2 --url $api|Out-Null
        if($LASTEXITCODE){throw 'Public test tunnel did not start'}
        $origin=$null
        for($i=0;$i -lt 60;$i++){
            $logs=(& docker logs $tunnelContainer 2>&1)-join "`n"
            $found=[regex]::Match($logs,'https://(?!api\.)[a-z0-9-]+\.trycloudflare\.com')
            if($found.Success){$origin=$found.Value;break}
            Start-Sleep -Milliseconds 500
        }
        Assert $origin 'Public test tunnel did not provide an HTTPS origin'
        # Give the edge a bounded readiness window before account/device traffic.
        $ready=$false
        $lastStatus=0;$lastFailure=''
        for($i=0;$i -lt 60;$i++){
            try{$response=Invoke-WebRequest "$origin/health" -TimeoutSec 5 -SkipHttpErrorCheck;$lastStatus=[int]$response.StatusCode;if($lastStatus -eq 200){$ready=$true;break}}
            catch{$lastFailure=$_.Exception.GetType().Name}
            Start-Sleep -Seconds 1
        }
        if(!$ready){Write-Host "Public origin readiness: HTTP $lastStatus; failure type $lastFailure"}
        Assert $ready 'Public test origin did not become ready'
        dotnet run --project tests/AnimalTown.OnlineChecks -c Release -- $origin
        if($LASTEXITCODE){throw 'Public HTTPS/WSS qualification failed'}
        Write-Host 'Public Cloudflare HTTPS/WSS backend qualification passed; this is not GameCube or DOL-015 qualification.'
    }
    Write-Host 'Server integration checks passed: authentication, device linking, initial card import, gate admission and reconnect, idempotency, permanent purchases, visitor custody, hosted placement review, and three-client WebSockets.'
}
finally {
    $env:ANIMALTOWN_TEST_CONNECTION=$oldTestConnection
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id}
    if($tunnelContainer){
        [IO.File]::WriteAllText((Join-Path $serverRoot 'out/online-tunnel-test.log'),((& docker logs $tunnelContainer 2>&1)-join "`n"))
        docker stop $tunnelContainer 2>&1|Out-Null
    }
    if($ownsDatabase){docker stop $container 2>&1 | Out-Null}
}
