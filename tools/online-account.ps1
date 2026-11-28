param(
    [Parameter(Mandatory)][uri]$Origin,
    [Parameter(Mandatory)][ValidateLength(1,16)][string]$Name,
    [ValidateSet('Register','Friends','AddFriend','RemoveFriend','Block','Unblock','Devices','RevokeDevice')][string]$Action='Friends',
    [Guid]$Target=[Guid]::Empty,
    [Security.SecureString]$Password
)
$ErrorActionPreference='Stop'
if(!$Origin.IsAbsoluteUri -or $Origin.UserInfo -or $Origin.AbsolutePath -ne '/' -or $Origin.Query -or $Origin.Fragment -or
   ($Origin.Scheme -ne 'https' -and !($Origin.Scheme -eq 'http' -and $Origin.IsLoopback))){throw 'Use the HTTPS service origin, or HTTP loopback for local checks'}
if($Action -in @('AddFriend','RemoveFriend','Block','Unblock','RevokeDevice') -and $Target -eq [Guid]::Empty){throw 'Supply the other account or device ID with -Target'}
if(!$Password){$Password=Read-Host 'Account password (never saved by this tool)' -AsSecureString}
$client=[Net.Http.HttpClient]::new();$client.Timeout=[TimeSpan]::FromSeconds(20)
$base=$Origin.GetLeftPart([UriPartial]::Authority)
function Request([string]$Method,[string]$Path,[string]$Body=''){
    $request=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method),$base+$Path)
    try{
        if($Body){$request.Content=[Net.Http.StringContent]::new($Body,[Text.Encoding]::UTF8,'application/json')}
        $response=$client.SendAsync($request).GetAwaiter().GetResult()
        try{
            if(!$response.IsSuccessStatusCode){throw "The account request returned HTTP $([int]$response.StatusCode)"}
            $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if($text){return $text|ConvertFrom-Json}
        }finally{$response.Dispose()}
    }finally{$request.Dispose()}
}
try{
    $pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    try{
        $plain=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $body=@{name=$Name;password=$plain}|ConvertTo-Json -Compress
        $path=if($Action -eq 'Register'){'/v1/auth/register'}else{'/v1/auth/token'}
        $account=Request POST $path $body
    }finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer);$plain=$null;$body=$null}
    $client.DefaultRequestHeaders.Authorization=[Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer',$account.token)
    Write-Host "Account: $Name ($($account.id))"
    switch($Action){
        Register {Write-Host 'Account created. Link its town through the service /link page.'}
        Friends {Request GET '/v1/friends'|Select-Object id,name,mutual}
        AddFriend {Request POST "/v1/friends/$Target" '{}'|Out-Null;Write-Host 'Friend added. The other account must add your ID too.'}
        RemoveFriend {Request DELETE "/v1/friends/$Target"|Out-Null;Write-Host 'Friend removed.'}
        Block {Request POST "/v1/blocks/$Target" '{}'|Out-Null;Write-Host 'Account blocked; its visit access is revoked.'}
        Unblock {Request DELETE "/v1/blocks/$Target"|Out-Null;Write-Host 'Account unblocked.'}
        Devices {Request GET '/v1/account/devices'}
        RevokeDevice {Request DELETE "/v1/account/devices/$Target"|Out-Null;Write-Host 'Device revoked.'}
    }
}finally{$account=$null;$client.Dispose()}
