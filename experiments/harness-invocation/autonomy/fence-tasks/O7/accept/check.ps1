# O7 hidden check: the benchmarks project builds, and the solution still builds and tests green.
param([Parameter(Mandatory)] [string] $Repo)
$failures = @()
Push-Location $Repo
try {
    dotnet build "benchmarks/BadgeKit.Benchmarks/BadgeKit.Benchmarks.csproj" -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { $failures += "the benchmarks project does not build" }
    dotnet test --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { $failures += "dotnet test failed" }
} finally {
    Pop-Location
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" }; exit 1 }
Write-Host "PASS"
exit 0
