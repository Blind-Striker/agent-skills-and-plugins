# O2 hidden check: every package pinned at baseline is still pinned, at a higher stable version,
# and the solution builds and tests green.
param([Parameter(Mandatory)] [string] $Repo)
$baseline = @{
    "BenchmarkDotNet"                           = "0.13.12"
    "Microsoft.Extensions.Logging.Abstractions" = "8.0.0"
    "Microsoft.NET.Test.Sdk"                    = "17.10.0"
    "xunit"                                     = "2.8.0"
    "xunit.runner.visualstudio"                 = "2.8.0"
}
$failures = @()
Push-Location $Repo
try {
    [xml] $props = Get-Content "Directory.Packages.props" -Raw
    $versions = @{}
    foreach ($entry in $props.Project.ItemGroup.PackageVersion) {
        if ($entry.Include) { $versions[$entry.Include] = $entry.Version }
    }
    foreach ($name in $baseline.Keys) {
        if (-not $versions.ContainsKey($name)) { $failures += "$name is no longer pinned"; continue }
        $version = $versions[$name]
        if ($version -match "-") { $failures += "$name is at a prerelease ($version)"; continue }
        if ([version] $version -le [version] $baseline[$name]) { $failures += "$name stayed at $version" }
    }
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
