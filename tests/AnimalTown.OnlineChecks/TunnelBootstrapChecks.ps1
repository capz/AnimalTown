$ErrorActionPreference='Stop'
$production=Join-Path (Split-Path $PSScriptRoot -Parent) 'server-integration.ps1'
$tokens=$null;$parseErrors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($production,[ref]$tokens,[ref]$parseErrors)
if($parseErrors.Count){throw 'Public tunnel bootstrap must parse before its fixture can run'}
$loops=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.ForStatementAst] -and $node.Extent.Text.StartsWith('for($tunnelAttempt=1;')},$true))
if($loops.Count -ne 1){throw 'Expected exactly one production public-tunnel bootstrap loop'}
# Exercise the real bootstrap with a deterministic clock and fake network tools.
# No tunnel, DNS request, database or account is created by these checks.
$bootstrap=[scriptblock]::Create($loops[0].Extent.Text.Replace('[DateTime]::UtcNow','(Get-TestUtcNow)'))
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('animaltown-tunnel-check-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $temporary 'out')|Out-Null
$checks=0
function Check([bool]$Condition,[string]$Message){$script:checks++;if(!$Condition){throw $Message}}
function Get-TestUtcNow {return $script:fixture.Utc}
function Start-Sleep([int]$Milliseconds,[int]$Seconds){$script:fixture.Utc=$script:fixture.Utc.AddMilliseconds($Milliseconds+$Seconds*1000)}
function Write-Host { $script:fixture.Diagnostics.Add(($args -join ' ')) }
function docker {
    $script:LASTEXITCODE=0
    switch($args[0]){
        'run'{$script:fixture.Attempts++;$script:fixture.LogReads=0;$script:fixture.Registered=$false;return 'disposable-fake-container'}
        'logs'{
            $script:fixture.LogReads++
            $label=if($script:fixture.Attempts -eq 1){'first-tunnel'}else{'second-tunnel'}
            "Visit https://$label.trycloudflare.com"
            if($script:fixture.Case -ne 'never-register' -and $script:fixture.LogReads -ge 3){
                if(!$script:fixture.Registered){$script:fixture.RegisteredAt=$script:fixture.Utc}
                $script:fixture.Registered=$true
                'INF Registered tunnel connection connIndex=0 protocol=http2'
            }
        }
        'stop'{$script:fixture.Stops++}
        default{throw 'Unexpected Docker operation in the isolated bootstrap fixture'}
    }
}
function Invoke-WebRequest([string]$Uri,[int]$TimeoutSec,[switch]$SkipHttpErrorCheck){
    if(!$script:fixture.Registered){$script:fixture.EarlyProbes++}
    else {Check (($script:fixture.Utc-$script:fixture.RegisteredAt).TotalSeconds -ge 2) 'Public DNS must have a settling interval after registration'}
    $script:fixture.Probes++
    $script:fixture.LastUri=$Uri
    if(($script:fixture.Case -eq 'retry-dns' -and $script:fixture.Attempts -eq 1) -or
       ($script:fixture.Case -eq 'delayed-dns' -and $script:fixture.Probes -lt 3)){
        $script:fixture.Utc=$script:fixture.Utc.AddSeconds($TimeoutSec)
        throw [Net.Http.HttpRequestException]::new('Simulated unresolvable new hostname')
    }
    return @{StatusCode=if($script:fixture.Case -eq 'unhealthy'){503}else{200}}
}
try{
    foreach($case in @('retry-dns','never-register','delayed-dns','unhealthy')){
        $start=[DateTime]::new(2026,11,27,12,0,0,[DateTimeKind]::Utc)
        $script:fixture=@{Case=$case;Utc=$start;Attempts=0;LogReads=0;Registered=$false;RegisteredAt=$start;Stops=0;Probes=0;EarlyProbes=0;LastUri='';Diagnostics=[Collections.Generic.List[string]]::new()}
        $serverRoot=$temporary;$ready=$false;$tunnelContainer=''
        . $bootstrap
        Check ($fixture.EarlyProbes -eq 0) 'A printed hostname must not be probed before a tunnel connection registers'
        Check (($fixture.Utc-$start).TotalSeconds -lt 320) 'Unreachable tunnels must consume only the bounded startup budget'
        switch($case){
            'retry-dns'{
                Check ($ready -and $fixture.Attempts -eq 2 -and $fixture.Stops -eq 1) 'An unreachable fresh endpoint must stop before trying a second unbound endpoint'
                Check ($origin -eq 'https://second-tunnel.trycloudflare.com' -and $fixture.LastUri -eq "$origin/health") 'Only the healthy endpoint may be handed to account and card setup'
            }
            'never-register'{Check (!$ready -and $fixture.Probes -eq 0 -and $fixture.Stops -eq 2) 'A tunnel that never registers must fail without public requests or card binding'}
            'delayed-dns'{Check ($ready -and $fixture.Attempts -eq 1 -and $fixture.Stops -eq 0 -and $fixture.Probes -eq 3) 'Temporary DNS propagation must retain the same healthy tunnel'}
            'unhealthy'{Check (!$ready -and $fixture.Stops -eq 2) 'A registered tunnel with an unhealthy origin must still fail qualification'}
        }
    }
}finally{
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
[Console]::WriteLine("$checks tunnel bootstrap checks passed: connection registration, delayed DNS, bounded retries and no premature endpoint binding.")
