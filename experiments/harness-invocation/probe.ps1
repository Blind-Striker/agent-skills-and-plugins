# One probe run -> a structured observation.
# Deterministic evidence comes from the tool_use stream, not from asking the model.
#   .\probe.ps1 -Id C4 -Prompt "..." [-Cwd project\scratch-repo] [-Budget 0.8]
param(
    [Parameter(Mandatory)] [string] $Id,
    [Parameter(Mandatory)] [string] $Prompt,
    [string] $Cwd     = "project",
    [double] $Budget  = 0.60,
    [string[]] $Plugins = @("deniz-process"),
    [string] $PermissionMode = ""
)
. "$PSScriptRoot\common.ps1"

$LAB  = Get-LabRoot
$REPO = $script:RepoRoot
Use-ClaudeIsolation
$cliArgs = @()
# --add-dir is load-bearing: a plugin mounted from outside the cwd is not readable
# otherwise, and a subagent silently falls back to the skill body instead of the
# bundled template. Measured: without it, reading code-reviewer.md returns DENIED.
foreach ($p in $Plugins) { $cliArgs += @("--plugin-dir", "$REPO\plugins\$p", "--add-dir", "$REPO\plugins\$p") }
if ($PermissionMode) { $cliArgs += @("--permission-mode", $PermissionMode) }
$cliArgs += @("--output-format","stream-json","--verbose","--max-budget-usd","$Budget","-p",$Prompt)

Push-Location (Join-Path $LAB $Cwd)
$raw = & claude @cliArgs 2>&1 | Out-String
Pop-Location

$skills=@(); $tasks=@(); $reads=@(); $text=""; $cost=0; $truncated=$true
foreach ($l in ($raw -split "`r?`n" | Where-Object { $_.Trim() })) {
    try { $e = $l | ConvertFrom-Json } catch { continue }
    if ($e.type -eq "assistant" -and $e.message.content) {
        foreach ($c in $e.message.content) {
            if ($c.type -eq "tool_use") {
                switch ($c.name) {
                    "Skill" { $skills += $c.input.skill }
                    "Task"  { $tasks  += ("{0}:{1}" -f $c.input.subagent_type, $c.input.description) }
                    "Agent" { $tasks  += ("{0}:{1}" -f $c.input.subagent_type, $c.input.description) }
                    "Read"  { $reads  += $c.input.file_path }
                }
            }
            if ($c.type -eq "text") { $text += $c.text }
        }
    }
    if ($e.type -eq "result") { $text = $e.result; $cost = $e.total_cost_usd; $truncated = $false }
}
[pscustomobject]@{
    Id        = $Id
    Skills    = $skills
    Subagents = $tasks
    ReadHits  = @($reads | Where-Object { $_ -match "code-reviewer|SKILL\.md|\.md$" })
    Cost      = $cost
    Truncated = $truncated
    Text      = $text
}
