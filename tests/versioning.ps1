$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot '../tools/version.ps1')
$checks=0
function Assert($value,[string]$message){$script:checks++;if(!$value){throw $message}}
function Reject([scriptblock]$action,[string]$message){$rejected=$false;try{& $action|Out-Null}catch{$rejected=$true};Assert $rejected $message}
$repo=Join-Path (Split-Path $PSScriptRoot -Parent) ('out/version-tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $repo|Out-Null
function FixtureGit([string[]]$Arguments){Invoke-VersionGit $repo $Arguments}
function Commit([string]$message){$null=FixtureGit @('-c','commit.gpgsign=false','commit','--allow-empty','-m',$message)}
$null=FixtureGit @('init','-b','main');$null=FixtureGit @('config','user.name','Version checks');$null=FixtureGit @('config','user.email','version-checks@example.invalid');$null=FixtureGit @('config','core.autocrlf','false')
[IO.File]::WriteAllText((Join-Path $repo '.gitignore'),"out/`n")
[IO.File]::WriteAllText((Join-Path $repo 'version.json'),'{"schema":1}')
$null=FixtureGit @('add','.gitignore','version.json');Commit 'Existing main baseline'
$base=FixtureGit @('rev-parse','HEAD')
$config=@{schema=2;major=0;minor=11;patch=10;baseCommit=$base}
function Config {
    [IO.File]::WriteAllText((Join-Path $repo 'version.json'),($config|ConvertTo-Json)+"`n")
    if($script:commitConfig){$null=FixtureGit @('add','version.json');Commit 'Fixture checkpoint configuration'}
}
Config;$null=FixtureGit @('add','version.json');Commit 'Adopt policy'
$merges=@()
function Version([string]$kind='main',[string]$mainRef='main') {Get-BuildVersion $repo -BuildKind $kind -MainRef $mainRef -MergedPullRequests $script:merges}
$a=Version
Assert ($a.version -ceq '0.11.10' -and $a.isMain -and !$a.dirty) 'Adoption freezes the current numeric baseline'
Assert (!$a.canPublish) 'Unmerged direct main commits cannot overwrite a numbered release'
Assert ((Version).version -ceq $a.version) 'Repeated resolution is deterministic'
$empty=Get-BuildVersion $repo -BuildKind branch -MainRef $base
Assert ($empty.policySource -ceq 'migration') 'The adoption PR can introduce the new frozen checkpoint before main has schema 2'
Assert ((Get-BuildVersion $repo -BuildKind branch -MainRef $base).version -ceq $empty.version) 'Empty baseline history round-trips through the offline cache'
Assert (@((Get-Content (Join-Path $repo 'out/version-context.json') -Raw|ConvertFrom-Json).merges).Count -eq 0) 'Empty cached history contains an empty array rather than a null entry'
$null=FixtureGit @('switch','-c','feature')
Commit 'feat!: first implementation';Commit 'fix: supporting fix';Commit 'Unclassified implementation'
$branchBefore=Version 'branch'
Assert ($branchBefore.baseVersion -ceq '0.11.10' -and $branchBefore.version -ceq "0.11.10+$($branchBefore.bb)") 'Branch commits never advance the numeric main version'
Assert ($branchBefore.bb -ceq (FixtureGit @('rev-parse','--short=12','HEAD'))) 'Branch identity uses the actual head SHA'
Assert (!$branchBefore.isMain -and $branchBefore.canPublish) 'Branch artifacts use a unique build-metadata suffix'
$featureCommit=$branchBefore.commit
$null=FixtureGit @('switch','main')
$null=FixtureGit @('-c','commit.gpgsign=false','merge','--no-ff','feature','-m','Merge pull request #101 from feature')
$merge=FixtureGit @('rev-parse','HEAD');$merges+=@{number=101;mergeCommit=$merge}
$b=Version
Assert ($b.version -ceq '0.11.11' -and $b.changes.Count -eq 1 -and $b.canPublish) 'A multi-commit feature PR bumps patch exactly once'
Assert ($b.major -eq 0 -and $b.minor -eq 11) 'Major and minor remain frozen regardless of feature titles'
$null=FixtureGit @('tag',$b.tag)
Assert ((Get-ReleaseAnchor $repo 'HEAD') -ceq 'v0.11.11') 'Hashless main release tags are validation anchors'
Assert (!(Get-ReleaseAnchor $repo 'HEAD^')) 'A rerun excludes its own release anchor'
Commit 'Unclassified direct main change'
Assert ((Version).version -ceq '0.11.11' -and !(Version).canPublish) 'Direct main commits do not masquerade as a merged PR'
# A squash PR has a single integration commit, independent of its title.
Commit 'Document the river (#102)';$squash=FixtureGit @('rev-parse','HEAD');$merges+=@{number=102;mergeCommit=$squash}
Assert ((Version).version -ceq '0.11.12') 'Documentation-only squash PRs also bump patch once'
# GitHub reports the final rebased commit as merge_commit_sha.
Commit 'Rebased implementation one';$rebaseFirst=FixtureGit @('rev-parse','HEAD')
Commit 'Rebased implementation two';$rebaseLast=FixtureGit @('rev-parse','HEAD');$merges+=@{number=103;mergeCommit=$rebaseLast}
Assert ((Version).version -ceq '0.11.13' -and (Version).changes.Count -eq 3) 'A rebased multi-commit PR bumps once at its reported integration boundary'
$null=FixtureGit @('switch','--detach',$rebaseFirst)
Assert ((Version 'main' $rebaseFirst).version -ceq '0.11.12') 'A historical main build excludes PRs integrated later'
$null=FixtureGit @('switch','main')
Reject {Version 'main' $merge} 'Main builds cannot pair another integration snapshot with their checkout'
$null=FixtureGit @('switch','-c','stale-feature',$featureCommit)
$branchAfter=Version 'branch'
Assert ($branchAfter.version -ceq "0.11.13+$($branchBefore.bb)") 'A stale feature branch builds against current main instead of its fork point'
Assert ($branchAfter.commit -ceq $featureCommit -and $branchAfter.mainCommit -ceq $rebaseLast) 'Source and main snapshots are recorded separately'
Assert ((Version 'branch').version -ceq $branchAfter.version) 'The same source/main pair is stable on rebuild'
$null=FixtureGit @('tag',$branchAfter.tag)
Assert ((Get-ReleaseAnchor $repo 'HEAD') -ceq $branchAfter.tag) 'New branch build-metadata tags are recognized'
$legacy="build-$($branchAfter.bb)";$null=FixtureGit @('tag',$legacy)
Assert ((Get-ReleaseAnchor $repo 'HEAD') -in @($legacy,$branchAfter.tag)) 'Legacy build tags remain compatible'
$null=FixtureGit @('tag','v99.99.99.aaaaaaaaaaaa');$null=FixtureGit @('tag','v-not-a-release')
Assert ((Get-ReleaseAnchor $repo 'HEAD') -in @($legacy,$branchAfter.tag)) 'Mismatched hash suffixes and malformed tags are ignored'
Write-VersionMetadata $branchAfter $repo
Assert (!(Version 'branch').dirty) 'Generated version metadata is not a source change'
Assert ((Get-Content (Join-Path $repo 'out/version.json') -Raw|ConvertFrom-Json).mainCommit -ceq $rebaseLast) 'Metadata preserves the pinned main snapshot'
$merges+=@{number=104;mergeCommit=$base}
Assert ((Version 'branch').patch -eq 13) 'Baseline PRs are not counted twice'
$merges+=@{number=101;mergeCommit=$merge}
Reject {Version 'branch'} 'Duplicate PR records cannot inflate the patch'
$merges=$merges[0..2]
$merges+=@{number=0;mergeCommit=$merge};Reject {Version 'branch'} 'Invalid PR identities are rejected';$merges=$merges[0..2]
Reject {Version 'branch' 'missing-main'} 'Missing main history fails instead of falling back to feature history'
$config.patch=99;Config
Assert ((Version 'branch').patch -eq 13 -and (Version 'branch').policySource -ceq 'main') 'Feature-branch configuration edits cannot alter current main version numbers'
$config.patch=10;Config
$null=FixtureGit @('switch','main');$script:commitConfig=$true
$config.baseCommit='ffffffffffffffffffffffffffffffffffffffff';Config
Reject {Version 'branch'} 'Missing baseline fails instead of inventing a version'
$config.baseCommit=$featureCommit;Config
Reject {Version 'branch'} 'A side-branch baseline cannot silently drop integration history'
$config.baseCommit=$base;$config.major=-1;Config
Reject {Version 'branch'} 'Negative version components are rejected'
$config.major=0;$config.patch=1.5;Config
Reject {Version 'branch'} 'Fractional version components are rejected'
$config.patch=[int]::MaxValue;Config
Reject {Version 'branch'} 'Patch overflow is rejected'
$config.patch=10;Config
# Exercise REST normalization/pagination without a network request or credentials.
$null=FixtureGit @('remote','add','origin','https://github.com/example/version-fixture.git')
$script:requests=@();$script:failApi=$false
function Invoke-RestMethod {
    param($Uri,$Headers,$ErrorAction)
    if($script:failApi){throw 'Fixture API unavailable'}
    $script:requests+=@($Uri)
    if($Uri.EndsWith('page=1')){
        $response=@(1..100|ForEach-Object {[pscustomobject]@{number=$_;merged_at='2026-11-10T00:00:00Z';merge_commit_sha=('a'*40);base=@{ref='main'}}})
        $response[98].merged_at=$null;$response[99].base.ref='develop'
        return ,$response
    }
    return ,@([pscustomobject]@{number=101;merged_at='2026-11-10T00:00:00Z';merge_commit_sha=('b'*40);base=@{ref='main'}})
}
$normalized=@(Get-VersionPullRequests $repo)
Assert ($script:requests.Count -eq 2 -and $normalized.Count -eq 99) 'All REST pages are read; closed-unmerged and other-base PRs are excluded'
Assert ($normalized[-1].number -eq 101 -and $normalized[-1].mergeCommit -ceq ('b'*40)) 'GitHub integration boundaries are normalized without title parsing'
$script:failApi=$true;Reject {Get-VersionPullRequests $repo} 'Unavailable GitHub history cannot silently reset the version'
Remove-Item -LiteralPath Function:\Invoke-RestMethod
# Cache the immutable main merge history so build containers need no GitHub token.
$script:apiCalls=0
function Get-VersionPullRequests {param([string]$Root);$script:apiCalls++;return $script:merges}
$c=Get-BuildVersion $repo -BuildKind branch -MainRef main -Refresh
$cached=Get-BuildVersion $repo -BuildKind branch -MainRef main
Assert ($script:apiCalls -eq 1 -and $cached.version -ceq $c.version) 'Matching snapshots reuse cached PR history offline'
$null=FixtureGit @('switch','main');Commit 'Another integrated PR';$last=FixtureGit @('rev-parse','HEAD');$merges+=@{number=105;mergeCommit=$last}
$d=Get-BuildVersion $repo -BuildKind main -MainRef HEAD
Assert ($d.version -ceq '0.11.14' -and $script:apiCalls -eq 2) 'An advanced main snapshot invalidates cached history'
Assert ((Get-BuildVersion $repo -BuildKind branch -MainRef main).version -ceq "0.11.14+$($d.bb)") 'Detached/branch builds at main SHA still carry build metadata'
$config.patch=11;Config
$updated=Get-BuildVersion $repo -BuildKind main -MainRef HEAD
Assert ($updated.version -ceq '0.11.15' -and $script:apiCalls -eq 3) 'An explicit checkpoint configuration change invalidates cached history'
Write-Host "Versioning checks passed ($checks cases)."
