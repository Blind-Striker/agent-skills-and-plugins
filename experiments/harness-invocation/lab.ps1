. "$PSScriptRoot\common.ps1"
# Harness probe lab — shared environment.
# Dot-source this in a DEDICATED terminal:  . .\experiments\harness-invocation\lab.ps1
# It rewrites this shell's environment. Close the window when you are done.

$LAB  = Get-LabRoot
$REPO = $script:RepoRoot
function Use-ClaudeLab {
    <#  Isolated Claude Code.
        CLAUDE_CONFIG_DIR *replaces* the config root (measured), so a fresh dir means
        no installed plugins, no user settings, no MCP. Credentials are the one thing
        copied across, or the session would demand a new OAuth login.  #>
    $env:CLAUDE_CONFIG_DIR = "$LAB\.claude-home"
    Set-Location "$LAB\project"
    Write-Host "CLAUDE_CONFIG_DIR = $env:CLAUDE_CONFIG_DIR" -ForegroundColor DarkGray
    Write-Host "cwd               = $(Get-Location)" -ForegroundColor DarkGray
}

function Start-ClaudeLab {
    <#  .EXAMPLE  Start-ClaudeLab
        .EXAMPLE  Start-ClaudeLab deniz-process,deniz-dotnet-general
        .EXAMPLE  Start-ClaudeLab deniz-process -Extra '--model','opus'  #>
    param(
        [string[]] $Plugins = @("deniz-process"),
        [string[]] $Extra   = @()
    )
    Use-ClaudeLab
    # NOT $args — that is an automatic variable in PowerShell.
    $cliArgs = @()
    foreach ($p in $Plugins) { $cliArgs += @("--plugin-dir", "$REPO\plugins\$p") }
    $cliArgs += $Extra
    Write-Host "claude $($cliArgs -join ' ')" -ForegroundColor Cyan
    & claude @cliArgs
}

function Use-OpenCodeLab {
    <#  Isolated OpenCode 2. OPENCODE_CONFIG_DIR replaces the global config root, so the lab
        sets it, home, all four XDG roots and the database below $LAB\.opencode-home, turns the
        project walk off, and disables the managed background service in the lab's service.json
        (see Use-OpenCodeIsolation in common.ps1). Sync-Lab uses installer composition into
        OPENCODE_CONFIG_DIR; nothing mounts a built tree.  #>
    Use-OpenCodeIsolation
    Set-Location "$LAB\project"
    foreach ($name in "HOME", "OPENCODE_TEST_HOME", "OPENCODE_CONFIG_DIR", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME", "OPENCODE_DB", "OPENCODE_DISABLE_PROJECT_CONFIG", "TEMP") {
        Write-Host ("{0,-31} = {1}" -f $name, [Environment]::GetEnvironmentVariable($name)) -ForegroundColor DarkGray
    }
    Write-Host ("{0,-31} = {1}" -f "cwd", (Get-Location)) -ForegroundColor DarkGray
}

function Start-OpenCodeLab {
    <#  The TUI or any CLI command, inside the isolation. With the service disabled, the CLI starts
        its own private server rather than the long-lived service.  #>
    param([string[]] $Extra = @())
    Use-OpenCodeLab
    Write-Host "opencode $($Extra -join ' ')" -ForegroundColor Cyan
    & opencode @Extra
}

function Sync-Lab {
    <#  Reconcile every built Module into the lab's OPENCODE_CONFIG_DIR after `npm run build`.
        The Claude side needs no sync: --plugin-dir points straight at the repo. Discovery is
        measured separately, against an isolated serve: oc2-discovery.ps1.  #>
    Use-OpenCodeLab
    & node "$REPO\tools\install-opencode.ts" install --all --yes
    if ($LASTEXITCODE -ne 0) { throw "OpenCode installer failed: $LASTEXITCODE" }

    $dest = $env:OPENCODE_CONFIG_DIR
    $s = if (Test-Path "$dest\skills") { @(Get-ChildItem "$dest\skills" -Directory).Count } else { 0 }
    $c = if (Test-Path "$dest\commands") { @(Get-ChildItem "$dest\commands" -File).Count } else { 0 }
    $a = if (Test-Path "$dest\agents") { @(Get-ChildItem "$dest\agents" -File).Count } else { 0 }
    Write-Host "synced: $s skill dirs, $c commands, $a agents" -ForegroundColor Green
}

Write-Host ""
Write-Host "Harness probe lab loaded." -ForegroundColor Green
Write-Host "  Start-ClaudeLab [-Plugins deniz-process,...] [-Extra '--model','opus']"
Write-Host "  Start-OpenCodeLab"
Write-Host "  Sync-Lab            # after npm run build"
Write-Host "  & $REPO\experiments\harness-invocation\verify.ps1   # prove the Claude isolation"
Write-Host "  & $REPO\experiments\harness-invocation\oc2-discovery.ps1 -Lab <dir>   # OpenCode discovery"
Write-Host ""
