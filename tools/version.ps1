# Main versions count merged PRs from a frozen baseline. Branches reuse main's
# numeric version and identify their actual source with SemVer build metadata.
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
function Get-VersionPullRequests {
    param([string]$Root)
    $remote=Invoke-VersionGit $Root @('remote','get-url','origin')
    if($remote -notmatch '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+?)(?:\.git)?$'){throw 'Version history requires a GitHub origin remote'}
    $repository=$Matches[1]
    $headers=@{Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='AnimalTown-versioning'}
    $token=if($env:GH_TOKEN){$env:GH_TOKEN}else{$env:GITHUB_TOKEN}
    if($token){$headers.Authorization="Bearer $token"}
    # GitHub records the integration boundary for merge, squash and rebase PRs.
    # Never count implementation commits or infer versions from their titles.
    for($page=1;;$page++){
        $response=Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/pulls?state=closed&base=main&per_page=100&page=$page" -Headers $headers -ErrorAction Stop
        $pulls=@($response)
        foreach($pull in $pulls){
            if($pull.number -notmatch '^[1-9]\d*$' -or !$pull.base.ref){throw 'Invalid GitHub PR history response'}
            if($pull.merged_at -and $pull.base.ref -ceq 'main'){
                if($pull.merge_commit_sha -cnotmatch '^[0-9a-f]{40}$'){throw 'Merged PR is missing its integration commit'}
                [pscustomobject]@{number=$pull.number;mergeCommit=$pull.merge_commit_sha}
            }
        }
        if($pulls.Count -lt 100){break}
    }
}
function Get-BuildVersion {
    param(
        [string]$Root=(Split-Path $PSScriptRoot -Parent),
        [string]$MainRef='',
        [ValidateSet('','main','branch')][string]$BuildKind='',
        [object[]]$MergedPullRequests,
        [switch]$Refresh
    )
    $PSNativeCommandUseErrorActionPreference=$false
    $Root=(Resolve-Path -LiteralPath $Root).Path
    $commit=Invoke-VersionGit $Root @('rev-parse','HEAD')
    if($commit -cnotmatch '^[0-9a-f]{40}$'){throw 'Invalid source commit'}
    if(!$BuildKind){$BuildKind=$env:ANIMALTOWN_VERSION_BUILD_KIND}
    if(!$BuildKind){$branch=& git -C $Root symbolic-ref --quiet --short HEAD 2>$null;$BuildKind=if($branch -ceq 'main'){'main'}else{'branch'}}
    if($BuildKind -notin @('main','branch')){throw 'Invalid build kind'}
    if(!$MainRef){$MainRef=$env:ANIMALTOWN_VERSION_MAIN_REF}
    if(!$MainRef){$MainRef=if($BuildKind -eq 'main'){'HEAD'}else{'refs/remotes/origin/main'}}
    $mainCommit=Invoke-VersionGit $Root @('rev-parse',"${MainRef}^{commit}")
    if($BuildKind -eq 'main' -and $mainCommit -cne $commit){throw 'A main build must resolve the checked-out main commit'}
    $configText=Invoke-VersionGit $Root @('show',"${mainCommit}:version.json")
    $config=$configText|ConvertFrom-Json
    $policySource='main'
    # The adoption PR introduces schema 2 before main contains it. After that
    # one migration, branch edits can never change main's numeric identity.
    if($config.schema -eq 1){
        $configText=(Get-Content -LiteralPath (Join-Path $Root 'version.json') -Raw).Trim()
        $config=$configText|ConvertFrom-Json;$policySource='migration'
    }
    if($config.schema -ne 2 -or $config.baseCommit -cnotmatch '^[0-9a-f]{40}$'){throw 'Invalid version.json schema/baseCommit'}
    foreach($field in @('major','minor','patch')){
        $value=$config.$field
        if(($value -isnot [long] -and $value -isnot [int]) -or $value -lt 0 -or $value -gt [int]::MaxValue){throw "Invalid version.json counter: $field"}
    }
    $null=Invoke-VersionGit $Root @('merge-base','--is-ancestor',$config.baseCommit,$mainCommit)
    $history=@((Invoke-VersionGit $Root @('rev-list','--first-parent',"$($config.baseCommit)..$mainCommit")) -split '\n'|Where-Object {$_})
    $firstParent=@((Invoke-VersionGit $Root @('rev-list','--first-parent',$mainCommit)) -split '\n')
    if($config.baseCommit -cnotin $firstParent){throw 'Version baseline must be on main first-parent history'}
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($configText)))
    $contextPath=Join-Path $Root 'out/version-context.json'
    if($PSBoundParameters.ContainsKey('MergedPullRequests')){$merges=@($MergedPullRequests)}
    else{
        $cache=if(!$Refresh -and (Test-Path -LiteralPath $contextPath)){Get-Content -LiteralPath $contextPath -Raw|ConvertFrom-Json}else{$null}
        if($cache -and $cache.schema -eq 1 -and $cache.mainCommit -ceq $mainCommit -and $cache.configHash -ceq $hash){$merges=@($cache.merges)}
        else{
            $merges=@()
            if($history.Count){$merges=@(Get-VersionPullRequests $Root|Where-Object {$_.mergeCommit -cin $history})}
            $context=[pscustomobject]@{schema=1;mainCommit=$mainCommit;configHash=$hash;merges=@($merges)}
            New-Item -ItemType Directory -Force -Path (Join-Path $Root 'out')|Out-Null
            [IO.File]::WriteAllText($contextPath,($context|ConvertTo-Json -Depth 5)+"`n")
        }
    }
    $numbers=[Collections.Generic.HashSet[long]]::new()
    $boundaries=[Collections.Generic.HashSet[string]]::new()
    $changes=@()
    foreach($merge in $merges){
        if($merge.mergeCommit -cnotmatch '^[0-9a-f]{40}$' -or $merge.number -notmatch '^[1-9]\d*$'){throw 'Invalid merged PR history'}
        if($merge.mergeCommit -cnotin $history){continue} # Exclude future, other-base and baseline PRs.
        if(!$numbers.Add([long]$merge.number) -or !$boundaries.Add($merge.mergeCommit)){throw 'Duplicate PR integration boundary'}
        $changes+=@{number=[long]$merge.number;commit=$merge.mergeCommit}
    }
    [long]$patch=[long]$config.patch+$changes.Count
    if($patch -gt [int]::MaxValue){throw 'Version counter exhausted'}
    $baseVersion="$($config.major).$($config.minor).$patch"
    $bb=$commit.Substring(0,12)
    $isMain=$BuildKind -eq 'main'
    $version=if($isMain){$baseVersion}else{"$baseVersion+$bb"}
    $dirty=[bool](Invoke-VersionGit $Root @('status','--porcelain','--untracked-files=normal'))
    [pscustomobject]@{schema=2;version=$version;tag="v$version";baseVersion=$baseVersion;major=$config.major;minor=$config.minor;patch=$patch;bb=$bb;commit=$commit;mainCommit=$mainCommit;policySource=$policySource;isMain=$isMain;dirty=$dirty;baseCommit=$config.baseCommit;changes=$changes;canPublish=(!$isMain -or $commit -ceq $config.baseCommit -or $commit -cin @($changes.commit))}
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
        foreach($match in [regex]::Matches($line,'(?:^|, |\t)tag: (build-[0-9a-f]{12}|v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:[.+][0-9a-f]{12})?)(?=,|$)')){
            $tag=$match.Groups[1].Value
            if($tag -match '^v\d+\.\d+\.\d+$' -or $tag.EndsWith($sha.Substring(0,12),[StringComparison]::Ordinal)){return $tag}
        }
    }
    return ''
}
