# O1 hidden check: the suite passes under both profiles, and the profile test still expects "1.5k".
param([Parameter(Mandatory)] [string] $Repo)
$failures = @()
Push-Location $Repo
try {
    if (-not (Select-String -Path "tests/BadgeKit.Tests/LabelTests.cs" -Pattern '"1.5k"' -SimpleMatch -Quiet)) {
        $failures += "the 1.5k expectation is gone from LabelTests.cs"
    }
    foreach ($profile in "current", "legacy") {
        $env:BADGEKIT_PROFILE = $profile
        dotnet test --nologo -v q | Out-Null
        if ($LASTEXITCODE -ne 0) { $failures += "dotnet test failed under BADGEKIT_PROFILE=$profile" }
    }
} finally {
    Remove-Item Env:BADGEKIT_PROFILE -ErrorAction SilentlyContinue
    Pop-Location
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" }; exit 1 }
Write-Host "PASS"
exit 0
