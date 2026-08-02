# Claude Code side of the panel. Same discipline as matrix.ps1: dry-run first, preflight
# refuses to start, and the model that ACTUALLY ran is read back from the init event rather
# than assumed from the flag — the OpenCode round showed a silently-dropped effort flag.
#   .\claude-matrix.ps1 -DryRun
#   .\claude-matrix.ps1 -Repeats 3
param(
    [string[]] $Models  = @("opus","fable"),
    [string]   $Effort  = "xhigh",
    [int]      $Repeats = 3,
    [string]   $Out     = $null,
    [switch]   $DryRun
)
. "$PSScriptRoot\common.ps1"
$LAB  = Get-LabRoot
if (-not $Out) { $Out = Join-Path $LAB "results-claude.txt" }
$REPO = $script:RepoRoot
$PLUG = "$REPO\plugins\deniz-process"
Use-ClaudeIsolation

# The task is identical to the OpenCode panel's P2, so the two sides are comparable.
$PROMPT = "/deniz-process:implement add a formatDuration(seconds) helper to duration.js"




Write-Host "preflight" -ForegroundColor Cyan
Push-Location "$LAB\project\scratch-repo"
$head = (git rev-parse --short HEAD 2>$null); $n = (git rev-list --count HEAD 2>$null)
Pop-Location
if ($head -ne (Get-FixtureBaseline)) { Fail "scratch HEAD is $head, baseline (Get-FixtureBaseline)" } else { Pass "scratch at baseline ($n commits)" }
if (-not (Test-Path "$PLUG\.claude-plugin\plugin.json")) { Fail "plugin not found at $PLUG" } else { Pass "plugin dir" }
if (-not (Test-Path "$LAB\.claude-home\.credentials.json")) { Fail "no credentials in the lab config" } else { Pass "credentials" }
if (Test-Path $Out) { Fail "$Out exists — move it, or two runs will mix" } else { Pass "results file clean" }
if ("low","medium","high","xhigh","max" -notcontains $Effort) { Fail "effort '$Effort' is not one claude --help lists" } else { Pass "effort $Effort is a listed level" }
# Does an invalid effort actually fail, or is it dropped like OpenCode's --variant?
$probe = & claude --effort zzz-not-a-level -p "hi" 2>&1 | Out-String
if ($probe -cmatch "hi|Hi|HI" -and $probe -notmatch "(?i)error|invalid|choice") {
    Write-Host "  note effort is NOT validated — a bad level runs anyway; trust the level list, not the exit code" -ForegroundColor Yellow
} else { Pass "effort is validated (a bad level is rejected)" }
Assert-Preflight
Write-Host "preflight clean: $($Models.Count) model(s) x $Repeats repeat(s) = $($Models.Count * $Repeats) run(s)" -ForegroundColor Green

foreach ($model in $Models) {
    for ($i = 1; $i -le $Repeats; $i++) {
        $cliArgs = @("--plugin-dir", $PLUG, "--add-dir", $PLUG, "--model", $model, "--effort", $Effort,
                     "--permission-mode", "acceptEdits", "--output-format", "stream-json", "--verbose",
                     "--max-budget-usd", "3.0", "-p", $PROMPT)
        if ($DryRun) { Write-Host ("  {0}#{1}  claude {2}" -f $model, $i, ($cliArgs -join " ")); continue }

        Reset-Scratch
        $sw = [Diagnostics.Stopwatch]::StartNew()
        Push-Location "$LAB\project\scratch-repo"
        $raw = & claude @cliArgs 2>&1 | Out-String
        Pop-Location
        $sw.Stop()

        $skills = @(); $ranModel = "?"; $cost = 0; $capped = $true; $text = ""
        foreach ($l in ($raw -split "`r?`n" | Where-Object { $_.Trim().StartsWith("{") })) {
            try { $e = $l | ConvertFrom-Json } catch { continue }
            if ($e.type -eq "system" -and $e.subtype -eq "init") { $ranModel = $e.model }
            if ($e.type -eq "assistant" -and $e.message.content) {
                foreach ($c in $e.message.content) {
                    if ($c.type -eq "tool_use" -and $c.name -eq "Skill") { $skills += $c.input.skill }
                }
            }
            if ($e.type -eq "result") { $cost = $e.total_cost_usd; $capped = $false; $text = "$($e.result)" }
        }
        $tdd = if (@($skills | Where-Object { $_ -like "*test-driven-development*" }).Count) { "TDD-FIRED" } else { "tdd-no" }
        # a capped run is not a non-firing run; never let the two look alike
        $line = "{0}#{1}|ran={2}@{3}|{4}|skills={5}|{6}s|`$~{7}|{8}" -f `
            $model, $i, $ranModel, $Effort, $tdd, (($skills | Sort-Object -Unique) -join "+"),
            [int]$sw.Elapsed.TotalSeconds, (Format-Cost $cost), $(if ($capped) { "CAPPED-no-result-event" } else { "" })
        Add-Content -Path $Out -Value $line
        Add-Content -Path "$Out.text" -Value ("`n===== $model #$i =====`n" + $text)
        Write-Output $line
    }
}
if ($DryRun) { Write-Host "DRY RUN — nothing was invoked." -ForegroundColor Green; return }
Reset-Scratch
Write-Output "CLAUDE MATRIX DONE"
