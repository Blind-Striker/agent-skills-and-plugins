# Shared plumbing for every probe script in this directory.
# Dot-source it:  . "$PSScriptRoot\common.ps1"
#
# Paths are DERIVED, never written down. A committed script that names a drive letter is a
# machine-specific path, which AGENTS.md forbids outright, and it also makes the subsystem
# unusable on a second machine.
#
# This file must stay side-effect free at load: it sets no environment variable, changes no
# directory, and does NOT resolve the lab. make-fixture.ps1 dot-sources it and must work with
# no lab present at all.

# Walk up for the repository, do not count directory levels. A fixed "..\.." silently resolves
# to the wrong place the moment this directory is moved or staged somewhere else for review, and
# every caller then proceeds with a RepoRoot that is not one - the same failure Get-LabRoot below
# exists to refuse.
$script:RepoRoot = $(
    $d = $PSScriptRoot
    while ($d) {
        if ((Test-Path (Join-Path $d "AGENTS.md")) -and (Test-Path (Join-Path $d "tools"))) { $d; break }
        $parent = Split-Path $d -Parent
        if ($parent -eq $d) { throw "no repository root above $PSScriptRoot (looked for AGENTS.md + tools/)" }
        $d = $parent
    }
)
$script:HereRoot = $PSScriptRoot

function Get-LabRoot {
    <#  Where the isolated harness homes and the raw transcripts live. NEVER inside the repository,
        and never under the user profile: OpenCode discovery walks up from the working directory
        past any git boundary, so a lab under %TEMP% (which sits below %USERPROFILE%) silently
        collects the real ~/.agents and ~/.claude trees on the way.

        This RESOLVES rather than derives, and throws rather than guessing. An earlier version
        computed a sibling of the repository, which named a directory that does not exist while
        every caller happily proceeded with it. The .opencode-home probe is what makes this a
        resolution: an empty directory of the right name is not a lab.  #>
    $realProfile = [Environment]::GetFolderPath('UserProfile')
    $tried = @()
    if ($env:HARNESS_LAB) { $tried += $env:HARNESS_LAB }
    $tried += "$(Split-Path -Qualifier $script:RepoRoot)\harness-probe-lab"

    $trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $separator = [IO.Path]::DirectorySeparatorChar
    $rootBase = [IO.Path]::GetFullPath($script:RepoRoot).TrimEnd($trimChars)
    $rootFull = $rootBase + $separator
    $profileBase = [IO.Path]::GetFullPath($realProfile).TrimEnd($trimChars)
    $profileFull = $profileBase + $separator
    $attempts = @()

    foreach ($c in $tried) {
        try {
            # GetFullPath normalizes a candidate without requiring that it already exist.
            $full = [IO.Path]::GetFullPath($c)
        } catch {
            $attempts += "'$c' (invalid path: $($_.Exception.Message))"
            continue
        }

        $fullForCompare = $full.TrimEnd($trimChars) + $separator
        if ($fullForCompare.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
            $attempts += "'$full' (refused: repository containment under '$rootBase')"
            continue
        }
        if ($fullForCompare.StartsWith($profileFull, [StringComparison]::OrdinalIgnoreCase)) {
            $attempts += "'$full' (refused: user-profile containment under '$profileBase')"
            continue
        }

        # String concatenation, not Join-Path: Join-Path validates the drive and emits two
        # non-terminating errors for a candidate on a drive that does not exist, which is a
        # perfectly ordinary thing for a candidate to be.
        if (Test-Path "$full\.opencode-home") { return $full }
        $attempts += "'$full' (missing .opencode-home)"
    }
    throw "no lab found. Tried: $($attempts -join '; '). Set `$env:HARNESS_LAB to an external lab outside the repository and user profile."
}

function Use-ClaudeIsolation {
    <#  CLAUDE_CONFIG_DIR *replaces* the config root (measured), so one variable is enough: a fresh
        directory means no installed plugins, no settings, no MCP.

        Sets the environment and nothing else. Relocating the caller's shell is the CALLER's job —
        an interactive session wants Set-Location, a matrix wants Push-Location per run, and a
        shared function that silently relocates is the variants.ps1 defect this file exists to end. #>
    $env:CLAUDE_CONFIG_DIR = Join-Path (Get-LabRoot) ".claude-home"
}

# Inherited variables that would steer an isolated OpenCode 2 process back toward real state: an
# extra config file, inline config, the alternate project-config switch (it is read BEFORE
# OPENCODE_DISABLE_PROJECT_CONFIG, so an inherited value would win), a server password, and the
# OpenCode 1 skill switch, which OpenCode 2 no longer reads and which must not suggest otherwise.
$script:OpenCodeLabClearedVariables = @(
    "OPENCODE_CONFIG", "OPENCODE_CONFIG_CONTENT", "OPENCODE_CONFIG_PROJECT_DISABLE",
    "OPENCODE_PASSWORD", "OPENCODE_SERVER_PASSWORD", "OPENCODE_DISABLE_CLAUDE_CODE_SKILLS"
)

function Get-OpenCodeLabEnvironment {
    <#  The OpenCode 2 isolation for one lab root, as data: every variable to set, all paths below
        Root. OpenCode 2 reads its global config from OPENCODE_CONFIG_DIR, which REPLACES the
        XDG root (measured), and reads ~/.claude/skills and ~/.agents/skills as always-on
        compatibility roots below OPENCODE_TEST_HOME or the OS home. So home, all four XDG roots,
        the config root and the database all move under the lab, as in upstream
        packages/cli/script/service-smoke.ts. OPENCODE_DISABLE_PROJECT_CONFIG=1 skips the project
        walk, which otherwise visits every ancestor of the working directory up to the filesystem
        root. Pure: it creates nothing and sets nothing; Use-OpenCodeIsolation and oc2-discovery.ps1
        apply it.  #>
    param([Parameter(Mandatory)] [string] $Root)
    $base = [IO.Path]::GetFullPath($Root)
    $homeDir = Join-Path $base "home"
    $xdg = Join-Path $base "xdg"
    $temp = Join-Path $base ".tmp"
    return [ordered]@{
        HOME                            = $homeDir
        USERPROFILE                     = $homeDir
        OPENCODE_TEST_HOME              = $homeDir
        XDG_CONFIG_HOME                 = Join-Path $xdg "config"
        XDG_DATA_HOME                   = Join-Path $xdg "data"
        XDG_STATE_HOME                  = Join-Path $xdg "state"
        XDG_CACHE_HOME                  = Join-Path $xdg "cache"
        OPENCODE_CONFIG_DIR             = Join-Path $base "config"
        OPENCODE_DB                     = [IO.Path]::Combine($xdg, "data", "opencode", "opencode.db")
        OPENCODE_DISABLE_PROJECT_CONFIG = "1"
        OPENCODE_DISABLE_AUTOUPDATE     = "1"
        TEMP                            = $temp
        TMP                             = $temp
        TMPDIR                          = $temp
    }
}

function Initialize-OpenCodeLab {
    <#  Create the directories an OpenCode lab environment names, and disable the managed background
        service for every CLI command run inside it. The CLI reads <config root>/service.json, and
        without {"disabled":true} it talks to the long-lived service on the default port, which
        runs with the REAL profile's environment whatever this shell exports.  #>
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Environment)
    foreach ($name in "HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME", "OPENCODE_CONFIG_DIR", "TEMP") {
        New-Item -ItemType Directory -Path $Environment[$name] -Force | Out-Null
    }
    New-Item -ItemType Directory -Path (Split-Path $Environment["OPENCODE_DB"] -Parent) -Force | Out-Null
    Set-Content -Path (Join-Path $Environment["OPENCODE_CONFIG_DIR"] "service.json") -Value '{"disabled":true}' -NoNewline
}

function Use-OpenCodeIsolation {
    <#  Isolated OpenCode 2 in THIS process: the environment from Get-OpenCodeLabEnvironment for
        <lab>/.opencode-home, with its directories and service.json created. The installer composes
        into OPENCODE_CONFIG_DIR, which the CLI and an isolated `opencode serve` both read as the
        global root. Environment only; see Use-ClaudeIsolation on why there is no Set-Location.  #>
    $environment = Get-OpenCodeLabEnvironment -Root (Join-Path (Get-LabRoot) ".opencode-home")
    foreach ($name in $script:OpenCodeLabClearedVariables) { Remove-Item "Env:$name" -ErrorAction SilentlyContinue }
    foreach ($name in $environment.Keys) { Set-Item "Env:$name" $environment[$name] }
    Initialize-OpenCodeLab -Environment $environment
}

function Get-ScratchRepo {
    return (Join-Path (Get-LabRoot) "project\scratch-repo")
}

function Get-FixtureBaseline {
    <#  The commit every probe must start from. Written by make-fixture.ps1 rather than typed, so
        the value cannot drift away from the tree it names.  #>
    $f = Join-Path $script:HereRoot "fixtures\BASELINE"
    if (-not (Test-Path $f)) { throw "no fixtures\BASELINE - run fixtures\make-fixture.ps1 first" }
    return (Get-Content $f -Raw).Trim()
}

function Reset-Scratch {
    <#  `git reset --hard` plus `clean -fdx`, NOT checkout-and-clean. The ceremony under test ends
        with "commit your work" and obeys it, so HEAD itself moves and a later HEAD~1..HEAD means
        something different on every run. One round published a finding that was purely this: the
        function under test had already been written by an earlier probe.  #>
    param([string] $Repo)
    $target = if ($Repo) { $Repo } else { Get-ScratchRepo }
    Push-Location $target
    try {
        git reset --hard (Get-FixtureBaseline) 2>&1 | Out-Null
        git clean -fdxq 2>&1 | Out-Null
    } finally { Pop-Location }
}

$script:PreflightFailures = 0
function Fail($m) { Write-Host "  FAIL $m" -ForegroundColor Red; $script:PreflightFailures++ }
function Pass($m) { Write-Host "  ok   $m" -ForegroundColor DarkGray }
function Assert-Preflight {
    if ($script:PreflightFailures) {
        throw "$($script:PreflightFailures) preflight failure(s) - nothing was run."
    }
}

function Format-Cost {
    <#  Invariant culture. A tr-TR machine writes 0,5267 and every other machine writes 0.5267,
        which makes the cost column unparseable across the two.  #>
    param([double] $Value)
    return [string]::Format([cultureinfo]::InvariantCulture, "{0:F4}", $Value)
}

# The one model table, in ONE structure: provider/model plus the requested variant. It lived in
# matrix.ps1 and again, in a different shape, in variants.ps1 - and the second copy still named two
# legs on a connector that hit its monthly limit mid-round. Readers that want the provider and the
# bare id split it themselves; see Split-LegModel.
$script:LegTable = @{
    sol         = @{ m = "openai/gpt-5.6-sol";                  v = "xhigh" }
    kimi        = @{ m = "moonshotai/kimi-k3";                  v = "max"   }
    grok        = @{ m = "xai/grok-4.5";                        v = "high"  }
    glm         = @{ m = "zai/glm-5.2";                         v = "max"   }
    deepseek    = @{ m = "deepseek/deepseek-v4-pro";            v = "max"   }
    # An empty v omits --variant entirely. Passing an unsupported value to reach the default would
    # rely on the silent drop, which is the trap, not a mechanism.
    "kimi-dflt" = @{ m = "opencode-go/kimi-k3";                 v = ""      }
    "glm-dflt"  = @{ m = "zai/glm-5.2";                         v = ""      }
    opus        = @{ m = "openrouter/anthropic/claude-opus-5";  v = ""      }
}

function Split-LegModel {
    <#  "openrouter/anthropic/claude-opus-5" -> provider "openrouter", id "anthropic/claude-opus-5".
        Only the FIRST segment is the provider; a naive split on "/" loses the rest.  #>
    param([string] $Model)
    $i = $Model.IndexOf("/")
    if ($i -lt 0) { return @{ provider = $Model; id = "" } }
    return @{ provider = $Model.Substring(0, $i); id = $Model.Substring($i + 1) }
}
