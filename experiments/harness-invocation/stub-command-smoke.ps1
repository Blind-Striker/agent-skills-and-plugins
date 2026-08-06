# Proves the parked-body stub for a bundled manual OpenCode command in both supported mount forms.
# Always run -DryRun first. Raw process output stays under the external lab.
param(
    [Parameter(Mandatory)] [string] $Leg,
    [ValidateRange(30, 1800)] [int] $TimeoutSeconds = 300,
    [switch] $DryRun
)

. "$PSScriptRoot\common.ps1"

$script:CleanupTimeoutMilliseconds = 5000
$script:ProjectMountCreated = $false
$script:GlobalCommandMounted = $false
$script:GlobalSkillMounted = $false
$script:LegResults = [Collections.Generic.List[object]]::new()

function Invoke-HarnessProcess {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string] $WorkingDirectory,
        [Parameter(Mandatory)] [int] $TimeoutSeconds
    )

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
    foreach ($argument in @($prefix) + @($Arguments)) { $startInfo.ArgumentList.Add([string] $argument) }

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
            if (-not $process.WaitForExit($script:CleanupTimeoutMilliseconds)) {
                throw "$Command timed out after $TimeoutSeconds seconds and did not exit within 5 seconds after termination"
            }
        }
        $streamsCompleted = [Threading.Tasks.Task]::WaitAll(
            [Threading.Tasks.Task[]] @($stdoutTask, $stderrTask), $script:CleanupTimeoutMilliseconds)
        if (-not $streamsCompleted) {
            throw "$Command redirected output did not complete within 5 seconds after process exit"
        }
        $watch.Stop()
        return [pscustomobject]@{
            RawOut = $stdoutTask.GetAwaiter().GetResult()
            RawErr = $stderrTask.GetAwaiter().GetResult()
            TimedOut = (-not $finished)
            ExitCode = $process.ExitCode
            DurationSeconds = [int] [Math]::Ceiling($watch.Elapsed.TotalSeconds)
        }
    } finally {
        $watch.Stop()
        $process.Dispose()
    }
}

function Save-ProcessCapture {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] $Run
    )

    Set-Content -Path (Join-Path $script:RunRoot "$Name.stdout") -Value $Run.RawOut -NoNewline
    Set-Content -Path (Join-Path $script:RunRoot "$Name.stderr") -Value $Run.RawErr -NoNewline
}

function Test-PathInside {
    param([Parameter(Mandatory)] [string] $Candidate, [Parameter(Mandatory)] [string] $Root)

    try {
        $trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $separator = [IO.Path]::DirectorySeparatorChar
        $candidateFull = [IO.Path]::GetFullPath($Candidate).TrimEnd($trimChars) + $separator
        $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd($trimChars) + $separator
        return $candidateFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)
    } catch {
        return $false
    }
}

function Assert-IsolatedEnvironment {
    if ([Environment]::GetEnvironmentVariable("OPENCODE_CONFIG_DIR")) {
        throw "OPENCODE_CONFIG_DIR is set; this smoke only supports the isolated project-local and XDG global mounts"
    }
    foreach ($name in "USERPROFILE", "HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME") {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not $value -or -not (Test-PathInside -Candidate $value -Root $script:Lab)) {
            throw "$name does not resolve inside the external lab"
        }
    }
    if ($env:OPENCODE_DISABLE_CLAUDE_CODE_SKILLS -cne "1") {
        throw "OPENCODE_DISABLE_CLAUDE_CODE_SKILLS is not 1"
    }
}

function Assert-RunSucceeded {
    param([Parameter(Mandatory)] $Run, [Parameter(Mandatory)] [string] $Name)

    if ($Run.TimedOut) { throw "$Name timed out" }
    if ($Run.ExitCode -ne 0) { throw "$Name exited $($Run.ExitCode)" }
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

function Get-StringLeaves {
    param($Value)

    $values = [Collections.Generic.List[string]]::new()
    function Add-StringLeaves($Node) {
        if ($null -eq $Node) { return }
        if ($Node -is [string]) {
            [void] $values.Add($Node)
            return
        }
        if ($Node -is [Collections.IDictionary]) {
            foreach ($entry in $Node.GetEnumerator()) { Add-StringLeaves $entry.Value }
            return
        }
        if ($Node -is [Collections.IEnumerable]) {
            foreach ($item in $Node) { Add-StringLeaves $item }
            return
        }
        if ($Node -is [psobject]) {
            foreach ($property in $Node.PSObject.Properties) { Add-StringLeaves $property.Value }
        }
    }

    Add-StringLeaves $Value
    return @($values)
}

function Get-OpenCodeObservation {
    param([Parameter(Mandatory)] [string] $Raw)

    $readInputs = [Collections.Generic.List[object]]::new()
    $textEvents = [Collections.Generic.List[object]]::new()
    $errors = [Collections.Generic.List[string]]::new()
    $text = ""
    $terminalEvent = $false
    $sequence = 0
    foreach ($event in @(ConvertFrom-EventLines $Raw)) {
        switch ($event.type) {
            "tool_use" {
                $sequence++
                if ([string] $event.part.tool -ceq "read") {
                    foreach ($input in @(Get-StringLeaves $event.part.state.input)) {
                        [void] $readInputs.Add([pscustomobject]@{
                            Path = $input
                            Status = [string] $event.part.state.status
                            Sequence = $sequence
                        })
                    }
                }
            }
            "text" {
                $sequence++
                $chunk = [string] $event.part.text
                $text += $chunk
                [void] $textEvents.Add([pscustomobject]@{ Text = $chunk; Sequence = $sequence })
            }
            "step_finish" { $sequence++; $terminalEvent = $true }
            "error" {
                $sequence++
                $message = [string] $event.error.data.message
                if ($message) { [void] $errors.Add(($message -replace "\s+", " ")) }
                else { [void] $errors.Add("OpenCode error event") }
            }
        }
    }
    return [pscustomobject]@{
        ReadInputs = @($readInputs)
        TextEvents = @($textEvents)
        Text = $text
        TerminalEvent = $terminalEvent
        Errors = @($errors)
    }
}

function Find-ReadSuffix {
    param(
        [Parameter(Mandatory)] [object[]] $Inputs,
        [Parameter(Mandatory)] [string] $ExpectedSuffix
    )

    foreach ($input in $Inputs) {
        $normalized = ([string] $input.Path).Replace("\", "/")
        if ($input.Status -ceq "completed" -and $normalized.EndsWith($ExpectedSuffix, [StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ Suffix = $ExpectedSuffix; Sequence = $input.Sequence }
        }
    }
    return $null
}

function Get-ResolvedState {
    param([Parameter(Mandatory)] [string] $Name)

    Assert-IsolatedEnvironment
    $configRun = Invoke-HarnessProcess -Command "opencode" -Arguments @("debug", "config") -WorkingDirectory $script:Scratch -TimeoutSeconds 90
    Save-ProcessCapture -Name "$Name-config" -Run $configRun
    Assert-RunSucceeded -Run $configRun -Name "$Name opencode debug config"
    try { $config = $configRun.RawOut | ConvertFrom-Json } catch { throw "$Name opencode debug config did not return JSON" }

    $skillRun = Invoke-HarnessProcess -Command "opencode" -Arguments @("debug", "skill") -WorkingDirectory $script:Scratch -TimeoutSeconds 90
    Save-ProcessCapture -Name "$Name-skill" -Run $skillRun
    Assert-RunSucceeded -Run $skillRun -Name "$Name opencode debug skill"
    try { $skills = @($skillRun.RawOut | ConvertFrom-Json) } catch { throw "$Name opencode debug skill did not return JSON" }

    $commands = if ($config.command) { @($config.command.PSObject.Properties.Name) } else { @() }
    $plugins = @($config.plugin | Where-Object { $_ })
    return [pscustomobject]@{
        CommandNames = $commands
        Skills = $skills
        SkillNames = @($skills | ForEach-Object { [string] $_.name })
        Plugins = $plugins
    }
}

function Assert-IsolationDiscovery {
    param([Parameter(Mandatory)] $State)

    if ($State.Plugins.Count) { throw "isolated OpenCode config declares plugin packages" }
    if (@($State.Skills | Where-Object { $_.name -ceq "customize-opencode" }).Count -ne 1) {
        throw "isolated OpenCode discovery is missing the customize-opencode built-in control"
    }
    $outside = @($State.Skills | Where-Object {
        $_.location -ne "<built-in>" -and -not (Test-PathInside -Candidate ([string] $_.location) -Root $script:Lab)
    })
    if ($outside.Count) { throw "OpenCode skill discovery escaped the isolated lab" }
}

function Assert-BetaAbsent {
    param([Parameter(Mandatory)] $State, [Parameter(Mandatory)] [string] $Stage)

    if ($State.CommandNames -ccontains "beta") { throw "beta command resolves before staging the $Stage mount" }
    if ($State.SkillNames -ccontains "beta") { throw "beta skill resolves before staging the $Stage mount" }
}

function Assert-BetaDiscovery {
    param([Parameter(Mandatory)] $State, [Parameter(Mandatory)] [string] $Stage)

    if ($State.CommandNames -cnotcontains "beta") { throw "beta command is not in resolved commands for the $Stage mount" }
    if ($State.SkillNames -ccontains "beta") { throw "beta appears in opencode debug skill for the $Stage mount" }
}

function Get-DeclaredModel {
    param([Parameter(Mandatory)] [string] $Model, [Parameter(Mandatory)] [string] $Provider)

    $run = Invoke-HarnessProcess -Command "opencode" -Arguments @("models", $Provider, "--verbose") -WorkingDirectory $script:Scratch -TimeoutSeconds 90
    Save-ProcessCapture -Name "provider-models" -Run $run
    Assert-RunSucceeded -Run $run -Name "opencode models $Provider"
    $lines = $run.RawOut -split "`r?`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -ceq $Model) { $start = $i + 1; break }
    }
    if ($start -lt 0) { return $null }

    $depth = 0
    $buffer = @()
    $started = $false
    for ($i = $start; $i -lt $lines.Count; $i++) {
        $buffer += $lines[$i]
        $depth += ([regex]::Matches($lines[$i], "\{")).Count - ([regex]::Matches($lines[$i], "\}")).Count
        if ($lines[$i].Contains("{")) { $started = $true }
        if ($started -and $depth -le 0) { break }
    }
    try { return (($buffer -join "`n") | ConvertFrom-Json) } catch { return $null }
}

function Test-ProviderCredential {
    param([Parameter(Mandatory)] [string] $Provider)

    $run = Invoke-HarnessProcess -Command "opencode" -Arguments @("auth", "list") -WorkingDirectory $script:Scratch -TimeoutSeconds 90
    Save-ProcessCapture -Name "provider-auth" -Run $run
    Assert-RunSucceeded -Run $run -Name "opencode auth list"
    $plain = [regex]::Replace($run.RawOut, "`e\[[0-?]*[ -/]*[@-~]", "")
    $needle = ($Provider -replace "[^A-Za-z0-9]", "").ToLowerInvariant()
    $haystack = ($plain -replace "[^A-Za-z0-9]", "").ToLowerInvariant()
    return $haystack.Contains($needle)
}

function Get-OpenCodeArguments {
    param([Parameter(Mandatory)] $Spec, [Parameter(Mandatory)] [string] $Prompt)

    $arguments = @("run", "--format", "json", "--auto", "-m", $Spec.m)
    if ($Spec.v) { $arguments += @("--variant", $Spec.v) }
    $arguments += @("--command", "beta", $Prompt)
    return $arguments
}

function Test-ProviderLive {
    param([Parameter(Mandatory)] $Spec)

    # The liveness command deliberately has no beta mount. It proves the provider before either
    # fixture is staged; the two smoke calls below each prove command/skill discovery immediately first.
    $arguments = @("run", "--format", "json", "--auto", "-m", $Spec.m)
    if ($Spec.v) { $arguments += @("--variant", $Spec.v) }
    $arguments += "Reply with the single token ZX-STUB-LIVE and nothing else."
    $run = Invoke-HarnessProcess -Command "opencode" -Arguments $arguments -WorkingDirectory $script:Scratch -TimeoutSeconds 90
    Save-ProcessCapture -Name "provider-liveness" -Run $run
    if ($run.TimedOut) { throw "provider liveness timed out after 90 seconds" }
    if ($run.ExitCode -ne 0) { throw "provider liveness exited $($run.ExitCode)" }
    $observation = Get-OpenCodeObservation $run.RawOut
    if (-not $observation.TerminalEvent) { throw "provider liveness has no step_finish event" }
    if ($observation.Errors.Count) { throw "provider liveness emitted an OpenCode error event" }
    if ($observation.Text -cnotmatch "ZX-STUB-LIVE") { throw "provider liveness did not return ZX-STUB-LIVE" }
}

function New-GeneratedFixture {
    $adapter = Join-Path $script:RunRoot "build-fixture.mjs"
    $fixture = Join-Path $script:RunRoot "emitter-fixture"
    $buildUrl = [Uri]::new((Join-Path $script:RepoRoot "tools\build.ts")).AbsoluteUri | ConvertTo-Json -Compress
    $adapterSource = @'
import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { buildAll } from __BUILD_URL__;

const root = process.argv[2];
if (!root) throw new Error("fixture root argument is required");
rmSync(root, { recursive: true, force: true });
mkdirSync(join(root, "external", "sp", ".claude-plugin"), { recursive: true });
mkdirSync(join(root, "external", "sp", "skills", "beta", "references"), { recursive: true });
mkdirSync(join(root, "curation"), { recursive: true });

writeFileSync(join(root, "external", "sp", ".claude-plugin", "plugin.json"), `${JSON.stringify({ name: "superpowers" })}\n`);
const originalSourceSkill = [
  "---",
  "name: beta",
  "description: Stub command smoke fixture",
  "---",
  "",
  "Baseline body.",
  "",
].join("\n");
const replacementBody = "Read this BODY.md before doing anything else. Do not inspect any other file or perform any other work. Reply exactly ZX-STUB-BODY-RAN ZX-STUB-ARG.\n";
writeFileSync(
  join(root, "external", "sp", "skills", "beta", "SKILL.md"),
  originalSourceSkill.replace("Baseline body.\n", replacementBody),
);
writeFileSync(join(root, "external", "sp", "skills", "beta", "references", "keep.txt"), "bundled fixture\n");
writeFileSync(
  join(root, "curation", "deniz-process.yaml"),
  [
    "plugin:",
    "  name: deniz-process",
    "  description: Runtime smoke fixture",
    "  version: 0.1.0",
    "items:",
    "  - source: sp/skills/beta",
    "    invocation: manual",
    "",
  ].join("\n"),
);

const report = buildAll(root);
console.log(JSON.stringify({ report }));
'@
    Set-Content -Path $adapter -Value $adapterSource.Replace("__BUILD_URL__", $buildUrl) -NoNewline

    $run = Invoke-HarnessProcess -Command "node" -Arguments @($adapter, $fixture) -WorkingDirectory $script:RunRoot -TimeoutSeconds 120
    Save-ProcessCapture -Name "build-fixture" -Run $run
    Assert-RunSucceeded -Run $run -Name "buildAll fixture adapter"
    try { $adapterResult = $run.RawOut | ConvertFrom-Json } catch { throw "buildAll fixture adapter did not return JSON" }

    $command = Join-Path $fixture "opencode\commands\beta.md"
    $skill = Join-Path $fixture "opencode\skills\beta"
    $body = Join-Path $skill "BODY.md"
    if (-not (Test-Path $command) -or -not (Test-Path $body) -or -not (Test-Path (Join-Path $skill "references\keep.txt"))) {
        throw "buildAll fixture adapter did not emit the bundled manual command shape"
    }
    if (Test-Path (Join-Path $skill "SKILL.md")) { throw "generated manual beta is skill-discoverable" }
    $commandText = Get-Content -Path $command -Raw
    $bodyText = Get-Content -Path $body -Raw
    if ($commandText -cnotmatch "skills/beta/BODY\.md" -or $commandText.Contains("ZX-STUB-BODY-RAN")) {
        throw "generated beta command is not the buildAll parked-body stub"
    }
    if ($bodyText -cnotmatch "ZX-STUB-BODY-RAN" -or $bodyText -cnotmatch "ZX-STUB-ARG") {
        throw "generated beta BODY.md lost the replacement source body"
    }
    if (@($adapterResult.report | Where-Object { $_ -cmatch "body parked at skills/beta/BODY\.md" }).Count -ne 1) {
        throw "buildAll fixture adapter did not report beta body parking"
    }
    return [pscustomobject]@{ Root = $fixture; Command = $command; Skill = $skill }
}

function Get-RelativeFiles {
    param([Parameter(Mandatory)] [string] $Root)

    return @(Get-ChildItem -Path $Root -Recurse -File |
        ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName).Replace("\", "/") } |
        Sort-Object)
}

function Assert-ProjectMountShape {
    $expected = @("commands/beta.md") + @((Get-RelativeFiles $script:Generated.Skill) | ForEach-Object { "skills/beta/$_" })
    $actual = Get-RelativeFiles $script:ProjectConfig
    if (($expected -join "`n") -cne ($actual -join "`n")) {
        throw "project-local mount contains something other than the generated beta command and parked bundle"
    }
}

function Assert-ArtifactCopy {
    param([Parameter(Mandatory)] [string] $Source, [Parameter(Mandatory)] [string] $Destination, [Parameter(Mandatory)] [string] $Label)

    if (-not (Test-Path $Destination)) { throw "$Label was not staged" }
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Source).Hash
    $destinationHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Destination).Hash
    if ($sourceHash -cne $destinationHash) { throw "$Label differs from the generated buildAll artifact" }
}

function Assert-MountsClear {
    if (Test-Path $script:ProjectConfig) { throw "scratch repository already has a project-local .opencode mount" }
    if ((Test-Path $script:GlobalCommand) -or (Test-Path $script:GlobalSkill)) {
        throw "isolated XDG global config already has beta; refusing to overwrite another mount"
    }
}

function Stage-ProjectMount {
    Assert-MountsClear
    New-Item -ItemType Directory -Path (Join-Path $script:ProjectConfig "commands") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $script:ProjectConfig "skills") -Force | Out-Null
    $script:ProjectMountCreated = $true
    Copy-Item -LiteralPath $script:Generated.Command -Destination (Join-Path $script:ProjectConfig "commands\beta.md") -Force
    Copy-Item -LiteralPath $script:Generated.Skill -Destination (Join-Path $script:ProjectConfig "skills\beta") -Recurse -Force
    Assert-ArtifactCopy -Source $script:Generated.Command -Destination (Join-Path $script:ProjectConfig "commands\beta.md") -Label "project beta command"
    Assert-ArtifactCopy -Source (Join-Path $script:Generated.Skill "BODY.md") -Destination (Join-Path $script:ProjectConfig "skills\beta\BODY.md") -Label "project beta body"
    Assert-ProjectMountShape
}

function Remove-ProjectMount {
    if ($script:ProjectMountCreated -and (Test-Path $script:ProjectConfig)) {
        Remove-Item -LiteralPath $script:ProjectConfig -Recurse -Force -ErrorAction Stop
    }
    $script:ProjectMountCreated = $false
}

function Stage-GlobalMount {
    Assert-MountsClear
    New-Item -ItemType Directory -Path (Split-Path $script:GlobalCommand -Parent) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $script:GlobalSkill -Parent) -Force | Out-Null
    $script:GlobalCommandMounted = $true
    Copy-Item -LiteralPath $script:Generated.Command -Destination $script:GlobalCommand -Force
    $script:GlobalSkillMounted = $true
    Copy-Item -LiteralPath $script:Generated.Skill -Destination $script:GlobalSkill -Recurse -Force
    Assert-ArtifactCopy -Source $script:Generated.Command -Destination $script:GlobalCommand -Label "global beta command"
    Assert-ArtifactCopy -Source (Join-Path $script:Generated.Skill "BODY.md") -Destination (Join-Path $script:GlobalSkill "BODY.md") -Label "global beta body"
}

function Remove-GlobalMount {
    if ($script:GlobalCommandMounted -and (Test-Path $script:GlobalCommand)) {
        Remove-Item -LiteralPath $script:GlobalCommand -Force -ErrorAction Stop
    }
    if ($script:GlobalSkillMounted -and (Test-Path $script:GlobalSkill)) {
        Remove-Item -LiteralPath $script:GlobalSkill -Recurse -Force -ErrorAction Stop
    }
    $script:GlobalCommandMounted = $false
    $script:GlobalSkillMounted = $false
}

function Write-LegResult {
    param([Parameter(Mandatory)] $Result)

    [void] $script:LegResults.Add([pscustomobject] $Result)
    $Result | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $script:RunRoot "$($Result.mount).result.json")
}

function Invoke-SmokeLeg {
    param([Parameter(Mandatory)] [string] $Mount, [Parameter(Mandatory)] [string] $ExpectedReadSuffix)

    $discovery = Get-ResolvedState -Name "$Mount-before-run"
    Assert-IsolationDiscovery $discovery
    Assert-BetaDiscovery -State $discovery -Stage $Mount
    $arguments = Get-OpenCodeArguments -Spec $script:Spec -Prompt "ZX-STUB-ARG"

    if ($DryRun) {
        Write-Host ("  {0} opencode {1}" -f $Mount, ($arguments -join " ")) -ForegroundColor DarkGray
        Write-LegResult ([ordered]@{
            mount = $Mount
            status = "dry-run"
            command_discovered = $true
            skill_discovered = $false
            expected_read_suffix = $ExpectedReadSuffix
        })
        return
    }

    $run = Invoke-HarnessProcess -Command "opencode" -Arguments $arguments -WorkingDirectory $script:Scratch -TimeoutSeconds $TimeoutSeconds
    Save-ProcessCapture -Name "$Mount-run" -Run $run
    $observation = Get-OpenCodeObservation $run.RawOut
    $read = Find-ReadSuffix -Inputs $observation.ReadInputs -ExpectedSuffix $ExpectedReadSuffix
    $readSuffix = if ($read) { $read.Suffix } else { $null }
    $finalText = if ($read) {
        (@($observation.TextEvents | Where-Object { $_.Sequence -gt $read.Sequence } | ForEach-Object { $_.Text }) -join "")
    } else {
        ""
    }
    $bodyMarker = $finalText -cmatch "ZX-STUB-BODY-RAN"
    $argumentMarker = $finalText -cmatch "ZX-STUB-ARG"
    $failures = @()
    if ($run.TimedOut) { $failures += "timed out after $TimeoutSeconds seconds" }
    elseif ($run.ExitCode -ne 0) { $failures += "process exited $($run.ExitCode)" }
    if (-not $observation.TerminalEvent) { $failures += "missing step_finish event" }
    if ($observation.Errors.Count) { $failures += "OpenCode error event" }
    if (-not $readSuffix) { $failures += "no completed read-tool input ending in $ExpectedReadSuffix" }
    if (-not $bodyMarker) { $failures += "final text missing ZX-STUB-BODY-RAN" }
    if (-not $argumentMarker) { $failures += "final text missing ZX-STUB-ARG" }
    $result = [ordered]@{
        mount = $Mount
        status = if ($failures.Count) { "fail" } else { "pass" }
        command_discovered = $true
        skill_discovered = $false
        expected_read_suffix = $ExpectedReadSuffix
        observed_read_suffix = $readSuffix
        markers = @("ZX-STUB-BODY-RAN", "ZX-STUB-ARG")
        marker_text_observed = ($bodyMarker -and $argumentMarker)
        terminal_event = [bool] $observation.TerminalEvent
        duration_seconds = $run.DurationSeconds
        failure = ($failures -join "; ")
    }
    Write-LegResult $result
    if ($failures.Count) { throw "$Mount smoke failed: $($failures -join '; ')" }
    Write-Host "  PASS $Mount read $readSuffix; both markers observed" -ForegroundColor Green
}

function Write-RunSummary {
    param([string] $Failure)

    $summary = [ordered]@{
        run_id = $script:RunId
        mode = if ($DryRun) { "dry-run" } else { "smoke" }
        emitter_commit = $script:EmitterCommit
        runner_revision = "sha256:$script:RunnerHash"
        harness_version = $script:OpenCodeVersion
        model = $script:Spec.m
        variant = $script:Spec.v
        opencode_config_dir_unset = -not [bool] [Environment]::GetEnvironmentVariable("OPENCODE_CONFIG_DIR")
        isolation_ok = [bool] $script:IsolationVerified
        results = @($script:LegResults)
        failure = $Failure
    }
    $summary | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $script:RunRoot "summary.json")
}

$savedEnvironment = @{}
foreach ($name in "USERPROFILE", "HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "OPENCODE_CONFIG_DIR", "OPENCODE_DISABLE_CLAUDE_CODE_SKILLS") {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
$failure = ""
$scratchReady = $false
try {
    if (-not $script:LegTable.ContainsKey($Leg)) { throw "unknown leg '$Leg'" }
    $script:Spec = $script:LegTable[$Leg]
    $script:Lab = Get-LabRoot
    $runContainer = Join-Path $script:Lab "stub-command-smoke"
    New-Item -ItemType Directory -Path $runContainer -Force | Out-Null
    $script:RunId = "$(Get-Date -Format yyyyMMddTHHmmss)-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
    $script:RunRoot = Join-Path $runContainer $script:RunId
    New-Item -ItemType Directory -Path $script:RunRoot -ErrorAction Stop | Out-Null
    $script:Scratch = Get-ScratchRepo
    $script:EmitterCommit = (& git -C $script:RepoRoot rev-parse HEAD 2>$null).Trim()
    $script:RunnerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $PSCommandPath).Hash.ToLowerInvariant()

    Use-OpenCodeIsolation
    $script:ProjectConfig = Join-Path $script:Scratch ".opencode"
    $script:GlobalConfig = Join-Path $env:XDG_CONFIG_HOME "opencode"
    $script:GlobalCommand = Join-Path $script:GlobalConfig "commands\beta.md"
    $script:GlobalSkill = Join-Path $script:GlobalConfig "skills\beta"

    Write-Host "preflight" -ForegroundColor Cyan
    Pass "leg $Leg -> $($script:Spec.m)"
    if (-not (Test-Path (Join-Path $script:Scratch ".git"))) { Fail "scratch-repo is not a git repo" }
    else {
        $head = (& git -C $script:Scratch rev-parse --short HEAD 2>$null).Trim()
        $dirty = (& git -C $script:Scratch status --porcelain 2>$null | Out-String).Trim()
        if ($head -cne (Get-FixtureBaseline)) { Fail "scratch-repo is not at its fixture baseline" }
        elseif ($dirty) { Fail "scratch-repo is dirty" }
        else { Pass "scratch-repo is at its fixture baseline"; $scratchReady = $true }
    }
    try { Assert-IsolatedEnvironment; Pass "isolated XDG environment with OPENCODE_CONFIG_DIR unset" }
    catch { Fail $_.Exception.Message }
    if (-not (Test-Path (Join-Path $env:XDG_DATA_HOME "opencode\auth.json"))) { Fail "no OpenCode auth.json in the isolated lab" }
    else { Pass "isolated OpenCode auth.json" }
    Assert-Preflight

    $versionRun = Invoke-HarnessProcess -Command "opencode" -Arguments @("--version") -WorkingDirectory $script:Scratch -TimeoutSeconds 90
    Save-ProcessCapture -Name "opencode-version" -Run $versionRun
    Assert-RunSucceeded -Run $versionRun -Name "opencode --version"
    $script:OpenCodeVersion = $versionRun.RawOut.Trim()

    $initialState = Get-ResolvedState -Name "isolation"
    Assert-IsolationDiscovery $initialState
    Assert-BetaAbsent -State $initialState -Stage "initial"
    $script:IsolationVerified = $true
    Pass "OpenCode discovery stays inside the isolated lab with no beta"

    $provider = (Split-LegModel $script:Spec.m).provider
    if (-not (Test-ProviderCredential -Provider $provider)) { throw "provider '$provider' is absent from opencode auth list" }
    $declared = Get-DeclaredModel -Model $script:Spec.m -Provider $provider
    if (-not $declared) { throw "model $($script:Spec.m) is not declared by provider $provider" }
    if ($script:Spec.v) {
        $variants = if ($declared.variants) { @($declared.variants.PSObject.Properties.Name) } else { @() }
        if ($variants -cnotcontains $script:Spec.v) {
            throw "model $($script:Spec.m) does not declare variant $($script:Spec.v)"
        }
    }
    Pass "provider credential, model, and requested variant are declared"

    if ($DryRun) {
        Write-Host "  skip provider liveness check (dry run)" -ForegroundColor DarkGray
    } else {
        Test-ProviderLive -Spec $script:Spec
        Pass "provider answered before fixture staging"
    }

    Reset-Scratch
    $scratchReady = $true
    Assert-MountsClear
    $script:Generated = New-GeneratedFixture
    Pass "fresh manual beta was generated by buildAll"

    $beforeProject = Get-ResolvedState -Name "before-project"
    Assert-IsolationDiscovery $beforeProject
    Assert-BetaAbsent -State $beforeProject -Stage "project-local"
    Stage-ProjectMount
    Invoke-SmokeLeg -Mount "project-local" -ExpectedReadSuffix ".opencode/skills/beta/BODY.md"
    Remove-ProjectMount
    Reset-Scratch

    $beforeGlobal = Get-ResolvedState -Name "before-global"
    Assert-IsolationDiscovery $beforeGlobal
    Assert-BetaAbsent -State $beforeGlobal -Stage "isolated-global"
    Stage-GlobalMount
    Invoke-SmokeLeg -Mount "isolated-global" -ExpectedReadSuffix ".config/opencode/skills/beta/BODY.md"
    Remove-GlobalMount
    Reset-Scratch

    Write-RunSummary ""
    if ($DryRun) {
        Write-Host "DRY RUN — no model calls were made." -ForegroundColor Green
    } else {
        Write-Host "STUB COMMAND SMOKE DONE" -ForegroundColor Green
    }
} catch {
    $failure = $_.Exception.Message
    if ($script:RunRoot) { Write-RunSummary $failure }
    throw
} finally {
    try { Remove-ProjectMount } catch { Write-Error "project mount cleanup failed: $($_.Exception.Message)" }
    try { Remove-GlobalMount } catch { Write-Error "global mount cleanup failed: $($_.Exception.Message)" }
    if ($scratchReady) {
        try { Reset-Scratch } catch { Write-Error "scratch cleanup failed: $($_.Exception.Message)" }
    }
    foreach ($name in $savedEnvironment.Keys) {
        if ($null -eq $savedEnvironment[$name]) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
        else { Set-Item "Env:$name" $savedEnvironment[$name] }
    }
}
