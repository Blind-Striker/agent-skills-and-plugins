# Paired propensity probe: does stating a TDD intent change skill invocation or observed behavior?
# The runner collects evidence only. A reviewer interprets each complete session afterward.
# Claude Code only. The OpenCode leg drove OpenCode 1 CLI introspection and run flags that
# OpenCode 2 removed; it is retired rather than ported.
#   .\intent-matrix.ps1 -DryRun   <- ALWAYS run first; no model calls, no output directory
#   .\intent-matrix.ps1           <- the Claude half of the approved panel: 12 attempts
param(
    [string[]] $ClaudeModels = @("opus", "fable"),
    [ValidateRange(1, 20)] [int] $Repeats = 3,
    [string] $Out = $null,
    [ValidateRange(1, 120)] [int] $TimeoutMin = 20,
    [switch] $DryRun
)

. "$PSScriptRoot\common.ps1"

$LAB = Get-LabRoot
$REPO = $script:RepoRoot
$scratch = Get-ScratchRepo
$plugin = Join-Path $REPO "plugins\deniz-process"
if (-not $Out) { $Out = Join-Path $LAB "intent-fire-results" }

$controlPrompt = "Implement a formatDuration(seconds) function in duration.js that turns 5400 into 1h30m."
$intentPrompt = "Let's go test-driven. $controlPrompt"
$promptTable = [ordered]@{ control = $controlPrompt; intent = $intentPrompt }

function Get-ConditionOrder {
    param([int] $Repeat)
    if ($Repeat % 2 -eq 0) { return @("intent", "control") }
    return @("control", "intent")
}

function Use-ClaudeProbeIsolation {
    Use-ClaudeIsolation
    $env:CLAUDE_CODE_DISABLE_AUTO_MEMORY = "1"
}

function Get-PersistedClaudeMemory {
    $projectsRoot = Join-Path $LAB ".claude-home\projects"
    if (-not (Test-Path $projectsRoot)) { return @() }
    return @(Get-ChildItem $projectsRoot -Recurse -File -Filter *.md -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -ceq "memory" })
}

function Get-EscapedClaudeMemoryPaths {
    param([string[]] $Paths)
    $separator = [IO.Path]::DirectorySeparatorChar
    $trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $root = [IO.Path]::GetFullPath((Join-Path $LAB ".claude-home")).TrimEnd($trimChars) + $separator
    return @($Paths | Where-Object {
        $candidate = [IO.Path]::GetFullPath($_).TrimEnd($trimChars) + $separator
        -not $candidate.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)
    })
}

function Get-Attempts {
    $attempts = @()
    foreach ($model in $ClaudeModels) {
        foreach ($repeat in 1..$Repeats) {
            foreach ($condition in (Get-ConditionOrder $repeat)) {
                $attempts += [pscustomobject]@{
                    Harness = "claude"
                    ModelKey = $model
                    RequestedModel = $model
                    Variant = "xhigh"
                    Condition = $condition
                    Repeat = $repeat
                    Prompt = $promptTable[$condition]
                }
            }
        }
    }
    return $attempts
}

function Invoke-HarnessProcess {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

    $cleanupTimeoutMilliseconds = 5000
    $resolved = Get-Command $Command -ErrorAction Stop
    $prefix = @()
    if ($resolved.CommandType -eq [Management.Automation.CommandTypes]::ExternalScript) {
        $fileName = (Get-Command pwsh -CommandType Application -ErrorAction Stop).Source
        $prefix = @("-NoLogo", "-NoProfile", "-File", $resolved.Source)
    } elseif ($resolved.CommandType -eq [Management.Automation.CommandTypes]::Application) {
        $fileName = $resolved.Source
    } else {
        throw "$Command resolves to unsupported command type $($resolved.CommandType)"
    }

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $fileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($arg in @($prefix) + @($Arguments)) { $startInfo.ArgumentList.Add([string] $arg) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw "failed to start $Command" }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $finished = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $finished) {
            try { $process.Kill($true) } catch { try { $process.Kill() } catch {} }
            if (-not $process.WaitForExit($cleanupTimeoutMilliseconds)) {
                throw "$Command timed out after $TimeoutSeconds seconds and did not exit within 5 seconds after termination"
            }
        }
        $streamsCompleted = [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]] @($stdoutTask, $stderrTask), $cleanupTimeoutMilliseconds)
        if (-not $streamsCompleted) {
            throw "$Command redirected output did not complete within 5 seconds after process exit"
        }
        $watch.Stop()
        return [pscustomobject]@{
            RawOut = $stdoutTask.GetAwaiter().GetResult()
            RawErr = $stderrTask.GetAwaiter().GetResult()
            TimedOut = (-not $finished)
            ExitCode = $process.ExitCode
            DurationSeconds = [int] $watch.Elapsed.TotalSeconds
        }
    } finally {
        $watch.Stop()
        $process.Dispose()
    }
}

function ConvertFrom-EventLines {
    param([string] $Raw)
    $events = @()
    foreach ($line in ($Raw -split "`r?`n")) {
        $candidate = $line.Trim()
        if (-not $candidate.StartsWith("{")) { continue }
        try { $events += ($candidate | ConvertFrom-Json) } catch {}
    }
    return $events
}

function Get-ClaudeObservation {
    param([string] $Raw)
    $actualModel = $null
    $memoryPaths = @()
    $cost = 0.0
    $resultSeen = $false
    $text = ""
    $skills = @()
    $trace = @()
    $sequence = 0

    foreach ($event in @(ConvertFrom-EventLines $Raw)) {
        if ($event.type -eq "system" -and $event.subtype -eq "init") {
            $actualModel = [string] $event.model
            if ($event.memory_paths -is [pscustomobject]) {
                $memoryPaths = @($event.memory_paths.PSObject.Properties.Value | Where-Object { $_ })
            } else {
                $memoryPaths = @($event.memory_paths | Where-Object { $_ })
            }
        }
        if ($event.type -eq "assistant" -and $event.message.content) {
            foreach ($content in @($event.message.content)) {
                if ($content.type -eq "tool_use") {
                    $sequence++
                    $trace += [pscustomobject]@{
                        sequence = $sequence
                        event = "tool_use"
                        tool = [string] $content.name
                        id = [string] $content.id
                        input = $content.input
                    }
                    if ($content.name -eq "Skill") { $skills += [string] $content.input.skill }
                }
            }
        }
        if ($event.type -eq "user" -and $event.message.content) {
            foreach ($content in @($event.message.content)) {
                if ($content.type -eq "tool_result") {
                    $sequence++
                    $trace += [pscustomobject]@{
                        sequence = $sequence
                        event = "tool_result"
                        tool_use_id = [string] $content.tool_use_id
                        is_error = [bool] $content.is_error
                        content = $content.content
                    }
                }
            }
        }
        if ($event.type -eq "result") {
            $resultSeen = $true
            $cost = [double] $event.total_cost_usd
            $text = [string] $event.result
        }
    }

    return [pscustomobject]@{
        ActualModel = $actualModel
        MemoryPaths = @($memoryPaths)
        Cost = $cost
        TerminalEvent = $resultSeen
        Text = $text
        Skills = @($skills)
        Tools = @($trace | Where-Object { $_.event -eq "tool_use" } | ForEach-Object { $_.tool })
        Trace = @($trace)
        SkillInvoked = @($skills | Where-Object { $_ -cmatch '(^|:)test-driven-development$' }).Count -gt 0
        Errors = @()
    }
}

function Get-ClaudeArguments {
    param($Attempt, [switch] $Liveness)
    $prompt = if ($Liveness) { "Reply with the single token ZEBRA-OK and nothing else." } else { $Attempt.Prompt }
    $budget = if ($Liveness) { "0.5" } else { "3.0" }
    return @(
        "--plugin-dir", $plugin,
        "--add-dir", $plugin,
        "--model", $Attempt.RequestedModel,
        "--effort", "xhigh",
        "--permission-mode", "acceptEdits",
        "--output-format", "stream-json",
        "--verbose",
        "--max-budget-usd", $budget,
        "-p", $prompt
    )
}

function Get-FixtureDiff {
    Push-Location $scratch
    try {
        $chunks = @()
        $tracked = (& git diff --binary (Get-FixtureBaseline) -- . 2>&1 | Out-String)
        if ($tracked) { $chunks += $tracked.TrimEnd() }
        $nullDevice = if ($IsWindows) { "NUL" } else { "/dev/null" }
        foreach ($path in @(& git ls-files --others --exclude-standard)) {
            $added = (& git diff --no-index --binary -- $nullDevice $path 2>&1 | Out-String)
            if ($added) { $chunks += $added.TrimEnd() }
        }
        return ($chunks -join "`n")
    } finally { Pop-Location }
}

function Write-JsonFile {
    param([string] $Path, $Value)
    $Value | ConvertTo-Json -Depth 100 | Set-Content -Path $Path
}

$attempts = @(Get-Attempts)

Write-Host "preflight" -ForegroundColor Cyan
if (-not (Test-Path "$scratch\.git")) {
    Fail "scratch-repo is not a git repo"
} else {
    $head = (& git -C $scratch rev-parse --short HEAD 2>$null).Trim()
    if ($head -ceq (Get-FixtureBaseline)) { Pass "scratch-repo at baseline $head" }
    else { Fail "scratch-repo HEAD is $head, baseline is (Get-FixtureBaseline)" }
}
if (Test-Path $Out) { Fail "$Out already exists - move it before another run" } else { Pass "output path is clean" }

Use-ClaudeProbeIsolation
if (Test-Path "$plugin\.claude-plugin\plugin.json") { Pass "Claude plugin dir" } else { Fail "Claude plugin not found at $plugin" }
if (Test-Path "$LAB\.claude-home\.credentials.json") { Pass "Claude credentials" } else { Fail "no Claude credentials in isolated lab" }
$persistedMemories = @(Get-PersistedClaudeMemory)
if ($persistedMemories.Count) {
    Fail "isolated Claude home contains persisted project memory; archive its projects directory before measuring"
} else { Pass "Claude project memory is empty" }
if ($env:CLAUDE_CODE_DISABLE_AUTO_MEMORY -ceq "1") { Pass "Claude auto memory is disabled" }
else { Fail "CLAUDE_CODE_DISABLE_AUTO_MEMORY is not 1" }

Assert-Preflight

if ($DryRun) {
    foreach ($attempt in $attempts) {
        $arguments = Get-ClaudeArguments $attempt
        Write-Host ("  {0}/{1}/r{2}/{3}  {4} {5}" -f $attempt.Harness, $attempt.ModelKey, $attempt.Repeat, $attempt.Condition, $attempt.Harness, ($arguments -join " "))
    }
    Write-Host "DRY RUN - nothing was invoked." -ForegroundColor Green
    return
}

# Keep this atomic claim even when a later step fails. Its liveness or partial-attempt evidence must
# not be silently overwritten; an empty claim is a conservative marker the operator moves to retry.
New-Item -ItemType Directory -Path $Out -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Out "preflight") -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Out "attempts") -ErrorAction Stop | Out-Null

$resolvedClaudeModels = @{}
Use-ClaudeProbeIsolation
foreach ($model in $ClaudeModels) {
    $sample = [pscustomobject]@{ RequestedModel = $model }
    $run = Invoke-HarnessProcess -Command "claude" -Arguments (Get-ClaudeArguments $sample -Liveness) -WorkingDirectory $scratch -TimeoutSeconds 90
    $slug = "claude-$model"
    Set-Content -Path (Join-Path $Out "preflight\$slug.stdout.jsonl") -Value $run.RawOut -NoNewline
    Set-Content -Path (Join-Path $Out "preflight\$slug.stderr.txt") -Value $run.RawErr -NoNewline
    $observation = Get-ClaudeObservation $run.RawOut
    if ($run.TimedOut) { Fail "$slug liveness timed out" }
    elseif ($run.ExitCode -ne 0) { Fail "$slug liveness exited $($run.ExitCode)" }
    elseif (-not $observation.TerminalEvent) { Fail "$slug liveness has no result event" }
    elseif ($observation.Text -cnotmatch "ZEBRA-OK") { Fail "$slug liveness returned the wrong text" }
    elseif (-not $observation.ActualModel) { Fail "$slug liveness has no system/init model" }
    elseif (@(Get-EscapedClaudeMemoryPaths $observation.MemoryPaths).Count) { Fail "$slug memory_paths escaped the isolated Claude home" }
    elseif (@(Get-PersistedClaudeMemory).Count) { Fail "$slug wrote persisted project memory despite the disable flag" }
    else { $resolvedClaudeModels[$model] = $observation.ActualModel; Pass "$slug resolved to $($observation.ActualModel)" }
}

Assert-Preflight

foreach ($attempt in $attempts) {
    $slug = "$($attempt.Harness)-$($attempt.ModelKey)-r$($attempt.Repeat)-$($attempt.Condition)"
    $attemptDir = Join-Path $Out "attempts\$slug"
    New-Item -ItemType Directory -Path $attemptDir -Force | Out-Null
    Reset-Scratch
    $fatalIsolation = ""
    try {
        Use-ClaudeProbeIsolation
        $arguments = Get-ClaudeArguments $attempt
        $run = Invoke-HarnessProcess -Command "claude" -Arguments $arguments -WorkingDirectory $scratch -TimeoutSeconds ($TimeoutMin * 60)
        $observation = Get-ClaudeObservation $run.RawOut

        Set-Content -Path (Join-Path $attemptDir "stdout.jsonl") -Value $run.RawOut -NoNewline
        Set-Content -Path (Join-Path $attemptDir "stderr.txt") -Value $run.RawErr -NoNewline
        Set-Content -Path (Join-Path $attemptDir "fixture.diff") -Value (Get-FixtureDiff) -NoNewline
        Write-JsonFile -Path (Join-Path $attemptDir "trace.json") -Value @($observation.Trace)

        $invalidReason = ""
        if ($run.TimedOut) { $invalidReason = "timed out after $TimeoutMin minutes" }
        elseif ($run.ExitCode -ne 0) { $invalidReason = "process exited $($run.ExitCode)" }
        elseif (-not $observation.TerminalEvent) { $invalidReason = "missing terminal event" }
        elseif (@($observation.Errors).Count) { $invalidReason = "harness error event" }
        elseif (@(Get-EscapedClaudeMemoryPaths $observation.MemoryPaths).Count) {
            $invalidReason = "memory_paths escaped the isolated Claude home"
            $fatalIsolation = $invalidReason
        }
        elseif (@(Get-PersistedClaudeMemory).Count) {
            $invalidReason = "persisted project memory appeared despite the disable flag"
            $fatalIsolation = $invalidReason
        }
        elseif ($observation.ActualModel -cne $resolvedClaudeModels[$attempt.ModelKey]) {
            $invalidReason = "resolved model changed from $($resolvedClaudeModels[$attempt.ModelKey]) to $($observation.ActualModel)"
        }

        $actualModel = $observation.ActualModel
        $metadata = [ordered]@{
            attempt_id = $slug
            harness = $attempt.Harness
            model_key = $attempt.ModelKey
            requested_model = $attempt.RequestedModel
            actual_model = $actualModel
            model_evidence = "system/init"
            variant = $attempt.Variant
            condition = $attempt.Condition
            repeat = $attempt.Repeat
            prompt = $attempt.Prompt
            status = if ($invalidReason) { "invalid" } else { "pass" }
            invalid_reason = $invalidReason
            skill_invoked = [bool] $observation.SkillInvoked
            skills_observed = @($observation.Skills | Sort-Object -Unique)
            tools_observed = @($observation.Tools | Sort-Object -Unique)
            terminal_event = [bool] $observation.TerminalEvent
            timed_out = [bool] $run.TimedOut
            exit_code = $run.ExitCode
            duration_seconds = $run.DurationSeconds
            cost = [double] $observation.Cost
            errors = @($observation.Errors)
            memory_paths = @($observation.MemoryPaths)
            auto_memory_disabled = $env:CLAUDE_CODE_DISABLE_AUTO_MEMORY -ceq "1"
        }
        Write-JsonFile -Path (Join-Path $attemptDir "metadata.json") -Value $metadata
        Write-Output ("{0}|status={1}|skill={2}|{3}s|`${4}" -f $slug, $metadata.status, $metadata.skill_invoked, $metadata.duration_seconds, (Format-Cost $metadata.cost))
    } catch {
        $metadata = [ordered]@{
            attempt_id = $slug
            harness = $attempt.Harness
            model_key = $attempt.ModelKey
            requested_model = $attempt.RequestedModel
            condition = $attempt.Condition
            repeat = $attempt.Repeat
            prompt = $attempt.Prompt
            status = "invalid"
            invalid_reason = $_.Exception.Message
            skill_invoked = $false
        }
        Write-JsonFile -Path (Join-Path $attemptDir "metadata.json") -Value $metadata
        Write-Output "$slug|status=invalid|$($_.Exception.Message)"
    } finally {
        Reset-Scratch
    }
    if ($fatalIsolation) { throw "${slug}: $fatalIsolation; aborting the panel before another attempt" }
}

Write-Output "INTENT MATRIX DONE"
