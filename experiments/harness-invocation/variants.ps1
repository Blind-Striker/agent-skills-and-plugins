. "$PSScriptRoot\common.ps1"
# What reasoning variants each model actually declares, read from the CLI's own metadata.
# `--variant` is NOT validated (a nonsense value runs silently), so this is the only way to
# know whether a panel leg's effort setting means anything.
$LAB  = Get-LabRoot
$env:USERPROFILE = "$LAB\.opencode-home"; $env:HOME = $env:USERPROFILE
$env:XDG_CONFIG_HOME = "$env:USERPROFILE\.config"; $env:XDG_DATA_HOME = "$env:USERPROFILE\.local\share"
Push-Location "$LAB\project"

# Projected from the one table in common.ps1. The old second copy still named two legs on a
# connector that hit its monthly limit mid-round - which is what a duplicate costs.
$targets = $script:LegTable.GetEnumerator() |
    Where-Object { $_.Value.v } |
    ForEach-Object {
        $parts = Split-LegModel $_.Value.m
        @{ prov = $parts.provider; id = $parts.id; asked = $_.Value.v }
    }
$cache = @{}
foreach ($t in $targets) {
    if (-not $cache.ContainsKey($t.prov)) {
        $cache[$t.prov] = (& opencode models $t.prov --verbose 2>&1 | Out-String)
    }
    $lines = $cache[$t.prov] -split "`r?`n"
    $header = "$($t.prov)/$($t.id)"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -ceq $header) { $start = $i + 1; break } }
    if ($start -lt 0) { "{0,-30} NOT FOUND" -f $header; continue }
    # accumulate the JSON object that follows, by brace balance
    $depth = 0; $buf = @(); $started = $false
    for ($i = $start; $i -lt $lines.Count; $i++) {
        $buf += $lines[$i]
        $depth += ([regex]::Matches($lines[$i], "\{")).Count
        $depth -= ([regex]::Matches($lines[$i], "\}")).Count
        if ($lines[$i].Contains("{")) { $started = $true }
        if ($started -and $depth -le 0) { break }
    }
    try { $obj = ($buf -join "`n") | ConvertFrom-Json } catch { "{0,-30} parse failed" -f $header; continue }
    $vs = $obj.variants
    if (-not $vs) { "{0,-30} asked={1,-6} declares: NONE  -> the flag is a no-op here" -f $header, $t.asked; continue }
    $names = @($vs.PSObject.Properties.Name)
    $detail = $vs.PSObject.Properties | ForEach-Object {
        $v = $_.Value
        $eff = if ($v.reasoningEffort) { $v.reasoningEffort } elseif ($v.options.reasoningEffort) { $v.options.reasoningEffort } else { ($v | ConvertTo-Json -Compress -Depth 4) }
        "{0}->{1}" -f $_.Name, $eff
    }
    $hit = if ($names -ccontains $t.asked) { "OK" } else { "MISSING" }
    "{0,-30} asked={1,-6} [{2,-7}] declares: {3}" -f $header, $t.asked, $hit, ($detail -join "  ")
}
Pop-Location
