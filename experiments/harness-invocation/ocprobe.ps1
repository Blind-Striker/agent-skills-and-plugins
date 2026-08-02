# One OpenCode probe run -> a structured observation, from the JSON event stream.
#   .\ocprobe.ps1 -Id O2 -Message "/requesting-code-review" [-Command name] [-Cwd project\scratch-repo]
param(
    [Parameter(Mandatory)] [string] $Id,
    [string] $Message = "",
    [string] $Command = "",
    [string] $Cwd     = "project",
    [switch] $Auto,
    [switch] $ShowText
)
. "$PSScriptRoot\common.ps1"

$LAB  = Get-LabRoot
Use-OpenCodeIsolation

$cliArgs = @("run", "--format", "json")
if ($Command) { $cliArgs += @("--command", $Command) }
if ($Auto)    { $cliArgs += "--auto" }
if ($Message) { $cliArgs += $Message }

Push-Location (Join-Path $LAB $Cwd)
$raw = & opencode @cliArgs 2>&1 | Out-String
Pop-Location

$skills = @(); $tools = @(); $text = ""; $firstInput = $null; $cost = 0
foreach ($l in ($raw -split "`r?`n" | Where-Object { $_.Trim().StartsWith("{") })) {
    try { $e = $l | ConvertFrom-Json } catch { continue }
    switch ($e.type) {
        "tool_use" {
            $t = $e.part.tool
            $tools += $t
            if ($t -eq "skill") { $skills += $e.part.state.input.name }
        }
        "text"        { $text += $e.part.text }
        "step_finish" {
            if ($null -eq $firstInput) { $firstInput = $e.part.tokens.input + $e.part.tokens.cache.read }
            $cost += $e.part.cost
        }
    }
}
[pscustomobject]@{
    Id           = $Id
    Skills       = $skills
    Tools        = $tools
    FirstInputTk = $firstInput      # size of the prompt the model actually saw
    Cost         = (Format-Cost $cost)
    Text         = if ($ShowText) { $text } else { $text.Substring(0, [Math]::Min(400, $text.Length)) }
}
