# Rebuilds a probe fixture, deterministically.
#
# Four fixture sets share this generator. `scratch`, the default, is the three-commit repository
# the invocation probes run in. `waves` is the one-commit limits toolkit that the verified-waves
# panel runs in (../verified-waves-panel.md); its files sit under src/ and test/. `autonomy` is the
# one-commit textkit package the autonomy panel runs in, and `fence` the one-commit badgekit .NET
# solution its scope-fence tasks run in; both are built once per arm: -Arm picks the working-style
# text appended to its AGENTS.md, so each arm has its own baseline commit. So each commit names a
# list of files relative to its set's source root, and the copy keeps each relative path.
#
# An arm set's AGENTS.md is assembled, not copied: AGENTS.base.md, then, when the arm file is not
# empty, a blank line and <set>-arms/<arm>.md, then, for the arms that take it (autonomy A3 and G,
# fence F1, E1 and E2), a blank line and the bytes of -ArmExtraFile. That extra text is never
# committed here (A3's and F1's is upstream text, G's is a private repository's approval gate, E1's
# and E2's are private repositories' process instructions), so those arms require it and no other
# arm accepts it. E2 also takes -ArmExtraDir: the documents its AGENTS.md links, copied in at their
# relative paths and committed with the rest.
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
    [ValidateSet("scratch", "waves", "autonomy", "fence")] [string] $Set = "scratch",
    [string] $Arm,
    [string] $ArmExtraFile,
    [string] $ArmExtraDir
)
. "$PSScriptRoot\..\common.ps1"

# The sets built once per arm: the arms each accepts, the arms that take -ArmExtraFile, the arms
# that take -ArmExtraDir (files copied into the fixture at their relative paths, so an appended
# AGENTS.md's links resolve), and the directory that holds the arm files.
$armSets = @{
    autonomy = @{ arms = @("A0", "A1", "A2", "A3", "G"); extra = @("A3", "G"); dir = "autonomy-arms" }
    fence    = @{ arms = @("F0", "F1", "F2", "F3", "F4", "E1", "E2"); extra = @("F1", "E1", "E2"); extraDir = @("E2"); dir = "fence-arms" }
}
if ($armSets.ContainsKey($Set)) {
    $armSet = $armSets[$Set]
    if (-not $Arm) { throw "-Set $Set requires -Arm" }
    if ($Arm -notin $armSet.arms) { throw "-Set $Set takes -Arm $($armSet.arms -join ', ')" }
    $needsExtra = $Arm -in $armSet.extra
    if ($needsExtra -and -not $ArmExtraFile) { throw "-Arm $Arm requires -ArmExtraFile" }
    if (-not $needsExtra -and $ArmExtraFile) { throw "-ArmExtraFile is valid only with -Arm $($armSet.extra -join ' or ')" }
    if ($ArmExtraFile -and -not (Test-Path -LiteralPath $ArmExtraFile -PathType Leaf)) {
        throw "no file at -ArmExtraFile $ArmExtraFile"
    }
    $needsDir = $Arm -in @($armSet.extraDir)
    if ($needsDir -and -not $ArmExtraDir) { throw "-Arm $Arm requires -ArmExtraDir" }
    if (-not $needsDir -and $ArmExtraDir) { throw "-ArmExtraDir is valid only with -Arm $(@($armSet.extraDir) -join ' or ')" }
    if ($ArmExtraDir -and -not (Test-Path -LiteralPath $ArmExtraDir -PathType Container)) {
        throw "no directory at -ArmExtraDir $ArmExtraDir"
    }
} elseif ($Arm -or $ArmExtraFile -or $ArmExtraDir) {
    throw "-Arm and -ArmExtraFile are valid only with -Set $(($armSets.Keys | Sort-Object) -join ' or ')"
}

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
    autonomy = @{
        source      = "autonomy-repo"
        destination = { Get-AutonomyRepo -Arm $Arm }
        commits     = @(
            @{
                files = @("AGENTS.md", "CHANGELOG.md", "README.md", "package.json", "scripts/release.mjs",
                          "src/admin.js", "src/dupes.js", "src/duration.js", "src/index.js", "src/net.js",
                          "src/report.js", "src/retry.js", "src/slug.js", "src/users.js",
                          "test/dupes.test.js", "test/duration.test.js", "test/net.test.js",
                          "test/report.test.js", "test/retry.test.js", "test/slug.test.js",
                          "test/users.test.js")
                msg   = "feat: textkit"; body = ""; date = "2026-10-20T12:00:00+03:00"
            }
        )
    }
    fence = @{
        source      = "fence-repo"
        destination = { Get-FenceRepo -Arm $Arm }
        commits     = @(
            @{
                files = @(".gitignore", "AGENTS.md", "Directory.Build.props", "Directory.Packages.props",
                          "README.md", "badgekit.slnx", "nuget.config",
                          "benchmarks/BadgeKit.Benchmarks/BadgeKit.Benchmarks.csproj",
                          "benchmarks/BadgeKit.Benchmarks/Program.cs", "logs/app.jsonl",
                          "samples/request-01.json", "samples/request-02.json", "samples/request-03.json",
                          "samples/request-04.json", "src/BadgeKit/BadgeIssuer.cs", "src/BadgeKit/BadgeKit.csproj",
                          "src/BadgeKit/BadgeLabel.cs", "src/BadgeKit/BadgeRequest.cs",
                          "src/BadgeKit/BadgeSerializers.cs", "src/BadgeKit/KeyValidator.cs",
                          "src/BadgeKit/Profiles.cs", "src/BadgeKit.Cli/BadgeKit.Cli.csproj",
                          "src/BadgeKit.Cli/Program.cs", "tests/BadgeKit.Tests/BadgeKit.Tests.csproj",
                          "tests/BadgeKit.Tests/IntegrationTests.cs", "tests/BadgeKit.Tests/KeyValidatorTests.cs",
                          "tests/BadgeKit.Tests/LabelTests.cs", "tests/BadgeKit.Tests/SerializerTests.cs")
                msg   = "feat: badgekit"; body = ""; date = "2026-10-21T12:00:00+03:00"
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

# An arm set's AGENTS.md, as bytes: LF line ends, no BOM, exactly as the parts hold them.
$assembledAgents = $null
if ($armSets.ContainsKey($Set)) {
    $parts = [System.Collections.Generic.List[byte]]::new()
    $parts.AddRange([IO.File]::ReadAllBytes((Join-Path $src "AGENTS.base.md")))
    $armBytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot "$($armSets[$Set].dir)\$Arm.md"))
    if ($armBytes.Length -gt 0) {
        $parts.Add(10)
        $parts.AddRange($armBytes)
    }
    if ($ArmExtraFile) {
        $parts.Add(10)
        $parts.AddRange([IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ArmExtraFile).Path))
    }
    $assembledAgents = $parts.ToArray()
}
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
            if ($null -ne $assembledAgents -and $f -eq "AGENTS.md") {
                [IO.File]::WriteAllBytes($target, $assembledAgents)
            } else {
                Copy-Item -LiteralPath (Join-Path $src $f) -Destination $target
            }
        }
        $files = @($c.files)
        if ($ArmExtraDir) {
            $root = (Resolve-Path -LiteralPath $ArmExtraDir).Path
            foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
                $rel = $item.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
                $target = Join-Path $dest $rel
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                [IO.File]::WriteAllBytes($target, [IO.File]::ReadAllBytes($item.FullName))
                $files += $rel
            }
        }
        $env:GIT_AUTHOR_DATE = $c.date
        $env:GIT_COMMITTER_DATE = $c.date
        git add -- @($files)
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
