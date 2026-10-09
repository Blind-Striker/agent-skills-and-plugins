# OpenCode 2 discovery check: what an isolated `opencode serve` registers. No model call.
#   .\oc2-discovery.ps1 -Lab <dir> -DryRun   print the isolated environment and the command; start nothing
#   .\oc2-discovery.ps1 -Lab <dir>           start the server, list skills, commands and agents, stop it
#
# A real run prints one JSON object:
#   { opencodeVersion, skills: [{ id, advertised }], commands: [id], agents: [id] }
#
# The OpenCode 2 CLI has no skill or command listing, and one-shot introspection races the
# asynchronous location load and returns empty lists. A persistent `serve` answers once the location
# has loaded, so this polls /api/skill until its ID set is non-empty and stable. `advertised` is the
# server's resolved
# `autoinvoke`, which OpenCode derives from metadata["opencode/autoinvoke"] (or
# disable-model-invocation); a skill is offered to the model only when that is not false.
#
# Isolation: every root (home, the four XDG roots, OPENCODE_CONFIG_DIR, OPENCODE_DB) is below -Lab,
# the project walk is off, the managed background service is disabled through
# <lab>/config/service.json, and the server listens on 127.0.0.1 with Basic auth from a random
# password, which is stopped in `finally`. The run fails closed when a skill resolves outside the lab
# (built-ins report /builtin/<id>.md). Install Modules first, if wanted, with
# OPENCODE_CONFIG_DIR=<lab>/config. Raw server logs stay in <lab>/logs.
param(
    [Parameter(Mandatory)] [string] $Lab,
    [ValidateRange(1, 65535)] [int] $Port = 0,
    [ValidateRange(1, 300)] [int] $DiscoverySeconds = 20,
    [ValidateRange(1, 60)] [int] $StableSeconds = 3,
    [switch] $DryRun
)
$ErrorActionPreference = "Stop"

. "$PSScriptRoot\common.ps1"

function Get-FreeLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return ([Net.IPEndPoint] $listener.LocalEndpoint).Port } finally { $listener.Stop() }
}

function Get-RouteData {
    param([string] $Route)
    $uri = "$baseUrl$Route" + "?" + [Uri]::EscapeDataString("location[directory]") + "=" + [Uri]::EscapeDataString($project)
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -NoProxy -TimeoutSec 10
    $reported = [IO.Path]::GetFullPath([string] $response.location.directory)
    if ($reported -ne $project) { throw "$Route answered for location '$reported', not the lab project" }
    return @($response.data)
}

$labFull = [IO.Path]::GetFullPath($Lab)
$trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = [IO.Path]::GetFullPath($script:RepoRoot).TrimEnd($trimChars) + [IO.Path]::DirectorySeparatorChar
if (($labFull.TrimEnd($trimChars) + [IO.Path]::DirectorySeparatorChar).StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "refused: the lab '$labFull' is inside the repository"
}

$environment = Get-OpenCodeLabEnvironment -Root $labFull
# Discovery needs no model catalog; skip the network fetch.
$environment["OPENCODE_DISABLE_MODELS_FETCH"] = "1"
$project = Join-Path $labFull "project"
if (-not $Port) { $Port = Get-FreeLoopbackPort }
$serveArgs = @("serve", "--hostname", "127.0.0.1", "--port", "$Port", "--print-logs")

if ($DryRun) {
    [ordered]@{
        env     = $environment
        cleared = $script:OpenCodeLabClearedVariables
        cwd     = $project
        command = "opencode $($serveArgs -join ' ')"
    } | ConvertTo-Json -Depth 5
    return
}

Initialize-OpenCodeLab -Environment $environment
New-Item -ItemType Directory -Path $project -Force | Out-Null
$logs = Join-Path $labFull "logs"
New-Item -ItemType Directory -Path $logs -Force | Out-Null

$opencode = (Get-Command opencode -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$password = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).
    TrimEnd('=').Replace('+', '-').Replace('/', '_')
$baseUrl = "http://127.0.0.1:$Port"
$headers = @{ Authorization = "Basic " + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("opencode:$password")) }

$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $opencode
$startInfo.WorkingDirectory = $project
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($arg in $serveArgs) { $startInfo.ArgumentList.Add($arg) }
foreach ($name in $script:OpenCodeLabClearedVariables) { $startInfo.Environment.Remove($name) | Out-Null }
foreach ($name in $environment.Keys) { $startInfo.Environment[$name] = [string] $environment[$name] }
$startInfo.Environment["OPENCODE_PASSWORD"] = $password

$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmss")
$started = $false
try {
    if (-not $process.Start()) { throw "failed to start opencode serve" }
    $started = $true
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()

    $info = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not $info) {
        if ($process.HasExited) { throw "opencode serve exited $($process.ExitCode) before it answered; see $logs" }
        if ([DateTime]::UtcNow -gt $deadline) { throw "opencode serve did not answer /api/info within 30 seconds" }
        try { $info = Invoke-RestMethod -Uri "$baseUrl/api/info" -Headers $headers -NoProxy -TimeoutSec 5 }
        catch { Start-Sleep -Milliseconds 250 }
    }

    # The first request races the location load: an empty or short list is "not loaded yet", not
    # "nothing installed". Non-empty is not enough: the built-in skills register before the file
    # scan finishes (measured: the first non-empty answer held only the 2 built-ins while 114
    # installed skills were still loading). So poll until the ID set is non-empty and unchanged
    # for $StableSeconds, within $DiscoverySeconds.
    $skills = @()
    $previous = $null
    $stableSince = $null
    $settled = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($DiscoverySeconds)
    while ([DateTime]::UtcNow -le $deadline) {
        $skills = @(Get-RouteData "/api/skill")
        $key = (@($skills | ForEach-Object { [string] $_.id } | Sort-Object) -join "`n")
        if (-not $skills.Count -or $key -cne $previous) {
            $previous = $key
            $stableSince = [DateTime]::UtcNow
        } elseif (([DateTime]::UtcNow - $stableSince).TotalSeconds -ge $StableSeconds) {
            $settled = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $skills.Count) { throw "/api/skill stayed empty for $DiscoverySeconds seconds" }
    if (-not $settled) { throw "/api/skill did not settle for $StableSeconds seconds within $DiscoverySeconds seconds" }
    # Fail closed on a leak: every skill is a built-in (/builtin/<id>.md) or a file below the lab.
    $labPrefix = $labFull.TrimEnd($trimChars) + [IO.Path]::DirectorySeparatorChar
    $leaked = @($skills | Where-Object {
        $path = [string] $_.path
        -not $path.StartsWith("/builtin/") -and
            -not [IO.Path]::GetFullPath($path).StartsWith($labPrefix, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($leaked) { throw "skills resolved outside the lab: $(@($leaked | ForEach-Object { "$($_.id) <- $($_.path)" }) -join '; ')" }
    $commands = @(Get-RouteData "/api/command")
    $agents = @(Get-RouteData "/api/agent")

    [ordered]@{
        opencodeVersion = [string] $info.version
        skills          = @($skills | Sort-Object { [string] $_.id } | ForEach-Object {
            [ordered]@{ id = [string] $_.id; advertised = ($_.autoinvoke -ne $false) }
        })
        commands        = @($commands | ForEach-Object { [string] $_.name } | Sort-Object)
        agents          = @($agents | ForEach-Object { [string] $_.id } | Sort-Object)
    } | ConvertTo-Json -Depth 5
} finally {
    if ($started) {
        if (-not $process.HasExited) {
            try { $process.Kill($true) } catch { try { $process.Kill() } catch {} }
        }
        if (-not $process.WaitForExit(10000)) { Write-Warning "opencode serve (pid $($process.Id)) did not exit within 10 seconds" }
        $streamsDone = [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]] @($stdoutTask, $stderrTask), 5000)
        if ($streamsDone) {
            Set-Content -Path (Join-Path $logs "serve-$stamp.stdout.log") -Value $stdoutTask.GetAwaiter().GetResult() -NoNewline
            Set-Content -Path (Join-Path $logs "serve-$stamp.stderr.log") -Value $stderrTask.GetAwaiter().GetResult() -NoNewline
        }
    }
    $process.Dispose()
}
