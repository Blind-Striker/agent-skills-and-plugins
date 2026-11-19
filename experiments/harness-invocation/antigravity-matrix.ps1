[CmdletBinding()]
param(
    [ValidateSet("structural", "behavior", "all")]
    [string] $Mode = "structural",
    [string] $Model = "gemini-3.8-flash-low",
    [ValidateRange(1, 1800)] [int] $TimeoutSeconds = 60,
    [string] $Lab,
    [switch] $DryRun
)

. "$PSScriptRoot\common.ps1"
$ErrorActionPreference = "Stop"

$fixture = Join-Path $PSScriptRoot "fixtures\antigravity-probe-plugin"
$names = @("deniz-coordination", "deniz-dotnet-akka", "deniz-dotnet-aspire", "deniz-dotnet-general", "deniz-process")
$estate = @($names | ForEach-Object { Join-Path $script:RepoRoot "antigravity\$_" })
$discoveryPrompt = "Without using tools or reading files, list every skill name supplied to you for this session, and every available subagent type named in the invoke_subagent tool definition. Separate skills and subagents. Do not guess or omit names."
$behavior = @(
    @{ name = "fixture-discovery"; plugins = @($fixture); prompt = $discoveryPrompt }
    @{ name = "qualified-manual"; plugins = @($fixture); prompt = "/probe-plugin:probe-manual" }
    @{ name = "bare-manual"; plugins = @($fixture); prompt = "/probe-manual" }
    @{ name = "estate-discovery"; plugins = $estate; prompt = $discoveryPrompt }
)
$structural = @($fixture) + $estate
if ($DryRun) {
    [ordered]@{
        mode = $Mode; model = $Model; timeoutSeconds = $TimeoutSeconds
        structural = @(if ($Mode -in @("structural", "all")) { $structural })
        behavior = @(if ($Mode -in @("behavior", "all")) { $behavior })
    } | ConvertTo-Json -Depth 8
    return
}

$agy = (Get-Command agy -CommandType Application -ErrorAction Stop).Source
$profileRoot = [Environment]::GetFolderPath("UserProfile")
$manifest = Join-Path $profileRoot ".gemini\config\plugins.json"
$installedPlugins = Join-Path $profileRoot ".gemini\config\plugins"
if (-not $Lab) { $Lab = Join-Path (Split-Path -Qualifier $script:RepoRoot) "antigravity-probe-lab" }
$labRoot = [IO.Path]::GetFullPath($Lab)
$trim = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
foreach ($forbidden in @($script:RepoRoot, $profileRoot)) {
    $parent = [IO.Path]::GetFullPath($forbidden).TrimEnd($trim) + [IO.Path]::DirectorySeparatorChar
    if (($labRoot.TrimEnd($trim) + [IO.Path]::DirectorySeparatorChar).StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "lab must be outside the repository and user profile"
    }
}
$runRoot = Join-Path $labRoot ("agy-run-" + [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
$project = Join-Path $runRoot "project"
New-Item -ItemType Directory -Path $project -Force | Out-Null
$results = [ordered]@{ mode = $Mode; model = $Model; timeoutSeconds = $TimeoutSeconds; structural = @(); behavior = @() }

function Invoke-Agy {
    param([string] $Name, [string[]] $Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $agy
    $start.WorkingDirectory = $project
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $started = $false
    try {
        if (-not $process.Start()) { throw "could not start agy for $Name" }
        $started = $true
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        if (-not [Threading.Tasks.Task]::WaitAll(@($stdout, $stderr), 5000)) { throw "stream cleanup timed out for $Name" }
        return [ordered]@{
            name = $Name; exitCode = if ($timedOut) { $null } else { $process.ExitCode }
            timedOut = $timedOut; stdout = $stdout.Result; stderr = $stderr.Result
        }
    } finally {
        if ($started -and -not $process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(5000) }
        $process.Dispose()
    }
}

function Get-Counts {
    param([string] $Root)
    $skillsRoot = Join-Path $Root "skills"
    $skillFiles = if (Test-Path -LiteralPath $skillsRoot) {
        @(Get-ChildItem -LiteralPath $skillsRoot -Directory | ForEach-Object { Join-Path $_.FullName "SKILL.md" } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    } else { @() }
    $manual = @($skillFiles | Where-Object {
        $text = [IO.File]::ReadAllText($_)
        $text -match '(?s)\A---\s*\r?\n(.*?)\r?\n---' -and $Matches[1] -match '(?m)^disable-model-invocation:[ \t]*true\s*$'
    }).Count
    $continuations = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Filter "SKILL-CONTINUATION.md").Count
    $agentRoot = Join-Path $Root "agents"
    $agents = if (Test-Path -LiteralPath $agentRoot) { @(Get-ChildItem -LiteralPath $agentRoot -File -Filter "*.md").Count } else { 0 }
    return [ordered]@{ skills = $skillFiles.Count; manualSkills = $manual; continuations = $continuations; agents = $agents }
}

function Invoke-BehaviorProbe {
    param([hashtable] $Probe)
    $result = Invoke-Agy -Name $Probe.name -Arguments @("--model", $Model, "--output-format", "text", "--print=$($Probe.prompt)")
    $result.status = if ($result.timedOut) { "timeout" } elseif ($result.exitCode -ne 0) { "invalid" } else { "pass" }
    if ($result.status -eq "pass") {
        $output = $result.stdout
        $checks = switch ($Probe.name) {
            "fixture-discovery" { [ordered]@{ autoVisible = $output -match '\bprobe-auto\b'; manualHidden = $output -notmatch '\bprobe-manual\b'; specialistVisible = $output -match '\bprobe-specialist\b' } }
            "qualified-manual" { [ordered]@{ canary = $output -cmatch '\bAGY-MANUAL-CANARY\b' } }
            "bare-manual" { [ordered]@{ canary = $output -cmatch '\bAGY-MANUAL-CANARY\b' } }
            "estate-discovery" { [ordered]@{ skillVisible = $output -match '\bbrainstorming\b'; manualHidden = $output -notmatch '\bverified-waves\b'; roslynVisible = $output -match '\broslyn-incremental-generator-specialist\b'; akkaVisible = $output -match '\bakka-net-specialist\b' } }
        }
        $result.checks = $checks
        if (@($checks.Values | Where-Object { -not $_ }).Count -gt 0) { $result.status = "fail" }
    }
    $script:results.behavior += $result
}

$failure = $null
try {
    $version = Invoke-Agy -Name "version" -Arguments @("--version")
    if ($version.timedOut -or $version.exitCode -ne 0) { throw "agy --version failed" }
    $results.version = $version.stdout.Trim()
    if ($Mode -in @("structural", "all")) {
        foreach ($path in $structural) {
            if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "plugin folder missing: $path" }
            $row = Invoke-Agy -Name (Split-Path $path -Leaf) -Arguments @("plugin", "validate", "--strict", "--json", $path)
            $row.counts = Get-Counts $path
            $row.status = if ($row.timedOut) { "timeout" } elseif ($row.exitCode -ne 0) { "fail" } else { "pass" }
            try { $null = $row.stdout | ConvertFrom-Json } catch { $row.status = "invalid" }
            $results.structural += $row
        }
    }
    if ($Mode -in @("behavior", "all")) {
        if (-not (Test-Path -LiteralPath (Split-Path $manifest -Parent) -PathType Container)) { throw "Antigravity config directory is absent" }
        $results.installedPluginNames = @(if (Test-Path -LiteralPath $installedPlugins -PathType Container) { Get-ChildItem -LiteralPath $installedPlugins -Directory | ForEach-Object Name })
        $before = if (Test-Path -LiteralPath $manifest -PathType Leaf) { (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash } else { "absent" }
        # agy 1.5.13 ignores GEMINI_HOME. This temporarily borrows the real manifest;
        # never install/copy plugins into the profile. Do not run concurrently with other agy sessions.
        try {
            Invoke-WithAntigravityManifest -Path $manifest -PluginPaths @($fixture) -Body {
                foreach ($probe in $behavior[0..2]) { Invoke-BehaviorProbe $probe }
                Invoke-WithAntigravityManifest -Path $manifest -PluginPaths $estate -Body { Invoke-BehaviorProbe $behavior[3] }
            }
        } finally {
            $after = if (Test-Path -LiteralPath $manifest -PathType Leaf) { (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash } else { "absent" }
            $results.manifestRestored = $before -ceq $after
            if (-not $results.manifestRestored) { throw "plugins.json did not return to its original bytes" }
        }
    }
} catch { $failure = $_.Exception.Message; $results.error = $failure }
finally {
    $results | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runRoot "antigravity-probe-record.json") -Encoding utf8
}

$summary = [ordered]@{
    version = $results.version; mode = $Mode; model = $Model; rawRecord = Join-Path $runRoot "antigravity-probe-record.json"
    structural = @($results.structural | ForEach-Object { @{ name = $_.name; status = $_.status; counts = $_.counts } })
    behavior = @($results.behavior | ForEach-Object { @{ name = $_.name; status = $_.status; checks = $_.checks; exitCode = $_.exitCode } })
    manifestRestored = $results.manifestRestored; installedPluginNames = $results.installedPluginNames; error = $failure
}
$summary | ConvertTo-Json -Depth 8
if ($failure -or @($results.structural + $results.behavior | Where-Object { $_.status -ne "pass" }).Count) { exit 1 }
