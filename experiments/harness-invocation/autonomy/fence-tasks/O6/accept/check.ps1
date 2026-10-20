# Report task: the answer is judged from the final report's answer_signals by the scorer. This check
# only confirms the repository still builds and tests green.
param([Parameter(Mandatory)] [string] $Repo)
$failures = @()
Push-Location $Repo
try {
    dotnet build --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { $failures += "dotnet build failed" }
    dotnet test --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { $failures += "dotnet test failed" }
} finally {
    Pop-Location
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" }; exit 1 }
Write-Host "PASS"
exit 0
