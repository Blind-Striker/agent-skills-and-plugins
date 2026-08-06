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
    <#  Isolated OpenCode. Harder than Claude: OPENCODE_CONFIG_DIR only ADDS a search
        location, so the real global config and package cache would still load. The
        isolation is a relocated HOME + XDG roots, with the built tree mounted as the
        global config. No `plugin:` key anywhere -> no package cache to outrank us.  #>
    $env:USERPROFILE                        = "$LAB\.opencode-home"
    $env:HOME                               = "$LAB\.opencode-home"
    $env:XDG_CONFIG_HOME                    = "$LAB\.opencode-home\.config"
    $env:XDG_DATA_HOME                      = "$LAB\.opencode-home\.local\share"
    $env:OPENCODE_DISABLE_CLAUDE_CODE_SKILLS = "1"   # explicit: the machine profile sets it too
    Remove-Item Env:OPENCODE_CONFIG_DIR -ErrorAction SilentlyContinue
    Set-Location "$LAB\project"
    Write-Host "USERPROFILE / HOME = $env:USERPROFILE"      -ForegroundColor DarkGray
    Write-Host "XDG_CONFIG_HOME    = $env:XDG_CONFIG_HOME"  -ForegroundColor DarkGray
    Write-Host "XDG_DATA_HOME      = $env:XDG_DATA_HOME"    -ForegroundColor DarkGray
    Write-Host "cwd                = $(Get-Location)"       -ForegroundColor DarkGray
}

function Start-OpenCodeLab {
    param([string[]] $Extra = @())
    Use-OpenCodeLab
    Write-Host "opencode $($Extra -join ' ')" -ForegroundColor Cyan
    & opencode @Extra
}

function Sync-Lab {
    <#  Re-mount the built OpenCode tree after `npm run build`. The Claude side needs
        no sync: --plugin-dir points straight at the repo.  #>
    $dest = "$LAB\.opencode-home\.config\opencode"
    Remove-Item "$dest\skills","$dest\commands" -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item "$REPO\opencode\skills"   -Destination $dest -Recurse -Force
    Copy-Item "$REPO\opencode\commands" -Destination $dest -Recurse -Force
    $skillDirs = @(Get-ChildItem "$dest\skills" -Directory)
    $s = $skillDirs.Count
    $discoverable = @($skillDirs | Where-Object { Test-Path (Join-Path $_.FullName "SKILL.md") }).Count
    $parked = @($skillDirs | Where-Object { Test-Path (Join-Path $_.FullName "BODY.md") }).Count
    $c = (Get-ChildItem "$dest\commands" -File).Count
    Write-Host "synced: $s skill dirs ($discoverable with SKILL.md + $parked parked), $c commands" -ForegroundColor Green
}

Write-Host ""
Write-Host "Harness probe lab loaded." -ForegroundColor Green
Write-Host "  Start-ClaudeLab [-Plugins deniz-process,...] [-Extra '--model','opus']"
Write-Host "  Start-OpenCodeLab"
Write-Host "  Sync-Lab            # after npm run build"
Write-Host "  & $LAB\verify.ps1   # prove the isolation before trusting a result"
Write-Host ""
