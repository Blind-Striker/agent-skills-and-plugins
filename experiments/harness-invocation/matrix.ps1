# The judgement probes across a model panel. Mechanism probes are model-independent
# and are not repeated here (debug config / validate already answer them).
#   .\matrix.ps1 -Probes P1,P2 -Legs grok,kimi
#   .\matrix.ps1 -DryRun            <- ALWAYS run this first. It walks the whole path
#                                      (leg lookup, probe lookup, argv, formatting) and
#                                      checks every requested variant against what the model
#                                      declares, without spending a token. Three measurement
#                                      errors in one session came from launching an hour-long
#                                      job whose wiring had never been executed once.
param(
    [string[]] $Probes = @("P1","P2","P3","P4"),
    [string[]] $Legs   = @("kimi","grok","glm","deepseek"),
    [string]   $Out    = $null,
    [int]      $TimeoutMin = 20,
    [switch]   $DryRun
)
. "$PSScriptRoot\common.ps1"
$LAB  = Get-LabRoot
if (-not $Out) { $Out = Join-Path $LAB "matrix-results.txt" }
Use-OpenCodeIsolation

# The leg table lives in common.ps1 - one copy, one shape. It used to live here and
# again in variants.ps1, and the second copy went stale before anyone edited it twice.
$legTable = $script:LegTable
# Each probe: the command to invoke (or none), the message, and what the run is asking.
$probeTable = @{
    P1 = @{ cmd = "requesting-code-review";      msg = "review HEAD~1..HEAD" }
    P2 = @{ cmd = "implement";                   msg = "add a formatDuration(seconds) helper to duration.js" }
    P3 = @{ cmd = "";                            msg = "implement a formatDuration(seconds) function in duration.js that turns 5400 into 1h30m" }
    # aimed at the final-review step, because that is where the body reaches for
    # ../requesting-code-review/code-reviewer.md — the path validate warns about.
    # RETIRED: it asks a one-file question by opening a 150-line ceremony, so a model that
    # ignores "skip ahead" runs the whole workflow and stalls at a human checkpoint that
    # `opencode run` has nobody to answer. One leg answered in 237s, the next hung at 39min.
    P4 = @{ cmd = "subagent-driven-development"; msg = "all the plan's tasks are already done. go straight to the final code review step for HEAD~1..HEAD and dispatch the reviewer." }
    # Same question, no ceremony: load the command body (so the broken relative path is live)
    # and force a single file resolution. Three outcomes are all informative — the heading
    # (reached it), DENIED (did not), or loading the requesting-code-review skill instead,
    # which is the `both`-posture escape hatch a `manual` item would not have.
    # RETIRED: "the reviewer template" is ambiguous — the body links four templates and three of
    # them sit in the parked bundle and resolve. All five legs read task-reviewer-prompt.md, so
    # this measured parked-bundle reachability (5/5 yes) and never touched the broken path.
    P4b = @{ cmd = "subagent-driven-development"; msg = "Do not perform any task, do not write or edit any file, do not dispatch a subagent. Read the reviewer template this skill's body points at and reply with ONLY its first markdown heading line. If you cannot reach it, reply exactly DENIED." }
    # The broken link is the one for the FINAL code review, which lives in a sibling item. Named
    # by role, not by path, so the model still has to resolve it from the body rather than be told.
    P4c = @{ cmd = "subagent-driven-development"; msg = "Do not perform any task, do not write or edit any file, do not dispatch a subagent. The body links several prompt templates. Find the one used for the FINAL CODE REVIEW at the end of the run — not the task reviewer, not the re-review, not the implementer — read it, and reply with ONLY its first markdown heading line. If you cannot reach that file, reply exactly DENIED." }
}

# The baseline every probe must start from. `implement`'s body ends with "Commit your work
# to the current branch" and the ceremony obeys it, so checkout+clean is not enough — HEAD
# itself moves, and then HEAD~1..HEAD means something different on every later run.


# `opencode run` does not exit when the provider refuses — a monthly-limit error left a run
# alive and idle for 39 minutes, blocking every leg behind it. Never call it unguarded.
function Invoke-OpenCode {
    param([string[]] $CliArgs, [int] $TimeoutSeconds, [string] $WorkDir)
    $so = [IO.Path]::GetTempFileName(); $se = [IO.Path]::GetTempFileName()
    $p = Start-Process -FilePath "opencode" -ArgumentList $CliArgs -WorkingDirectory $WorkDir `
         -NoNewWindow -PassThru -RedirectStandardOutput $so -RedirectStandardError $se
    $done = $p.WaitForExit($TimeoutSeconds * 1000)
    if (-not $done) {
        try { $p.Kill($true) } catch { try { $p.Kill() } catch {} }
        Start-Sleep -Seconds 2
    }
    $out = (Get-Content $so -Raw -ErrorAction SilentlyContinue) + "`n" + (Get-Content $se -Raw -ErrorAction SilentlyContinue)
    Remove-Item $so, $se -Force -ErrorAction SilentlyContinue
    return @{ Raw = $out; TimedOut = (-not $done) }
}

# A leg that cannot answer a one-token prompt will not answer a ceremony either, and the
# failure mode is a hang rather than an error. One cheap call each, before anything long.
function Test-LegLive {
    param([string] $Model)
    $r = Invoke-OpenCode -CliArgs @("run","--format","json","-m",$Model,
            "reply with the single token ZEBRA-OK and nothing else") -TimeoutSeconds 90 -WorkDir "$LAB\project"
    if ($r.TimedOut) { return @{ Live = $false; Why = "timed out after 90s (provider hang)" } }
    $err = $null
    foreach ($ln in ($r.Raw -split "`r?`n" | Where-Object { $_.Trim().StartsWith("{") })) {
        try { $e = $ln | ConvertFrom-Json } catch { continue }
        if ($e.type -eq "text" -and $e.part.text -cmatch "ZEBRA-OK") { return @{ Live = $true; Why = "" } }
        if ($e.type -eq "error") { $err = $e.error.data.message }
    }
    return @{ Live = $false; Why = ("$err" -replace "\s+", " ") }
}

# Pre-flight. Everything here is free and deterministic; nothing below it should be able
# to fail for a reason this pass could have caught.

Write-Host "preflight" -ForegroundColor Cyan
foreach ($p in $Probes) { if ($probeTable.ContainsKey($p)) { Pass "probe $p" } else { Fail "unknown probe: $p" } }
foreach ($n in $Legs)   { if ($legTable.ContainsKey($n))   { Pass "leg $n" }   else { Fail "unknown leg: $n" } }
if (-not (Test-Path "$LAB\project\scratch-repo\.git")) {
    Fail "scratch-repo is not a git repo"
} else {
    Push-Location "$LAB\project\scratch-repo"
    $head = (git rev-parse --short HEAD 2>$null)
    $n    = (git rev-list --count HEAD 2>$null)
    Pop-Location
    if ($head -ne (Get-FixtureBaseline)) { Fail "scratch-repo HEAD is $head, baseline is (Get-FixtureBaseline) — a probe committed and was never rolled back" }
    else { Pass "scratch-repo at baseline (Get-FixtureBaseline) ($n commits)" }
}
if (Test-Path $Out) { Fail "$Out already exists — delete it, or results from two runs will mix" } else { Pass "results file is clean" }
if (-not (Test-Path "$LAB\.opencode-home\.local\share\opencode\auth.json")) { Fail "no auth.json in the lab" } else { Pass "auth.json" }

# The variant trap: an effort the model does not declare is dropped in silence and the run
# proceeds at the default, so "no error" proves nothing. Read what each model offers.
$declaredCache = @{}
foreach ($n in ($Legs | Where-Object { $legTable.ContainsKey($_) })) {
    $spec = $legTable[$n]
    $prov = ($spec.m -split "/")[0]
    if (-not $declaredCache.ContainsKey($prov)) { $declaredCache[$prov] = (& opencode models $prov --verbose 2>&1 | Out-String) }
    $lines = $declaredCache[$prov] -split "`r?`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -ceq $spec.m) { $start = $i + 1; break } }
    if ($start -lt 0) { Fail "$n : $($spec.m) is not in ``opencode models $prov``"; continue }
    $depth = 0; $buf = @(); $seen = $false
    for ($i = $start; $i -lt $lines.Count; $i++) {
        $buf += $lines[$i]
        $depth += ([regex]::Matches($lines[$i], "\{")).Count - ([regex]::Matches($lines[$i], "\}")).Count
        if ($lines[$i].Contains("{")) { $seen = $true }
        if ($seen -and $depth -le 0) { break }
    }
    $obj = $null; try { $obj = ($buf -join "`n") | ConvertFrom-Json } catch {}
    $declared = if ($obj -and $obj.variants) { @($obj.variants.PSObject.Properties.Name) } else { @() }
    if (-not $spec.v) { Pass "$n : --variant omitted, default effort by design" }
    elseif ($declared -ccontains $spec.v) { Pass "$n : variant $($spec.v) is declared" }
    else { Fail "$n : variant '$($spec.v)' is NOT declared by $($spec.m) (declares: $($declared -join ', ')) — it would be dropped silently" }
}
if (-not $DryRun) {
    foreach ($n in ($Legs | Where-Object { $legTable.ContainsKey($_) })) {
        $r = Test-LegLive -Model $legTable[$n].m
        if ($r.Live) { Pass "$n : provider answered" } else { Fail "$n : provider did not answer — $($r.Why)" }
    }
} else { Write-Host "  skip liveness check (dry run)" -ForegroundColor DarkGray }
Assert-Preflight
if (-not $DryRun) {
    # Claim the output atomically. Only another matrix targeting this path collides, not unrelated
    # OpenCode sessions. Keep even an empty or partial claim after failure: the operator must move
    # it before retry so evidence cannot be silently overwritten.
    New-Item -ItemType File -Path $Out -ErrorAction Stop | Out-Null
}
Write-Host "preflight clean: $($Probes.Count) probe(s) x $($Legs.Count) leg(s) = $($Probes.Count * $Legs.Count) run(s), ${TimeoutMin}min cap each" -ForegroundColor Green

foreach ($p in $Probes) {
    foreach ($legName in $Legs) {
        $spec = $legTable[$legName]; $pr = $probeTable[$p]
        if (-not $spec -or -not $pr) { throw "unknown leg/probe: $legName / $p" }
        Reset-Scratch
        $cliArgs = @("run", "--format", "json", "-m", $spec.m, "--auto")
        if ($spec.v) { $cliArgs += @("--variant", $spec.v) }
        if ($pr.cmd) { $cliArgs += @("--command", $pr.cmd) }
        $cliArgs += $pr.msg

        if ($DryRun) {
            Write-Host ("  {0}/{1,-10} opencode {2}" -f $p, $legName, ($cliArgs -join " "))
            continue
        }

        $sw = [Diagnostics.Stopwatch]::StartNew()
        $res = Invoke-OpenCode -CliArgs $cliArgs -TimeoutSeconds ($TimeoutMin * 60) -WorkDir "$LAB\project\scratch-repo"
        $raw = $res.Raw
        $sw.Stop()

        $skills = @(); $tools = @(); $text = ""; $cost = 0
        foreach ($l in ($raw -split "`r?`n" | Where-Object { $_.Trim().StartsWith("{") })) {
            try { $e = $l | ConvertFrom-Json } catch { continue }
            switch ($e.type) {
                "tool_use"    { $tools += $e.part.tool; if ($e.part.tool -eq "skill") { $skills += $e.part.state.input.name } }
                "text"        { $text += $e.part.text }
                "step_finish" { $cost += $e.part.cost }
            }
        }
        $err = ($raw -split "`r?`n" | Where-Object { $_ -match "^Error" } | Select-Object -First 1)
        # a killed run is not a result; never let a timeout read as "the model declined"
        if ($res.TimedOut) { $err = "TIMED-OUT-after-${TimeoutMin}min — no verdict, rerun this leg" }
        # the pointer question: did the body's /setup-matt-pocock-skills get relayed, or invoked?
        $relayed = if ($text -match "setup-matt-pocock-skills") { "mentions-setup" } else { "-" }
        # the model is echoed back so a mislabelled leg cannot hide in the results again
        $line = "{0}|{1}|{2}@{3}|skills={4}|tools={5}|{6}|{7}s|`$~{8}|{9}" -f `
            $p, $legName, $spec.m, $spec.v,
            (($skills | Sort-Object -Unique) -join "+"), (($tools | Sort-Object -Unique) -join "+"),
            $relayed, [int]$sw.Elapsed.TotalSeconds, (Format-Cost $cost), $err
        Add-Content -Path $Out -Value $line
        Add-Content -Path "$Out.text" -Value ("`n===== $p / $legName =====`n" + $text)
        Write-Output $line
    }
}
if ($DryRun) { Write-Host "DRY RUN — nothing was invoked." -ForegroundColor Green; return }
Reset-Scratch
Write-Output "MATRIX DONE"
