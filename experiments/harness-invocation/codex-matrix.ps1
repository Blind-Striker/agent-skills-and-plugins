[CmdletBinding()]
param(
    [switch] $DryRun,
    [switch] $Behavioural,
    [switch] $GeneratedPlugins,
    [string] $CodexHome,
    [string] $Out,
    [string] $Model = "gpt-5.6-luna",
    [ValidateSet("minimal", "low", "medium", "high", "xhigh")]
    [string] $ReasoningEffort = "low",
    [ValidateRange(1, 20)]
    [int] $Repeats = 3,
    [ValidateRange(10, 1800)]
    [int] $TimeoutSeconds = 120
)

. "$PSScriptRoot\common.ps1"

function Test-PathInside {
    param([string] $Child, [string] $Parent)
    $trim = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $separator = [IO.Path]::DirectorySeparatorChar
    $childFull = [IO.Path]::GetFullPath($Child).TrimEnd($trim) + $separator
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd($trim) + $separator
    return $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Get-FileSnapshot {
    param([string[]] $Roots)
    $rows = @()
    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $item = Get-Item -LiteralPath $root -Force
        $files = if ($item.PSIsContainer) {
            @(Get-ChildItem -LiteralPath $root -File -Recurse -Force)
        } else {
            @($item)
        }
        foreach ($file in $files) {
            $rows += "$($file.FullName)|$((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash)"
        }
    }
    return @($rows | Sort-Object)
}

function Invoke-CodexProcess {
    param(
        [string] $Name,
        [string[]] $Arguments,
        [string] $InputText = ""
    )

    if ($DryRun) {
        Write-Host "DRY RUN codex $($Arguments -join ' ')" -ForegroundColor Cyan
        return [ordered]@{
            name = $Name
            arguments = $Arguments
            exitCode = 0
            timedOut = $false
            stdout = ""
            stderr = ""
        }
    }

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $script:CodexCommand
    $start.WorkingDirectory = $script:ProjectRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    $start.CreateNoWindow = $true
    $start.Environment["CODEX_HOME"] = $script:CodexHomeRoot
    foreach ($argument in $script:CodexPrefixArguments) { [void]$start.ArgumentList.Add($argument) }
    foreach ($argument in $Arguments) { [void]$start.ArgumentList.Add($argument) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "could not start Codex for $Name" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if ($InputText) { $process.StandardInput.Write($InputText) }
    $process.StandardInput.Close()

    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) {
        try { $process.Kill($true) } catch { }
        [void]$process.WaitForExit(5000)
    }
    [void][Threading.Tasks.Task]::WaitAll(@($stdoutTask, $stderrTask), 5000)
    $result = [ordered]@{
        name = $Name
        arguments = $Arguments
        exitCode = if ($timedOut) { $null } else { $process.ExitCode }
        timedOut = $timedOut
        stdout = $stdoutTask.Result
        stderr = $stderrTask.Result
    }
    $process.Dispose()
    return $result
}

function Invoke-CodexJson {
    param([string] $Name, [string[]] $Arguments)
    $result = Invoke-CodexProcess -Name $Name -Arguments ($Arguments + "--json")
    $script:CommandResults += $result
    if ($result.timedOut) { throw "$Name timed out" }
    if ($result.exitCode -ne 0) { throw "$Name failed with exit $($result.exitCode): $($result.stderr.Trim())" }
    if ($DryRun) { return $null }
    try { return ($result.stdout | ConvertFrom-Json) }
    catch { throw "$Name did not return JSON: $($result.stdout)" }
}

function Invoke-CodexBehaviour {
    param([string] $Name, [string] $Prompt)
    $pluginCache = Join-Path $script:CodexHomeRoot "plugins\cache"
    $args = @(
        # CODEX_HOME is already a disposable isolated profile. Do not add
        # --ignore-user-config here: Codex 0.153.4 also suppresses the plugin
        # registry under that flag, so installed skills become undiscoverable.
        "exec", "--json", "--ephemeral", "--ignore-rules",
        "--model", $Model, "--config", "model_reasoning_effort=`"$ReasoningEffort`"",
        # Installed skill bodies and bundled references live outside ProjectRoot.
        # Give Codex reviewed workspace access only to the disposable project and
        # isolated plugin cache; this avoids machine-wide unsandboxed execution.
        "--skip-git-repo-check", "--approve-for-me", "--add-dir", $pluginCache,
        "--cd", $script:ProjectRoot, "-"
    )
    $result = Invoke-CodexProcess -Name $Name -Arguments $args -InputText $Prompt
    $result.agentText = Get-CodexAgentText -JsonLines $result.stdout
    $script:CommandResults += $result
    return $result
}

function Get-CodexAgentText {
    param([string] $JsonLines)
    $text = @()
    foreach ($line in @($JsonLines -split "`r?`n")) {
        if (-not $line.Trim()) { continue }
        try { $event = $line | ConvertFrom-Json } catch { continue }
        if ($event.type -ceq "item.completed" -and $event.item.type -ceq "agent_message") {
            $text += [string]$event.item.text
        } elseif ($event.type -ceq "response.output_text.done") {
            $text += [string]$event.text
        }
    }
    return ($text -join "`n")
}

function Assert-Probe {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
    Write-Host "  ok   $Message" -ForegroundColor DarkGray
}

$lab = Get-LabRoot
$realProfile = [Environment]::GetFolderPath("UserProfile")
$repoRoot = [IO.Path]::GetFullPath($script:RepoRoot)
$script:ProjectRoot = Join-Path $lab "project"
$fixtureMarketplace = Join-Path $PSScriptRoot "fixtures\codex-marketplace"
$generatedMarketplace = $repoRoot
$codexShim = Get-Command codex -ErrorAction Stop
$script:CodexPrefixArguments = @()
if ($codexShim.CommandType -eq "ExternalScript" -or $codexShim.Source.EndsWith(".cmd", [StringComparison]::OrdinalIgnoreCase)) {
    $shimRoot = Split-Path $codexShim.Source -Parent
    $bundledNode = Join-Path $shimRoot "node.exe"
    $script:CodexCommand = if (Test-Path -LiteralPath $bundledNode -PathType Leaf) {
        $bundledNode
    } else {
        (Get-Command node -CommandType Application -ErrorAction Stop).Source
    }
    $codexEntry = Join-Path $shimRoot "node_modules\@openai\codex\bin\codex.js"
    if (-not (Test-Path -LiteralPath $codexEntry -PathType Leaf)) { throw "could not resolve the Codex CLI entrypoint" }
    $script:CodexPrefixArguments = @($codexEntry)
} else {
    $script:CodexCommand = $codexShim.Source
}

if (-not (Test-Path -LiteralPath $script:ProjectRoot -PathType Container)) {
    throw "external lab project is missing: $($script:ProjectRoot)"
}
if (-not (Test-Path -LiteralPath (Join-Path $fixtureMarketplace ".agents\plugins\marketplace.json") -PathType Leaf)) {
    throw "Codex fixture marketplace is missing"
}
if ($GeneratedPlugins -and -not (Test-Path -LiteralPath (Join-Path $generatedMarketplace ".agents\plugins\marketplace.json") -PathType Leaf)) {
    throw "generated Codex marketplace is missing; run npm run build"
}

if ($CodexHome) {
    $script:CodexHomeRoot = [IO.Path]::GetFullPath($CodexHome)
    if (-not (Test-Path -LiteralPath $script:CodexHomeRoot -PathType Container)) {
        throw "-CodexHome must name an already-created directory"
    }
} else {
    $runName = "codex-run-" + [DateTime]::UtcNow.ToString("yyyyMMddTHHmmssZ") + "-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
    $script:CodexHomeRoot = Join-Path $lab $runName
    if (-not $DryRun) { New-Item -ItemType Directory -Path $script:CodexHomeRoot | Out-Null }
}

if (-not (Test-PathInside -Child $script:CodexHomeRoot -Parent $lab)) {
    throw "CODEX_HOME escaped the external lab"
}
if (Test-PathInside -Child $script:CodexHomeRoot -Parent $repoRoot) {
    throw "CODEX_HOME is inside the repository"
}
if (Test-PathInside -Child $script:CodexHomeRoot -Parent $realProfile) {
    throw "CODEX_HOME is inside the real user profile"
}

if (-not $Out) { $Out = Join-Path $script:CodexHomeRoot "codex-probe-record.json" }
$outFull = [IO.Path]::GetFullPath($Out)
if (-not (Test-PathInside -Child $outFull -Parent $lab)) { throw "-Out escaped the external lab" }
if (-not $DryRun -and (Test-Path -LiteralPath $outFull)) { throw "output already exists: $outFull" }

$realCodex = Join-Path $realProfile ".codex"
$realStatePaths = @(
    (Join-Path $realCodex "config.toml"),
    (Join-Path $realCodex "plugins\installed_plugins.json"),
    (Join-Path $realCodex "plugins\marketplaces.json"),
    (Join-Path $realCodex "plugins\cache")
)
$realBefore = Get-FileSnapshot -Roots $realStatePaths
$repoBefore = @(git -C $repoRoot status --porcelain=v1 --untracked-files=all)
$script:CommandResults = @()
$assertions = [ordered]@{}
$failure = $null

try {
    $versionResult = Invoke-CodexProcess -Name "version" -Arguments @("--version")
    $script:CommandResults += $versionResult
    if (-not $DryRun -and $versionResult.exitCode -ne 0) { throw "codex --version failed" }
    $codexVersion = if ($DryRun) { "dry-run" } else { $versionResult.stdout.Trim() }

    $initial = Invoke-CodexJson -Name "initial-marketplaces" -Arguments @("plugin", "marketplace", "list")
    if (-not $DryRun) {
        Assert-Probe (@($initial.marketplaces).Count -eq 0) "fresh CODEX_HOME has no marketplaces"
        $assertions.initialMarketplaceCount = @($initial.marketplaces).Count
    }

    [void](Invoke-CodexJson -Name "add-fixture-marketplace" -Arguments @("plugin", "marketplace", "add", $fixtureMarketplace))
    $marketplaces = Invoke-CodexJson -Name "list-marketplaces" -Arguments @("plugin", "marketplace", "list")
    $available = Invoke-CodexJson -Name "list-available-fixture" -Arguments @("plugin", "list", "--available")
    if (-not $DryRun) {
        # Codex may expose OpenAI's built-in remote catalog even when an isolated
        # CODEX_HOME has no user-configured marketplaces. Scope fixture assertions
        # to the marketplace under test instead of assuming a globally empty
        # `available` collection.
        $availableIds = @(
            $available.available |
                Where-Object { $_.marketplaceName -ceq "codex-probe-marketplace" } |
                ForEach-Object { $_.pluginId } |
                Sort-Object
        )
        Assert-Probe ($availableIds -join "," -ceq "codex-negative-control@codex-probe-marketplace,codex-probe@codex-probe-marketplace") "fixture exposes both available plugins"
        Assert-Probe (@($marketplaces.marketplaces).Count -eq 1) "only the fixture marketplace is configured"
        $assertions.fixtureAvailable = $availableIds
    }

    [void](Invoke-CodexJson -Name "install-fixture-plugin" -Arguments @("plugin", "add", "codex-probe@codex-probe-marketplace"))
    $installed = Invoke-CodexJson -Name "list-installed-fixture" -Arguments @("plugin", "list")
    if (-not $DryRun) {
        $installedIds = @(
            $installed.installed |
                Where-Object { $_.marketplaceName -ceq "codex-probe-marketplace" } |
                ForEach-Object { $_.pluginId }
        )
        Assert-Probe ($installedIds.Count -eq 1 -and $installedIds[0] -ceq "codex-probe@codex-probe-marketplace") "only the positive fixture plugin is installed"
        Assert-Probe (-not ($installedIds -contains "codex-negative-control@codex-probe-marketplace")) "negative-control plugin remains uninstalled"
        $assertions.fixtureInstalled = $installedIds
    }

    if ($GeneratedPlugins) {
        [void](Invoke-CodexJson -Name "add-generated-marketplace" -Arguments @("plugin", "marketplace", "add", $generatedMarketplace))
        $generatedAvailable = Invoke-CodexJson -Name "list-generated-available" -Arguments @("plugin", "list", "--available", "--marketplace", "deniz-skills")
        $expectedGenerated = @("deniz-dotnet-akka", "deniz-dotnet-aspire", "deniz-dotnet-general", "deniz-process")
        if (-not $DryRun) {
            $actualGenerated = @($generatedAvailable.available | ForEach-Object { $_.name } | Sort-Object)
            Assert-Probe ($actualGenerated.Count -eq 4 -and ($actualGenerated -join ",") -ceq ($expectedGenerated -join ",")) "all four generated plugins are available"
            $assertions.generatedAvailable = $actualGenerated
        }
        foreach ($plugin in $expectedGenerated) {
            [void](Invoke-CodexJson -Name "install-generated-$plugin" -Arguments @("plugin", "add", "$plugin@deniz-skills"))
        }
        $generatedInstalled = Invoke-CodexJson -Name "list-generated-installed" -Arguments @("plugin", "list", "--marketplace", "deniz-skills")
        if (-not $DryRun) {
            $actualInstalled = @($generatedInstalled.installed | ForEach-Object { $_.name } | Sort-Object)
            Assert-Probe ($actualInstalled.Count -eq 4 -and ($actualInstalled -join ",") -ceq ($expectedGenerated -join ",")) "all four generated plugins install in isolation"
            $cacheRoot = Join-Path $script:CodexHomeRoot "plugins\cache\deniz-skills"
            $skillCount = @(Get-ChildItem -LiteralPath $cacheRoot -Filter SKILL.md -File -Recurse).Count
            $manualPolicyCount = @(Get-ChildItem -LiteralPath $cacheRoot -Filter openai.yaml -File -Recurse).Count
            Assert-Probe ($skillCount -gt 0) "generated plugin cache contains native skills"
            $assertions.generatedInstalled = $actualInstalled
            $assertions.generatedSkillCount = $skillCount
            $assertions.generatedManualPolicyCount = $manualPolicyCount
        }
    }

    if ($Behavioural) {
        $credentialPresent = [bool][Environment]::GetEnvironmentVariable("OPENAI_API_KEY") -or
            (Test-Path -LiteralPath (Join-Path $script:CodexHomeRoot "auth.json") -PathType Leaf)
        if (-not $DryRun -and -not $credentialPresent) { throw "behavioural probe requested but isolated CODEX_HOME has no auth.json and OPENAI_API_KEY is absent" }

        $live = Invoke-CodexBehaviour -Name "liveness" -Prompt "Reply exactly LIVE."
        if (-not $DryRun -and ($live.timedOut -or $live.exitCode -ne 0 -or -not $live.agentText.Contains("LIVE"))) {
            throw "one-token liveness preflight failed"
        }
        $explicitAuto = Invoke-CodexBehaviour -Name "explicit-auto" -Prompt 'Use $codex-probe:codex-auto-zebra.'
        $explicitManual = Invoke-CodexBehaviour -Name "explicit-manual" -Prompt 'Use $codex-probe:codex-manual-otter.'
        $explicitBoth = Invoke-CodexBehaviour -Name "explicit-both" -Prompt 'Use $codex-probe:codex-both-lemur.'
        $crossSkill = Invoke-CodexBehaviour -Name "cross-skill-handoff" -Prompt 'Use $codex-probe:codex-handoff-ibis.'
        $bundledReference = Invoke-CodexBehaviour -Name "bundled-reference" -Prompt 'Use $codex-probe:codex-reference-fox.'
        if (-not $DryRun) {
            Assert-Probe ($explicitAuto.exitCode -eq 0 -and $explicitAuto.agentText.Contains("CODEX-AUTO-RAN")) "ordinary skill is explicitly addressable"
            Assert-Probe ($explicitManual.exitCode -eq 0 -and $explicitManual.agentText.Contains("CODEX-MANUAL-RAN")) "manual skill is explicitly addressable"
            Assert-Probe ($explicitBoth.exitCode -eq 0 -and $explicitBoth.agentText.Contains("CODEX-BOTH-RAN")) "both-surface skill is explicitly addressable"
            Assert-Probe ($crossSkill.exitCode -eq 0 -and $crossSkill.agentText.Contains("CODEX-AUTO-RAN")) "one installed skill can hand off to another"
            Assert-Probe ($bundledReference.exitCode -eq 0 -and $bundledReference.agentText.Contains("CODEX-REFERENCE-RAN")) "an installed skill can load a bundled reference"
        }

        $manualImplicit = @()
        $autoImplicit = @()
        $bothImplicit = @()
        for ($i = 1; $i -le $Repeats; $i++) {
            $manualImplicit += Invoke-CodexBehaviour -Name "implicit-manual-$i" -Prompt "OTTER-FLAGGED-MANUAL. Follow any applicable installed skill."
            $autoImplicit += Invoke-CodexBehaviour -Name "implicit-auto-$i" -Prompt "ZEBRA-FLAGGED-AUTO. Follow any applicable installed skill."
            $bothImplicit += Invoke-CodexBehaviour -Name "implicit-both-$i" -Prompt "LEMUR-FLAGGED-BOTH. Follow any applicable installed skill."
        }
        $manualImplicitMarkerCount = @($manualImplicit | Where-Object { $_.agentText.Contains("CODEX-MANUAL-RAN") }).Count
        $autoImplicitMarkerCount = @($autoImplicit | Where-Object { $_.agentText.Contains("CODEX-AUTO-RAN") }).Count
        $bothImplicitMarkerCount = @($bothImplicit | Where-Object { $_.agentText.Contains("CODEX-BOTH-RAN") }).Count
        if (-not $DryRun) {
            Assert-Probe ($manualImplicitMarkerCount -eq 0) "manual policy suppresses implicit invocation"
            Assert-Probe ($autoImplicitMarkerCount -gt 0) "auto skill is selected implicitly in at least one repeated probe"
            Assert-Probe ($bothImplicitMarkerCount -gt 0) "both-surface skill is selected implicitly in at least one repeated probe"
        }
        $assertions.manualImplicitMarkerCount = $manualImplicitMarkerCount
        $assertions.autoImplicitMarkerCount = $autoImplicitMarkerCount
        $assertions.bothImplicitMarkerCount = $bothImplicitMarkerCount
        $assertions.behaviourRepeats = $Repeats

        $uninstalledNegative = Invoke-CodexBehaviour -Name "uninstalled-negative" -Prompt "WALRUS-FLAGGED-NEGATIVE. Follow any applicable installed skill."
        if (-not $DryRun) {
            Assert-Probe (-not $uninstalledNegative.agentText.Contains("CODEX-NEGATIVE-LEAK")) "an uninstalled plugin skill does not leak into discovery"
        }

        if ($GeneratedPlugins) {
            $generatedExplicit = Invoke-CodexBehaviour -Name "generated-explicit-manual" -Prompt 'Use $deniz-process:using-superpowers. According to that skill, reply only with the exact sentence template it says to announce before following a skill.'
            $generatedImplicit = Invoke-CodexBehaviour -Name "generated-implicit-large-catalog" -Prompt "Review a .NET SIMD reduction whose tail overlaps a previously processed vector and whose operation is non-idempotent. Follow any applicable installed skill. Reply only with the API name the applicable skill says must replace repeated lanes with the operation's identity."
            if (-not $DryRun) {
                Assert-Probe ($generatedExplicit.exitCode -eq 0 -and $generatedExplicit.agentText.Contains("Using [skill] to [purpose]")) "a generated manual skill executes from the installed cache"
                Assert-Probe ($generatedImplicit.exitCode -eq 0 -and $generatedImplicit.agentText.Contains("ConditionalSelect")) "a generated auto skill is discoverable in the full catalog"
            }
        }
    }

    [void](Invoke-CodexJson -Name "remove-fixture-plugin" -Arguments @("plugin", "remove", "codex-probe@codex-probe-marketplace"))
    $removed = Invoke-CodexJson -Name "list-after-fixture-removal" -Arguments @("plugin", "list")
    if (-not $DryRun) {
        $remainingIds = @(
            $removed.installed |
                Where-Object { $_.marketplaceName -ceq "codex-probe-marketplace" } |
                ForEach-Object { $_.pluginId }
        )
        Assert-Probe (-not ($remainingIds -contains "codex-probe@codex-probe-marketplace")) "fixture removal clears the positive fixture plugin"
        Assert-Probe (-not ($remainingIds -contains "codex-negative-control@codex-probe-marketplace")) "negative-control plugin remains uninstalled"
        $assertions.fixtureInstalledAfterRemove = @($remainingIds | Where-Object { $_ -like "codex-probe@*" }).Count
    }
} catch {
    $failure = $_.Exception.Message
} finally {
    if (-not $DryRun) {
        $realAfter = Get-FileSnapshot -Roots $realStatePaths
        $repoAfter = @(git -C $repoRoot status --porcelain=v1 --untracked-files=all)
        $realDelta = @(Compare-Object -ReferenceObject $realBefore -DifferenceObject $realAfter)
        $repoDelta = @(Compare-Object -ReferenceObject $repoBefore -DifferenceObject $repoAfter)
        $assertions.realCodexPluginStateUnchanged = $realDelta.Count -eq 0
        $assertions.repositoryStateUnchanged = $repoDelta.Count -eq 0
        if ($realDelta.Count -ne 0 -and -not $failure) { $failure = "real Codex plugin state changed" }
        if ($repoDelta.Count -ne 0 -and -not $failure) { $failure = "repository state changed during probe" }

        $record = [ordered]@{
            schemaVersion = 1
            timestampUtc = [DateTime]::UtcNow.ToString("o")
            repoHead = (git -C $repoRoot rev-parse HEAD).Trim()
            codexVersion = $codexVersion
            model = $Model
            reasoningEffort = $ReasoningEffort
            codexHome = "<LAB>/$([IO.Path]::GetFileName($script:CodexHomeRoot))"
            behaviouralRequested = [bool]$Behavioural
            generatedPluginsRequested = [bool]$GeneratedPlugins
            assertions = $assertions
            commands = $script:CommandResults
            failure = $failure
        }
        $parent = Split-Path $outFull -Parent
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outFull -Encoding utf8NoBOM
        Write-Host "Raw record: $outFull" -ForegroundColor DarkGray
    }
}

if ($DryRun) {
    Write-Host "DRY RUN complete; no Codex process changed state." -ForegroundColor Green
    exit 0
}
if ($failure) { throw $failure }
Write-Host "Codex structural probe passed." -ForegroundColor Green
