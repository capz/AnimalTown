$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
. (Join-Path $PSScriptRoot '../tools/version.ps1')
$checks=0
function Assert($value,[string]$message){$script:checks++;if(!$value){throw $message}}
function Reject([scriptblock]$action,[string]$message){$rejected=$false;try{& $action|Out-Null}catch{$rejected=$true};Assert $rejected $message}
Assert ((Get-ChangeKind 'feat(water): add river tiles') -eq 'feature') 'Scoped feature title'
Assert ((Get-ChangeKind 'fix!: repair broken disc layout') -eq 'bugfix') 'Breaking marker must not request a major bump'
foreach($kind in @('docs','chore','test','ci','build','refactor','style','revert')){Assert ((Get-ChangeKind "${kind}: maintain project") -eq 'none') "Non-feature $kind"}
Reject {Get-ChangeKind 'Add a feature'} 'Unclassified changes must fail'
Reject {Get-ChangeKind "feat: first`nfix: second"} 'PR title must be a single line'
Assert ((Get-ChangeKind "Merge pull request #5 from capz/river`n`nfeat: river tiles" -MergeMessage) -eq 'feature') 'GitHub merge title classification'
Reject {Get-ChangeKind 'Merge pull request #5 from capz/river' -MergeMessage} 'Missing merge title must fail'

$repo=Join-Path (Split-Path $PSScriptRoot -Parent) ('out/version-tests/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $repo|Out-Null
function FixtureGit([string[]]$Arguments){Invoke-VersionGit $repo $Arguments}
function Commit([string]$message){$null=FixtureGit @('-c','commit.gpgsign=false','commit','--allow-empty','-m',$message)}
$null=FixtureGit @('init','-b','main');$null=FixtureGit @('config','user.name','Version checks');$null=FixtureGit @('config','user.email','version-checks@example.invalid');$null=FixtureGit @('config','core.autocrlf','false')
[IO.File]::WriteAllText((Join-Path $repo '.gitignore'),"out/`n")
$null=FixtureGit @('add','.gitignore');Commit 'Unversioned project baseline'
$base=FixtureGit @('rev-parse','HEAD')
$config=@{schema=1;xx=0;yy=0;zz=0;baseCommit=$base}
function Config { [IO.File]::WriteAllText((Join-Path $repo 'version.json'),($config|ConvertTo-Json)+"`n") }
Config;$null=FixtureGit @('add','version.json');Commit 'feat: adopt versioning'
$a=Get-BuildVersion $repo
Assert ($a.xx -eq 0 -and $a.yy -eq 1 -and $a.zz -eq 0 -and !$a.dirty) 'Initial feature from zero baseline'
Assert ($a.version -ceq "0.1.0.$($a.commit.Substring(0,12))") 'Exact four-part numeric/hash identity'
Assert ((Get-BuildVersion $repo).version -ceq $a.version) 'Rebuilds must be idempotent'
$null=FixtureGit @('tag',$a.tag)
Assert ((Get-ReleaseAnchor $repo 'HEAD') -ceq $a.tag) 'New version release anchor'
Assert (!(Get-ReleaseAnchor $repo 'HEAD^')) 'Rerun baseline excludes current release'
Commit 'fix: repair texture seams';Commit 'feat: add ponds';Commit 'docs: document water'
$b=Get-BuildVersion $repo
Assert ($b.yy -eq 2 -and $b.zz -eq 1) 'Independent counters: feature does not reset bugfix count'
Assert ($b.bb -ne $a.bb) 'Metadata-only changes still get their own commit hash'
Commit 'feat!: add a new rendering path'
Assert ((Get-BuildVersion $repo).xx -eq 0) 'Breaking feature must never bump xx automatically'
$config.xx=1;Config
Assert ((Get-BuildVersion $repo).xx -eq 1 -and (Get-BuildVersion $repo).dirty) 'Explicit major config is respected and marked dirty'
$null=FixtureGit @('add','version.json');Commit 'chore: set user-requested major'
$null=FixtureGit @('switch','-c','river');Commit 'feat: river implementation';Commit 'fix: adjust new river edge'
$null=FixtureGit @('switch','main');$null=FixtureGit @('-c','commit.gpgsign=false','merge','--no-ff','river','-m',"Merge pull request #5 from capz/river`n`nfeat: river tile rendering")
$c=Get-BuildVersion $repo
Assert ($c.xx -eq 1 -and $c.yy -eq 4 -and $c.zz -eq 1) 'Merge counts its PR once, not its implementation commits'
$legacy="build-$($c.bb)";$null=FixtureGit @('tag',$legacy)
Assert ((Get-ReleaseAnchor $repo 'HEAD') -ceq $legacy) 'Legacy release anchor compatibility'
Commit 'docs: preserve ancestor validation';$null=FixtureGit @('tag','v99.99.99.aaaaaaaaaaaa');$null=FixtureGit @('tag','v-not-a-release')
Assert ((Get-ReleaseAnchor $repo 'HEAD') -ceq $legacy) 'Ignore malformed tags and mismatched hash suffixes'
$clean=Get-BuildVersion $repo;Write-VersionMetadata $clean $repo
Assert (!(Get-BuildVersion $repo).dirty) 'Generated metadata is not a source change'
Assert ((Get-Content (Join-Path $repo 'out/version.json') -Raw|ConvertFrom-Json).version -ceq $clean.version) 'Version metadata round trip'
Commit 'An unclassified production change'
Reject {Get-BuildVersion $repo} 'Unclassified post-adoption integration commit must block release'
$config.baseCommit='ffffffffffffffffffffffffffffffffffffffff';Config
Reject {Get-BuildVersion $repo} 'Missing version history must fail instead of inventing a version'
$config.baseCommit=$base;$config.xx=-1;Config
Reject {Get-BuildVersion $repo} 'Negative major counter rejected'
$config.xx=0;$config.zz=1.5;Config
Reject {Get-BuildVersion $repo} 'Fractional counters rejected'
Write-Host "Versioning checks passed ($checks cases)."
