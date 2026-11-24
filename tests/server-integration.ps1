param([string]$DatabaseConnection='')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$serverRoot=(Get-Location).Path
$container='animaltown-integration-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$api='http://127.0.0.1:5189'
$process=$null
$oldTestConnection=$env:ANIMALTOWN_TEST_CONNECTION
$ownsDatabase=!$DatabaseConnection
function Request([string]$Method,[string]$Path,$Body,$Token) {
    $headers=@{}; if($Token){$headers.Authorization="Bearer $Token"}
    $parameters=@{Method=$Method;Uri="$api$Path";Headers=$headers;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$parameters.Body=($Body|ConvertTo-Json -Compress);$parameters.ContentType='application/json'}
    $response=Invoke-WebRequest @parameters
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
    Assert ((Request POST '/v1/auth/token' @{name='LegacySplit';password='test-password-long'} $null).Status -eq 200) 'Migrated login failed'
    dotnet tests/AnimalTown.ServerChecks/bin/Release/net10.0/AnimalTown.ServerChecks.dll verify
    if($LASTEXITCODE){throw 'Backend split storage or migration failed'}
    Write-Host 'Server integration checks passed: authentication, ownership, idempotency, friendship, blocking, permanent purchases, visitor item permissions, custody, deferred rewards, restoration and concurrent local claims.'
}
finally {
    $env:ANIMALTOWN_TEST_CONNECTION=$oldTestConnection
    if($process -and !$process.HasExited){Stop-Process -Id $process.Id}
    if($ownsDatabase){docker stop $container 2>&1 | Out-Null}
}
