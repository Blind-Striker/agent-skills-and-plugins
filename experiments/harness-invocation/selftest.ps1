# Free, deterministic checks over this subsystem. No harness call, no token, no network.
#   .\selftest.ps1            everything
#   .\selftest.ps1 -SkipLab   omit the checks that need an isolated lab on this machine
#
# The original checks here were written against defects that actually happened while this subsystem
# was being planned: a machine-path guard that matched nothing, a lab resolver that named a directory
# which does not exist, a fixture generator that could not reproduce its own baseline, and a cost
# column that a Turkish locale made unparseable. The suite now also holds deterministic regression
# checks for later experiment and installer failures.
#
# What this does NOT cover, stated so nobody reads green as more than it is: the runners' own
# behaviour. Argv construction, leg lookup, timeout handling, the liveness call and event-stream
# parsing are exercised only by `-DryRun` and by being run for real.
param([switch] $SkipLab)

. "$PSScriptRoot\common.ps1"

$script:fails = 0
$script:skips = 0
function Test-That {
    param([string] $Name, [scriptblock] $Body)
    try {
        $r = & $Body
        if ($r -eq $true) { Write-Host "  ok   $Name" -ForegroundColor DarkGray }
        else { Write-Host "  FAIL $Name  -> $r" -ForegroundColor Red; $script:fails++ }
    } catch {
        Write-Host "  FAIL $Name  -> threw: $($_.Exception.Message)" -ForegroundColor Red
        $script:fails++
    }
}
function Skip-That {
    param([string] $Name, [string] $Why)
    Write-Host "  skip $Name  ($Why)" -ForegroundColor Yellow
    $script:skips++
}
# The footer is a FUNCTION called at the very end, so a section appended later runs BEFORE the
# verdict. An earlier draft ended with the exit code inline; anything appended after it either
# never ran (exit 1 fired first) or ran after "green" was already printed, with exit 0.
function Exit-Selftest {
    Write-Host ""
    if ($script:skips) { Write-Host "$($script:skips) check(s) skipped." -ForegroundColor Yellow }
    if ($script:fails) { Write-Host "$($script:fails) check(s) FAILED." -ForegroundColor Red; exit 1 }
    Write-Host "selftest green." -ForegroundColor Green
    exit 0
}

function Get-UnusedDriveQualifier {
    foreach ($l in [char[]]("ZYXWV")) { if (-not (Test-Path "${l}:\")) { return "${l}:" } }
    return $null
}

function Invoke-InstallerProcess {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string[]] $InstallerArgs,
        [string] $OpenCodeConfigDir
    )
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = (Get-Command node -ErrorAction Stop).Source
    $psi.WorkingDirectory = $script:RepoRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.ArgumentList.Add((Join-Path $script:RepoRoot "tools\install-opencode.ts"))
    foreach ($arg in $InstallerArgs) { $psi.ArgumentList.Add($arg) }
    $psi.Environment["HOME"] = $Root
    $psi.Environment["USERPROFILE"] = $Root
    $psi.Environment["XDG_CONFIG_HOME"] = Join-Path $Root "xdg"
    $psi.Environment.Remove("OPENCODE_CONFIG_DIR") | Out-Null
    if ($OpenCodeConfigDir) { $psi.Environment["OPENCODE_CONFIG_DIR"] = $OpenCodeConfigDir }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    if (-not $process.Start()) { throw "failed to start installer process" }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $result = [pscustomobject]@{
        ExitCode = $process.ExitCode
        StdOut = $stdout.GetAwaiter().GetResult()
        StdErr = $stderr.GetAwaiter().GetResult()
    }
    $process.Dispose()
    return $result
}

Write-Host "`n=== common.ps1 ===" -ForegroundColor Cyan

Test-That "RepoRoot is the repository" {
    (Test-Path (Join-Path $script:RepoRoot "AGENTS.md")) -and (Test-Path (Join-Path $script:RepoRoot "tools"))
}

Test-That "Format-Cost is culture-invariant" {
    $v = Format-Cost 0.5267
    if ($v -eq "0.5267") { $true } else { "got '$v' under culture $([cultureinfo]::CurrentCulture.Name)" }
}

Test-That "Split-LegModel keeps everything after the first slash" {
    $r = Split-LegModel "openrouter/anthropic/claude-opus-5"
    if ($r.provider -eq "openrouter" -and $r.id -eq "anthropic/claude-opus-5") { $true }
    else { "got provider='$($r.provider)' id='$($r.id)'" }
}

Test-That "Get-LabRoot throws rather than returning a guess" {
    # Both candidates are evaluated and made invalid: the env override points at an empty
    # directory, and RepoRoot is moved to a drive letter that does not exist on this machine.
    $q = Get-UnusedDriveQualifier
    if (-not $q) { return "no unused drive letter to test with" }
    $empty = Join-Path $PSScriptRoot ("nolab-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $empty | Out-Null
    $savedEnv  = $env:HARNESS_LAB
    $savedRoot = $script:RepoRoot
    try {
        $env:HARNESS_LAB = $empty
        $script:RepoRoot = "$q\not-a-repo"
        try {
            $r = Get-LabRoot
            "returned '$r' instead of throwing"
        } catch {
            $message = $_.Exception.Message
            $fallback = "$q\harness-probe-lab"
            if ($message -notmatch [regex]::Escape($empty)) { return "env candidate was not reported: $message" }
            if ($message -notmatch [regex]::Escape($fallback)) { return "fallback candidate was not reported: $message" }
            if ($message -notmatch "missing \.opencode-home") { return "missing lab marker was not reported: $message" }
            $true
        }
    } finally {
        $script:RepoRoot = $savedRoot
        if ($null -eq $savedEnv) { Remove-Item Env:HARNESS_LAB -ErrorAction SilentlyContinue }
        else { $env:HARNESS_LAB = $savedEnv }
        Remove-Item $empty -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-That "Get-LabRoot refuses a valid-looking lab inside the repository" {
    $savedEnv  = $env:HARNESS_LAB
    $savedRoot = $script:RepoRoot
    $lab = Join-Path $savedRoot ("lab-root-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path (Join-Path $lab ".opencode-home") | Out-Null
    try {
        $env:HARNESS_LAB = $lab
        $fallback = "$(Split-Path -Qualifier $savedRoot)\harness-probe-lab"
        if (Test-Path "$fallback\.opencode-home") {
            # Broaden the test root to the drive root so the fallback is refused too.
            $script:RepoRoot = [IO.Path]::GetPathRoot($savedRoot)
        }
        try {
            $r = Get-LabRoot
            "returned '$r' instead of refusing repository containment"
        } catch {
            if ($_.Exception.Message -match "repository containment") { $true }
            else { "wrong refusal: $($_.Exception.Message)" }
        }
    } finally {
        $script:RepoRoot = $savedRoot
        if ($null -eq $savedEnv) { Remove-Item Env:HARNESS_LAB -ErrorAction SilentlyContinue }
        else { $env:HARNESS_LAB = $savedEnv }
        Remove-Item $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-That "Get-LabRoot refuses a valid-looking lab inside the real user profile" {
    $realProfile = [Environment]::GetFolderPath('UserProfile')
    if (-not $realProfile) { return "the real user profile path is empty" }
    $q = Get-UnusedDriveQualifier
    if (-not $q) { return "no unused drive letter to isolate the fallback candidate" }
    $savedEnv  = $env:HARNESS_LAB
    $savedRoot = $script:RepoRoot
    $lab = Join-Path $realProfile ("lab-root-selftest-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path (Join-Path $lab ".opencode-home") | Out-Null
    try {
        $env:HARNESS_LAB = $lab
        $script:RepoRoot = "$q\not-a-repo"
        try {
            $r = Get-LabRoot
            "returned '$r' instead of refusing user-profile containment"
        } catch {
            if ($_.Exception.Message -match "user-profile containment") { $true }
            else { "wrong refusal: $($_.Exception.Message)" }
        }
    } finally {
        $script:RepoRoot = $savedRoot
        if ($null -eq $savedEnv) { Remove-Item Env:HARNESS_LAB -ErrorAction SilentlyContinue }
        else { $env:HARNESS_LAB = $savedEnv }
        Remove-Item $lab -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-That "common.ps1 has no side effect at load" {
    # It must not resolve the lab, set an environment variable or move the shell: make-fixture.ps1
    # dot-sources it and has to work on a machine with no lab at all.
    #
    # Read the AST, not the text. A line-anchored regex matches the same statements INSIDE the
    # functions - where they are the whole point - and reports a file that is in fact clean. That
    # false positive was this test's own first result.
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot "common.ps1"), [ref]$null, [ref]$null)
    $isTopLevel = {
        param($node)
        $p = $node.Parent
        while ($p) {
            if ($p -is [System.Management.Automation.Language.FunctionDefinitionAst]) { return $false }
            $p = $p.Parent
        }
        return $true
    }
    $bad = @()
    foreach ($n in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.CommandAst] }, $true)) {
        if ((& $isTopLevel $n) -and $n.GetCommandName() -in @("Set-Location","Push-Location","Pop-Location","Get-LabRoot")) {
            $bad += $n.GetCommandName()
        }
    }
    foreach ($n in $ast.FindAll({ param($x) $x -is [System.Management.Automation.Language.AssignmentStatementAst] }, $true)) {
        if ((& $isTopLevel $n) -and $n.Left.Extent.Text -like '$env:*') { $bad += $n.Left.Extent.Text }
    }
    if ($bad) { "top-level side effects: $($bad -join ', ')" } else { $true }
}

Write-Host "`n=== OpenCode installer isolation ===" -ForegroundColor Cyan

Test-That "installer Plan is zero-write in a throwaway XDG root" {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("installer-plan-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $result = Invoke-InstallerProcess -Root $root -InstallerArgs @("install", "--all")
        if ($result.ExitCode -ne 0) { return "Plan exited $($result.ExitCode): $($result.StdErr.Trim())" }
        if ($result.StdOut -notmatch "Plan: install") { return "Plan output was missing" }
        $writes = @(Get-ChildItem $root -Force -Recurse)
        if ($writes) { "Plan wrote: $(@($writes | ForEach-Object Name) -join ', ')" } else { $true }
    } finally {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-That "installer Apply creates only the Native tree and .deniz-skills" {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("installer-apply-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $result = Invoke-InstallerProcess -Root $root -InstallerArgs @("install", "--all", "--yes")
        if ($result.ExitCode -ne 0) { return "Apply exited $($result.ExitCode): $($result.StdErr.Trim())" }
        $destination = Join-Path $root "xdg\opencode"
        if (-not (Test-Path (Join-Path $destination ".deniz-skills\install.json"))) {
            return "Apply did not write Install state"
        }
        $allowed = @(".deniz-skills", "agents", "commands", "skills")
        $unexpected = @(Get-ChildItem $destination -Force -Recurse | Where-Object {
            $relative = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace("\", "/")
            $allowed -cnotcontains $relative.Split("/")[0]
        } | ForEach-Object { [IO.Path]::GetRelativePath($destination, $_.FullName).Replace("\", "/") })
        if ($unexpected) { "Apply wrote outside the Native tree/state: $($unexpected -join ', ')" } else { $true }
    } finally {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Test-That "installer refuses OPENCODE_CONFIG_DIR without mutation" {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("installer-refusal-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $root | Out-Null
    try {
        $result = Invoke-InstallerProcess -Root $root -InstallerArgs @("status") `
            -OpenCodeConfigDir (Join-Path $root "alternate")
        if ($result.ExitCode -eq 0) { return "refusal exited zero" }
        if ($result.StdErr -notmatch "OPENCODE_CONFIG_DIR") { return "refusal did not name OPENCODE_CONFIG_DIR" }
        $writes = @(Get-ChildItem $root -Force -Recurse)
        if ($writes) { "refusal wrote: $(@($writes | ForEach-Object Name) -join ', ')" } else { $true }
    } finally {
        Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n=== machine-path guard ===" -ForegroundColor Cyan
# NOTE the doubled backslash. PowerShell -match is .NET regex, where the bash spelling
# '[A-Za-z]:[\]' compiles SILENTLY as one character class and matches nothing - reproducing the
# exact no-op this guard exists to prevent. Keep the two spellings in sync by testing both against
# the positive control, never by copy-paste.
$guard = '[A-Za-z]:[\\]|/home/[a-z]|/Users/[a-z]'
$fx = Join-Path $PSScriptRoot "tests\fixtures"
$guardedExtensions = @(".ps1", ".md", ".json", ".txt", ".js")
$pathAllowlist = @("tests/fixtures/has-machine-path.txt")

function Get-MachinePathHits {
    $hits = @()
    foreach ($file in Get-ChildItem $PSScriptRoot -Recurse -File) {
        if ($guardedExtensions -notcontains $file.Extension.ToLowerInvariant()) { continue }
        $relative = [IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName).Replace("\", "/")
        if ($pathAllowlist -ccontains $relative) { continue }
        if ((Get-Content $file.FullName -Raw) -cmatch $guard) { $hits += $relative }
    }
    return $hits
}

function Assert-NoMachinePaths {
    $hits = @(Get-MachinePathHits)
    if ($hits) { throw "machine paths in: $($hits -join ', ')" }
}

Test-That "guard catches its positive control" {
    if ((Get-Content (Join-Path $fx "has-machine-path.txt") -Raw) -cmatch $guard) { $true }
    else { "the guard matched nothing in a file that hardcodes a machine path" }
}
Test-That "guard is silent on its negative control" {
    if ((Get-Content (Join-Path $fx "clean.txt") -Raw) -cmatch $guard) { "false positive on a clean file" } else { $true }
}
Test-That "no committed file under experiments/ carries a machine path" {
    # -cmatch, not -match: the default is case-insensitive, so /HOME/x would trip the /home/ arm.
    try { Assert-NoMachinePaths; $true } catch { $_.Exception.Message }
}
Test-That "machine-path scan does not allowlist the fixtures directory" {
    $evil = Join-Path $fx "evil-path.txt"
    try {
        Set-Content -Path $evil -Value ("E" + ":\" + "other-lab") -NoNewline
        try {
            Assert-NoMachinePaths
            "tree scan accepted tests/fixtures/evil-path.txt"
        } catch {
            if ($_.Exception.Message -match "tests/fixtures/evil-path\.txt") { $true }
            else { "tree scan failed for the wrong file: $($_.Exception.Message)" }
        }
    } finally {
        Remove-Item $evil -Force -ErrorAction SilentlyContinue
        if (Test-Path $evil) { throw "failed closed: tests/fixtures/evil-path.txt remains" }
    }
}

Write-Host "`n=== scripts ===" -ForegroundColor Cyan

Test-That "every .ps1 parses" {
    $bad = @()
    foreach ($f in Get-ChildItem $PSScriptRoot -Recurse -Filter *.ps1) {
        $e = $null
        [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e) | Out-Null
        if ($e.Count) { $bad += $f.Name }
    }
    if ($bad) { "parse errors in: $($bad -join ', ')" } else { $true }
}

Test-That "only common.ps1 defines a leg table" {
    # Shape, not model names: a check that greps for today's provider strings is defeated by the
    # next model swap, which is exactly how the duplicate table went stale in the first place.
    # selftest.ps1 is excluded because it carries the pattern as data - a test cannot test itself.
    $offenders = Get-ChildItem $PSScriptRoot -Filter *.ps1 |
        Where-Object { $_.Name -notin @("common.ps1", "selftest.ps1") } |
        Where-Object { (Get-Content $_.FullName -Raw) -match '(?m)=\s*@\{\s*(m|prov)\s*=' } |
        ForEach-Object { $_.Name }
    if ($offenders) { "leg-table shape found in: $($offenders -join ', ')" } else { $true }
}

Test-That "Invoke-HarnessProcess bounds process and redirected-stream cleanup" {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot "intent-matrix.ps1"), [ref] $tokens, [ref] $errors)
    if ($errors.Count) { return "intent-matrix.ps1 has parse errors" }

    $functions = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -ceq "Invoke-HarnessProcess"
    }, $true))
    if ($functions.Count -ne 1) { return "found $($functions.Count) Invoke-HarnessProcess definitions" }

    $calls = @($functions[0].FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
    }, $true))
    $processWaits = @($calls | Where-Object {
        $_.Expression -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $_.Expression.VariablePath.UserPath -ceq "process" -and
            $_.Member.Extent.Text -ceq "WaitForExit"
    })
    $unboundedProcessWaits = @($processWaits | Where-Object { $_.Arguments.Count -eq 0 })
    if ($unboundedProcessWaits.Count) {
        return "found zero-argument process WaitForExit: $($unboundedProcessWaits[0].Extent.Text)"
    }

    $cleanupProcessWaits = @($processWaits | Where-Object {
        $_.Arguments.Count -eq 1 -and
            @($_.Arguments[0].FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.VariableExpressionAst] -and
                    $node.VariablePath.UserPath -ceq "cleanupTimeoutMilliseconds"
            }, $true)).Count -eq 1
    })
    if ($cleanupProcessWaits.Count -ne 1) { return "no bounded post-timeout process cleanup wait" }

    $streamWaits = @($calls | Where-Object {
        if ($_.Expression -isnot [System.Management.Automation.Language.TypeExpressionAst] -or
            $_.Expression.TypeName.FullName -cne "Threading.Tasks.Task" -or
            $_.Member.Extent.Text -cne "WaitAll" -or
            $_.Arguments.Count -ne 2) { return $false }
        $tasks = @($_.Arguments[0].FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.VariableExpressionAst]
        }, $true) | ForEach-Object { $_.VariablePath.UserPath })
        $timeout = @($_.Arguments[1].FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.VariableExpressionAst]
        }, $true) | ForEach-Object { $_.VariablePath.UserPath })
        $tasks -ccontains "stdoutTask" -and $tasks -ccontains "stderrTask" -and
            $timeout -ccontains "cleanupTimeoutMilliseconds"
    })
    if ($streamWaits.Count -ne 1) { return "no bounded completion wait for both redirected streams" }
    $true
}

Test-That "every runner still contains the parts that make it a runner" {
    # This check exists because its absence let a botched edit reduce matrix.ps1 from 222 lines to
    # 28 - probe table, preflight, timeout wrapper, liveness check and run loop all gone - while
    # the whole suite stayed green. A stub parses. A stub has no machine path. A stub defines no
    # leg table. Nothing here tested that a runner does anything, so nothing noticed.
    #
    # It is a truncation alarm, not a behaviour test: it says the load-bearing parts are present,
    # never that they work. The dry-run check below is the closest this suite gets to the latter.
    $required = @{
        "matrix.ps1"        = @('$probeTable', 'Invoke-OpenCode', 'Test-LegLive', 'Assert-Preflight', 'Reset-Scratch', 'foreach ($p in $Probes)')
        "claude-matrix.ps1" = @('$PROMPT', 'Assert-Preflight', 'Reset-Scratch', 'stream-json', 'foreach ($model in $Models)')
        "intent-matrix.ps1" = @('$controlPrompt', '$intentPrompt', 'Invoke-HarnessProcess', 'Assert-Preflight', 'Reset-Scratch', 'stream-json', '--format', 'metadata.json', 'trace.json', 'memory_paths', 'persisted project memory', 'CLAUDE_CODE_DISABLE_AUTO_MEMORY', 'escaped the isolated Claude home')
        "verify.ps1"        = @('debug skill', 'debug config', 'Check ', 'customize-opencode')
        "variants.ps1"      = @('$targets', 'opencode models', 'variants')
        "probe.ps1"         = @('--plugin-dir', '--add-dir', 'tool_use', 'stream-json')
        "ocprobe.ps1"       = @('--format', 'tool_use', 'step_finish')
        "lab.ps1"           = @('Start-ClaudeLab', 'Start-OpenCodeLab', 'Sync-Lab')
    }
    $bad = @()
    foreach ($f in $required.Keys) {
        $path = Join-Path $PSScriptRoot $f
        if (-not (Test-Path $path)) { $bad += "$f is missing"; continue }
        $text = Get-Content $path -Raw
        $missing = @($required[$f] | Where-Object { -not $text.Contains($_) })
        if ($missing) { $bad += "$f lost: $($missing -join ', ')" }
    }
    if ($bad) { $bad -join "; " } else { $true }
}

Write-Host "`n=== documentation regression guards ===" -ForegroundColor Cyan

Test-That "local packed-package protocol builds and packs the repository explicitly" {
    $protocol = Get-Content (Join-Path $PSScriptRoot "protocol.md") -Raw
    $localStart = $protocol.IndexOf("### Local packed package")
    $localEnd = $protocol.IndexOf("### Immutable private GitHub Release")
    if ($localStart -lt 0 -or $localEnd -le $localStart) { return "local packed-package section was not found" }
    $local = $protocol.Substring($localStart, $localEnd - $localStart)
    $required = @(
        'npm --prefix $REPO run build',
        'npm pack $REPO --json --pack-destination $packDir'
    )
    $missing = @($required | Where-Object { -not $local.Contains($_) })
    if ($missing) { "local package commands lost repository scope: $($missing -join ', ')" } else { $true }
}

Test-That "reference audit keeps global identities and scans both harness outputs" {
    $playbook = Get-Content (Join-Path $script:RepoRoot "docs\agents\reference-audit-playbook.md") -Raw
    $required = @(
        'const paths = new Set([`plugins/${plugin}`]);',
        'taken.add(name);',
        'const opencodeRoot = `opencode/${plugin}`;',
        '`${opencodeRoot}/skills/${name}`',
        '`${opencodeRoot}/${artifact}s/${name}.md`'
    )
    $missing = @($required | Where-Object { -not $playbook.Contains($_) })
    if ($missing) { "reference audit lost output identity/path logic: $($missing -join ', ')" } else { $true }
}

Write-Host "`n=== fixture ===" -ForegroundColor Cyan

Test-That "the generator reproduces the recorded baseline" {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("fixchk-" + [guid]::NewGuid().ToString("N"))
    try {
        $sha  = & (Join-Path $PSScriptRoot "fixtures\make-fixture.ps1") -Destination $tmp -Quiet
        $want = (Get-Content (Join-Path $PSScriptRoot "fixtures\BASELINE") -Raw).Trim()
        if ($sha -eq $want) { $true } else { "generated $sha, BASELINE says $want" }
    } finally { if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } }
}

Test-That "the generator restores the shell it borrowed" {
    $depthBefore = (Get-Location -Stack).Count
    $cwdBefore   = (Get-Location).Path
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("fixenv-" + [guid]::NewGuid().ToString("N"))
    try {
        & (Join-Path $PSScriptRoot "fixtures\make-fixture.ps1") -Destination $tmp -Quiet | Out-Null
        $leaked = @("GIT_AUTHOR_NAME","GIT_AUTHOR_DATE","GIT_COMMITTER_NAME","GIT_COMMITTER_DATE") |
                  Where-Object { [Environment]::GetEnvironmentVariable($_) }
        if ($leaked) { return "leaked: $($leaked -join ', ')" }
        if ((Get-Location).Path -ne $cwdBefore) { return "cwd moved to $((Get-Location).Path)" }
        if ((Get-Location -Stack).Count -ne $depthBefore) { return "location stack depth changed" }
        $true
    } finally { if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue } }
}

Test-That "Reset-Scratch undoes a commit, not just a checkout" {
    # The most expensive bug this repository has had. A probe ceremony ends with "commit your
    # work" and obeys, so checkout-and-clean leaves HEAD moved and the next run measures the
    # previous run's residue. That produced a finding which reached an ADR and the roadmap before
    # repeats disproved it.
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("reschk-" + [guid]::NewGuid().ToString("N"))
    try {
        $base = & (Join-Path $PSScriptRoot "fixtures\make-fixture.ps1") -Destination $tmp -Quiet
        Push-Location $tmp
        try {
            Set-Content -Path (Join-Path $tmp "extra.js") -Value "export const x = 1;"
            "// dirtied" | Add-Content -Path (Join-Path $tmp "cache.js")
            git add -A 2>&1 | Out-Null
            $env:GIT_AUTHOR_NAME = "probe"; $env:GIT_AUTHOR_EMAIL = "probe@local"
            $env:GIT_COMMITTER_NAME = "probe"; $env:GIT_COMMITTER_EMAIL = "probe@local"
            git commit -q -m "commit your work" 2>&1 | Out-Null
            Set-Content -Path (Join-Path $tmp "untracked.txt") -Value "residue"
        } finally { Pop-Location }
        if ((git -C $tmp rev-parse --short HEAD).Trim() -eq $base) { return "the setup never moved HEAD; the test proves nothing" }
        Reset-Scratch -Repo $tmp
        $head   = (git -C $tmp rev-parse --short HEAD).Trim()
        $dirty  = (git -C $tmp status --porcelain)
        # HEAD alone cannot tell reset from checkout: `git checkout <sha>` DETACHES at that sha, so
        # rev-parse HEAD returns the baseline while the branch still points at the extra commit and
        # the residue stays reachable. Mutation-tested - asserting only HEAD passes on a checkout.
        $branch = git -C $tmp symbolic-ref -q --short HEAD
        if (-not $branch) { return "HEAD is detached - a checkout, not a reset; the branch still holds the commit" }
        $tip = (git -C $tmp rev-parse --short $branch).Trim()
        if ($head -ne $base) { return "HEAD is $head, baseline is $base - reset did not undo the commit" }
        if ($tip -ne $base)  { return "branch $branch is at $tip, baseline is $base - the commit is still reachable" }
        if ($dirty)          { return "tree still dirty: $($dirty -replace '\s+', ' ')" }
        $true
    } finally {
        foreach ($v in "GIT_AUTHOR_NAME","GIT_AUTHOR_EMAIL","GIT_COMMITTER_NAME","GIT_COMMITTER_EMAIL") {
            Remove-Item "Env:$v" -ErrorAction SilentlyContinue
        }
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host "`n=== derived thresholds ===" -ForegroundColor Cyan

Test-That "the ledger derivation reproduces the round's hand-verified numbers" {
    # These values are an ORACLE, not an expectation: when curation changes and this goes red,
    # update them in the same commit as the curation change, having checked the new numbers by hand.
    # Editing them only to silence a red is how an oracle stops being one.
    $l = Get-Content (Join-Path $script:RepoRoot "docs\ledger.json") -Raw | ConvertFrom-Json
    $all  = $l.PSObject.Properties.Value
    $mine = $l.PSObject.Properties | Where-Object { $_.Name -like "deniz-process/*" } | ForEach-Object { $_.Value }
    $got = @{
        skills   = @($all  | Where-Object { $_.opencode.artifacts -contains "skill" }).Count
        commands = @($all  | Where-Object { $_.opencode.artifacts -contains "command" }).Count
        model    = @($mine | Where-Object { $_.invocation -in @("auto","both") }).Count
        parked   = @($all  | Where-Object { $_.opencode.artifacts -notcontains "skill" -and @($_.opencode.parked).Count -gt 0 }).Count
    }
    $want = @{ skills = 73; commands = 33; model = 18; parked = 11 }
    $bad = @($want.Keys | Where-Object { $got[$_] -ne $want[$_] } | ForEach-Object { "$_=$($got[$_]) want $($want[$_])" })
    if ($bad) { $bad -join "; " } else { $true }
}

Write-Host "`n=== lab ===" -ForegroundColor Cyan
if ($SkipLab) {
    Skip-That "Get-LabRoot resolves to a real lab" "-SkipLab"
    Skip-That "all matrices reach the end of a dry run" "-SkipLab"
} else {
    Test-That "Get-LabRoot resolves to a real lab" {
        $l = Get-LabRoot
        if (Test-Path (Join-Path $l ".opencode-home")) { $true } else { "resolved '$l', which holds no .opencode-home" }
    }
    Test-That "all matrices reach the end of a dry run" {
        # The only check here that executes a runner rather than reading it. -DryRun walks the whole
        # path - argv, leg lookup, probe lookup, formatting, and every declared variant - and spends
        # no token. Three measurement errors in one round came from launching an hour-long job whose
        # wiring had never been executed once.
        #
        # -Out is redirected to a fresh temp path on purpose: the preflight refuses to start when a
        # results file already exists, and the lab holds one from the round that produced the
        # committed record. That refusal is correct behaviour, not something to work around in
        # anger - it is redirected, never disabled.
        $bad = @()
        foreach ($m in @(
            @{ f = "matrix.ps1";        a = @{ DryRun = $true; Legs = @("grok"); Probes = @("P1") } }
            @{ f = "claude-matrix.ps1"; a = @{ DryRun = $true; Models = @("opus"); Repeats = 1 } }
            @{ f = "intent-matrix.ps1"; a = @{ DryRun = $true; Harnesses = @("claude", "opencode"); ClaudeModels = @("opus"); OpenCodeLegs = @("grok"); Repeats = 1 } })) {
            $out = Join-Path ([IO.Path]::GetTempPath()) ("dry-" + [guid]::NewGuid().ToString("N") + ".txt")
            # HASHTABLE splatting. An array splat binds positionally once a switch is in it, so
            # -Probes landed on -TimeoutMin; and passing @(...) as an ordinary expression hands the
            # whole array to the first parameter, which left -DryRun unbound and fired four real
            # liveness calls. Both were learned here rather than in a paid round.
            $p = $m.a.Clone(); $p.Out = $out
            # 6>&1 as well as 2>&1: Write-Host writes to the INFORMATION stream, not to the
            # pipeline, so the dry-run marker is invisible to Out-String without it. The
            # first version of this check reported "never reached the marker" against a run
            # that had reached it and printed it to the console.
            $log = & (Join-Path $PSScriptRoot $m.f) @p 2>&1 6>&1 | Out-String
            Remove-Item $out -Force -ErrorAction SilentlyContinue
            if ($log -notmatch "DRY RUN") { $bad += "$($m.f): never reached the dry-run marker`n$($log.Trim())" }
        }
        if ($bad) { $bad -join "`n" } else { $true }
    }
}

Exit-Selftest
