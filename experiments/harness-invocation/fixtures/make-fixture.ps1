# Rebuilds a probe fixture, deterministically.
#
# Two fixture sets share this generator. `scratch`, the default, is the three-commit repository
# the invocation probes run in. `waves` is the one-commit limits toolkit that the verified-waves
# panel runs in (../verified-waves-panel.md); its files sit under src/ and test/. So each commit
# names a list of files relative to its set's source root, and the copy keeps each relative path.
#
# Every input to the commit hash is pinned - tree, message INCLUDING ITS BODY, author, committer
# and both timestamps - so a rebuild on any machine reproduces the same SHA and the baseline in a
# past run record keeps meaning what it meant. `Closes #42` on the scratch set's last commit is
# load-bearing twice over: without it the SHA is 139ee21, and it is the tracker reference the
# review probe's spec-discovery axis is aimed at.
#
# It writes to -Destination and NEVER deletes an existing fixture: the original lives in no other
# git repository, and an earlier version of this script removed it before proving the rebuild.
#
# Everything that mutates the caller - the location stack and six GIT_* variables - is restored in
# a finally. Without that, a failed run leaves the shell inside the fixture with a back-dated
# author identity, and the next commit in that shell is authored `lab <lab@local>` on 2026-08-01.
param(
    [string] $Destination,
    [switch] $Quiet,
    [ValidateSet("scratch", "waves")] [string] $Set = "scratch"
)
. "$PSScriptRoot\..\common.ps1"

# One entry per set: its source root under fixtures/, its default destination in the lab, and its
# commits. A commit's files are paths relative to the source root.
$sets = @{
    scratch = @{
        source      = "scratch-repo"
        destination = { Get-ScratchRepo }
        commits     = @(
            @{ files = @("duration.js");  msg = "feat: parse a duration string"; body = "";           date = "2026-08-01T12:17:11+03:00" }
            @{ files = @("cache.js");     msg = "feat: a tiny ttl cache";        body = "";           date = "2026-08-01T12:17:11+03:00" }
            @{ files = @("ratelimit.js"); msg = "feat: per-key rate limiter";    body = "Closes #42"; date = "2026-08-01T12:54:55+03:00" }
        )
    }
    waves = @{
        source      = "waves-repo"
        destination = { Get-WavesRepo }
        commits     = @(
            @{
                files = @("package.json", "src/budget.js", "src/paginate.js", "src/quota.js", "src/retry.js",
                          "src/ttl.js", "src/window.js", "test/paginate.test.js", "test/retry.test.js")
                msg   = "feat: limits toolkit"; body = ""; date = "2026-10-12T12:00:00+03:00"
            }
        )
    }
}
$fixture = $sets[$Set]

$dest = if ($Destination) { $Destination } else { & $fixture.destination }
if (Test-Path $dest) {
    if ($Destination) { throw "refusing to overwrite $dest" }
    throw "a fixture already exists at $dest. Rename it aside, or pass -Destination to build a copy."
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$src = Join-Path $PSScriptRoot $fixture.source
$vars = @("GIT_AUTHOR_NAME","GIT_AUTHOR_EMAIL","GIT_COMMITTER_NAME","GIT_COMMITTER_EMAIL",
          "GIT_AUTHOR_DATE","GIT_COMMITTER_DATE")
$saved = @{}
foreach ($v in $vars) { $saved[$v] = [Environment]::GetEnvironmentVariable($v) }

$sha = $null
Push-Location $dest
try {
    $env:GIT_AUTHOR_NAME = "lab"; $env:GIT_AUTHOR_EMAIL = "lab@local"
    $env:GIT_COMMITTER_NAME = "lab"; $env:GIT_COMMITTER_EMAIL = "lab@local"
    git init -q
    git config core.autocrlf false
    git config commit.gpgsign false
    foreach ($c in $fixture.commits) {
        foreach ($f in $c.files) {
            $target = Join-Path $dest $f
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath (Join-Path $src $f) -Destination $target
        }
        $env:GIT_AUTHOR_DATE = $c.date
        $env:GIT_COMMITTER_DATE = $c.date
        git add -- @($c.files)
        if ($c.body) { git commit -q -m $c.msg -m $c.body } else { git commit -q -m $c.msg }
    }
    $sha = (git rev-parse --short HEAD).Trim()
} finally {
    Pop-Location
    foreach ($v in $vars) {
        if ($null -eq $saved[$v]) { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
        else { Set-Item "Env:$v" $saved[$v] }
    }
}

if (-not $Quiet) { Write-Host "fixture built at $dest - $sha" -ForegroundColor Green }
return $sha
