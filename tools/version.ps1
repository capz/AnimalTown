# Deterministic release identity. No counters are committed by CI and no tags
# are needed to calculate a version; retries and parallel builds are stable.
function Invoke-VersionGit {
    param([string]$Root,[string[]]$Arguments)
    $PSNativeCommandUseErrorActionPreference=$false
    $result=& git -C $Root @Arguments 2>$null
    if($LASTEXITCODE){throw "Version history unavailable: git $($Arguments[0]). Use a full checkout containing version.json's baseCommit."}
    return ($result -join "`n").Trim()
}
function Get-ChangeKind {
    param([string]$Message,[switch]$MergeMessage)
    if(!$MergeMessage -and $Message -match '[\r\n]'){throw 'Change title must be a single line'}
    $lines=@($Message -split '\r?\n'|Where-Object {$_.Trim()})
    if(!$lines.Count){throw 'Missing change title'}
    $title=$lines[0]
    if($MergeMessage -and $title -match '^Merge pull request #\d+ '){
        if($lines.Count -lt 2){throw 'Merge commit is missing its PR title'}
        $title=$lines[1]
    }elseif($MergeMessage -and $title -match "^Merge (branch|remote-tracking branch) '"){
        return 'none' # An explicit history sync is not another feature/fix.
    }
    if($title -cnotmatch '^(feat|fix|docs|chore|test|ci|build|refactor|style|revert)(\([^\r\n()]+\))?!?: .+'){
        throw "Unclassified change '$title'. Use feat:, fix:, or a non-feature prefix (docs/chore/test/ci/build/refactor/style/revert)."
    }
    if($Matches[1] -eq 'feat'){return 'feature'}
    if($Matches[1] -eq 'fix'){return 'bugfix'}
    return 'none'
}
function Get-BuildVersion {
    param([string]$Root=(Split-Path $PSScriptRoot -Parent))
    $PSNativeCommandUseErrorActionPreference=$false
    $Root=(Resolve-Path -LiteralPath $Root).Path
    $config=Get-Content -LiteralPath (Join-Path $Root 'version.json') -Raw|ConvertFrom-Json
    if($config.schema -ne 1 -or $config.baseCommit -cnotmatch '^[0-9a-f]{40}$'){throw 'Invalid version.json schema/baseCommit'}
    foreach($field in @('xx','yy','zz')){
        $value=$config.$field
        if(($value -isnot [long] -and $value -isnot [int]) -or $value -lt 0 -or $value -gt [int]::MaxValue){throw "Invalid version.json counter: $field"}
    }
    $commit=Invoke-VersionGit $Root @('rev-parse','HEAD')
    if($commit -cnotmatch '^[0-9a-f]{40}$'){throw 'Invalid source commit'}
    $null=Invoke-VersionGit $Root @('merge-base','--is-ancestor',$config.baseCommit,$commit)
    $history=Invoke-VersionGit $Root @('rev-list','--first-parent','--reverse',"$($config.baseCommit)..$commit")
    [long]$yy=$config.yy;[long]$zz=$config.zz
    $changes=@()
    foreach($sha in @($history -split '\n'|Where-Object {$_})){
        # Changes preceding adoption are the baseline, even when another PR
        # lands between branching and merging the versioning feature.
        & git -C $Root cat-file -e "${sha}:version.json" 2>$null
        if($LASTEXITCODE){continue}
        $message=Invoke-VersionGit $Root @('show','-s','--format=%B',$sha)
        try{$kind=Get-ChangeKind $message -MergeMessage}catch{throw "Version classification failed at ${sha}: $_"}
        if($kind -eq 'feature'){$yy++}
        if($kind -eq 'bugfix'){$zz++}
        if($yy -gt [int]::MaxValue -or $zz -gt [int]::MaxValue){throw 'Version counter exhausted'}
        if($kind -ne 'none'){$changes+=@{commit=$sha;kind=$kind}}
    }
    $bb=$commit.Substring(0,12)
    $version="$($config.xx).$yy.$zz.$bb"
    $dirty=[bool](Invoke-VersionGit $Root @('status','--porcelain','--untracked-files=normal'))
    [pscustomobject]@{schema=1;version=$version;tag="v$version";xx=$config.xx;yy=$yy;zz=$zz;bb=$bb;commit=$commit;dirty=$dirty;baseCommit=$config.baseCommit;changes=$changes}
}
function Write-VersionMetadata {
    param($Version,[string]$Root)
    $out=Join-Path $Root 'out';New-Item -ItemType Directory -Force -Path $out|Out-Null
    [IO.File]::WriteAllText((Join-Path $out 'version.json'),($Version|ConvertTo-Json -Depth 5)+"`n")
}
function Get-ReleaseAnchor {
    param([string]$Root,[string]$Anchor)
    $PSNativeCommandUseErrorActionPreference=$false
    # Only ancestors on the integration history count. Keep legacy releases
    # usable during adoption; arbitrary v* tags must not hide unvalidated code.
    $history=& git -C $Root log --first-parent --decorate=short '--format=%H%x09%D' $Anchor 2>$null
    if($LASTEXITCODE){return ''}
    foreach($line in $history){
        $sha=$line.Substring(0,40)
        foreach($match in [regex]::Matches($line,'(?:^|, |\t)tag: (build-[0-9a-f]{12}|v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.[0-9a-f]{12})(?=,|$)')){
            $tag=$match.Groups[1].Value
            if($tag.EndsWith($sha.Substring(0,12),[StringComparison]::Ordinal)){return $tag}
        }
    }
    return ''
}
