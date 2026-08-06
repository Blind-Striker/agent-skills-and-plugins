# Prove the isolation before trusting any observation.
# protocol.md: "A negative needs a positive beside it" — every check below has both.
#   .\verify.ps1          structural + OpenCode discovery (free, deterministic)
#   .\verify.ps1 -Deep    also runs two Claude Code -p calls (costs tokens, ~30s)
param([switch] $Deep)

. "$PSScriptRoot\common.ps1"

$LAB  = Get-LabRoot
$REPO = $script:RepoRoot
$saved = @{}
foreach ($v in "USERPROFILE","HOME","XDG_CONFIG_HOME","XDG_DATA_HOME","OPENCODE_CONFIG_DIR","OPENCODE_DISABLE_CLAUDE_CODE_SKILLS","CLAUDE_CONFIG_DIR") {
    $saved[$v] = [Environment]::GetEnvironmentVariable($v)
}
$fails = 0
function Check($name, $got, $want) {
    $ok = "$got" -eq "$want"
    if (-not $ok) { $script:fails++ }
    $mark  = if ($ok) { "PASS" } else { "FAIL" }
    $color = if ($ok) { "Green" } else { "Red" }
    Write-Host ("  [{0}] {1,-52} got {2}  want {3}" -f $mark, $name, $got, $want) -ForegroundColor $color
}

Write-Host "`n=== OpenCode ===" -ForegroundColor Cyan
Use-OpenCodeIsolation
Push-Location "$LAB\project"

$skills = @((& opencode debug skill 2>&1 | Out-String) | ConvertFrom-Json)
$cfg    = (& opencode debug config 2>&1 | Out-String) | ConvertFrom-Json
$cmds   = @($cfg.command.PSObject.Properties.Name)
$leaked = @($skills | Where-Object { $_.location -ne "<built-in>" -and $_.location -notlike "$LAB*" })

Check "skills discovered (73 ours + 1 built-in)"        $skills.Count 74
Check "commands discovered"                             $cmds.Count   33
Check "built-in control present (customize-opencode)"   (@($skills | Where-Object name -eq "customize-opencode").Count) 1
Check "skills resolving OUTSIDE the lab (must be zero)" $leaked.Count 0
Check "plugin: list empty (no package cache)"           (@($cfg.plugin).Count) 0
Check "parked bundles invisible"                        (@($skills | Where-Object name -in @(
    "convert-to-cpm",
    "dotnet-aot-compat",
    "dotnet-trace-collect",
    "dump-collect",
    "improve-codebase-architecture",
    "migrate-nullable-references",
    "setup-matt-pocock-skills",
    "teach",
    "triage",
    "writing-great-skills",
    "writing-skills"
)).Count) 0
if ($leaked.Count) { $leaked | ForEach-Object { Write-Host ("      LEAK: " + $_.name + " <- " + $_.location) -ForegroundColor Red } }
Pop-Location

Write-Host "`n=== Claude Code ===" -ForegroundColor Cyan
Use-ClaudeIsolation
Check "config dir is the lab"                           (Split-Path $env:CLAUDE_CONFIG_DIR -Leaf) ".claude-home"
Check "no installed-plugin registry in the lab config"  (Test-Path "$LAB\.claude-home\plugins\installed_plugins.json") $false
Check "no user settings.json in the lab config"         (Test-Path "$LAB\.claude-home\settings.json")                 $false
Check "credentials present (no re-login)"               (Test-Path "$LAB\.claude-home\.credentials.json")              $true
Check "project dir carries no CLAUDE.md"                (Test-Path "$LAB\project\CLAUDE.md")                           $false
Check "project dir carries no .claude/"                 (Test-Path "$LAB\project\.claude")                             $false

if ($Deep) {
    Write-Host "  running two -p calls (negative control, then mounted)..." -ForegroundColor DarkGray
    Push-Location "$LAB\project"
    $q = "List the exact name of every Skill available to your Skill tool, one per line, nothing else. If there are none, reply exactly NONE."
    $a = (& claude -p $q 2>&1 | Out-String)
    $b = (& claude --plugin-dir "$REPO\plugins\deniz-process" -p $q 2>&1 | Out-String)
    Pop-Location
    $aLeak = @($a -split "`r?`n" | Where-Object { $_ -match "deniz-|superpowers|dotnet-|aspire|mattpocock" })
    $bMine = @($b -split "`r?`n" | Where-Object { $_ -match "^deniz-process:" })
    $bLeak = @($b -split "`r?`n" | Where-Object { $_ -match "superpowers:|dotnet-|aspire:|mattpocock" })
    Check "control: unmounted session sees no curated skill"  $aLeak.Count 0
    Check "mounted: model sees auto+both only (10+8)"         $bMine.Count 18
    Check "mounted: no upstream plugin leaked in"             $bLeak.Count 0
}

foreach ($v in $saved.Keys) {
    if ($null -eq $saved[$v]) { Remove-Item "Env:$v" -ErrorAction SilentlyContinue }
    else { Set-Item "Env:$v" $saved[$v] }
}
Write-Host ""
if ($fails) { Write-Host "$fails check(s) FAILED — do not trust an observation from this lab." -ForegroundColor Red; exit 1 }
Write-Host "Isolation verified. Nothing but the mounted tree is reachable." -ForegroundColor Green
