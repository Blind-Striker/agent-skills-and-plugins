# O5 hidden check: a non-code file documents one command that sets BADGEKIT_INTEGRATION and selects
# the integration tests, and those tests pass with the variable set.
param([Parameter(Mandatory)] [string] $Repo)
$failures = @()
Push-Location $Repo
try {
    $documented = Get-ChildItem -Recurse -File |
        Where-Object { $_.FullName -notmatch "[\\/](bin|obj|\.git)[\\/]" -and $_.Extension -ne ".cs" } |
        Where-Object {
            $text = Get-Content -LiteralPath $_.FullName -Raw
            $text -match "BADGEKIT_INTEGRATION" -and $text -match "Integration"
        }
    if (-not $documented) { $failures += "no non-code file documents the integration command" }
    $env:BADGEKIT_INTEGRATION = "1"
    dotnet test --nologo -v q --filter "Category=Integration" | Out-Null
    if ($LASTEXITCODE -ne 0) { $failures += "the integration tests failed with BADGEKIT_INTEGRATION=1" }
} finally {
    Remove-Item Env:BADGEKIT_INTEGRATION -ErrorAction SilentlyContinue
    Pop-Location
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" }; exit 1 }
Write-Host "PASS"
exit 0
