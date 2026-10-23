#:package OpenCodeDotNet.Sdk@0.9.0-preview.13
#:property TargetFramework=net10.0

// Disposable-runner probe: can a dispatch harness start `opencode serve` inside an OS sandbox by giving
// the OpenCode .NET SDK a wrapper argv, and do the SDK launcher's guarantees (readiness, appended
// argv, graceful stop, no orphans after the owner dies) still hold through the wrapper?
//
//   dotnet run probe.cs -- <none|bwrap|seatbelt> <outDir>
//
// Mode `none` is the unsandboxed baseline and runs anywhere. `bwrap`, `bwrap-nopid` (no
// --unshare-pid) and `bwrap-tini` (tini or dumb-init between bwrap and opencode, to forward signals)
// run only on Linux, and `seatbelt` only on macOS. The result goes to <outDir>/<mode>.json, and a summary table goes to stdout and to
// $GITHUB_STEP_SUMMARY when set. `--hold <mode> <root>` is the internal child mode for the hard kill.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Sdk;
using OpenCode.Sdk.Models;

if (args.Length >= 3 && args[0] == "--hold")
{
    return await Probe.HoldAsync(args[1], args[2]);
}

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run probe.cs -- <none|bwrap|seatbelt> <outDir>");
    return 2;
}

return await Probe.RunAsync(args[0], Path.GetFullPath(args[1]));

sealed record Layout(string Root, string Work, string Outside, string State);

sealed record Proc(int Pid, int Ppid, int Pgid, string Cmd);

static class Probe
{
    const string AppendedTail = "serve --stdio --port 0";
    static readonly TimeSpan Readiness = TimeSpan.FromSeconds(90);

    public static async Task<int> RunAsync(string mode, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var result = new JsonObject
        {
            ["mode"] = mode,
            ["os"] = RuntimeInformation.OSDescription,
            ["arch"] = RuntimeInformation.OSArchitecture.ToString(),
            ["startedUtc"] = DateTime.UtcNow.ToString("O"),
        };
        var code = 0;
        try
        {
            code = await RunCoreAsync(mode, result);
        }
        catch (Exception ex)
        {
            result["fatal"] = ex.ToString();
            code = 1;
        }
        finally
        {
            result["finishedUtc"] = DateTime.UtcNow.ToString("O");
            var path = Path.Combine(outDir, mode + ".json");
            await File.WriteAllTextAsync(path, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var summary = Summary(result);
            Console.WriteLine(summary);
            Console.WriteLine($"wrote {path}");
            if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } stepSummary)
            {
                await File.AppendAllTextAsync(stepSummary, summary + "\n");
            }
        }

        return code;
    }

    static async Task<int> RunCoreAsync(string mode, JsonObject result)
    {
        var refusal = Refusal(mode);
        if (refusal is not null)
        {
            result["refused"] = refusal;
            return 2;
        }

        if (mode == "bwrap-tini")
        {
            if (ResolveInit() is not { } init)
            {
                result["skipped"] = "init missing: neither tini nor dumb-init was found";
                return 0;
            }

            result["init"] = ToArray(init);
        }

        var layout = CreateLayout(mode);
        result["layout"] = new JsonObject
        {
            ["root"] = layout.Root,
            ["work"] = layout.Work,
            ["outside"] = layout.Outside,
            ["state"] = layout.State,
        };
        var command = Command(mode, layout, unshareNet: false);
        result["command"] = ToArray(command);

        // 1. Readiness.
        var readiness = new JsonObject();
        result["readiness"] = readiness;
        var output = new OpenCodeServerOutput();
        var sw = Stopwatch.StartNew();
        OpenCodeServer server;
        try
        {
            server = await Start(command, layout, output);
        }
        catch (Exception ex)
        {
            readiness["started"] = false;
            readiness["startMs"] = sw.ElapsedMilliseconds;
            readiness["error"] = $"{ex.GetType().Name}: {ex.Message}";
            readiness["stderrTail"] = StderrTail(output);
            return 0;
        }

        readiness["started"] = true;
        readiness["startMs"] = sw.ElapsedMilliseconds;
        readiness["endpoint"] = server.Endpoint.ToString();
        readiness["launcherPid"] = server.ProcessId;
        var client = server.CreateClient();
        var info = await PollInfoAsync(client, TimeSpan.FromSeconds(30));
        readiness["readyMs"] = sw.ElapsedMilliseconds;
        readiness["info"] = info;

        // 2. Process tree.
        var treeAtStart = Descendants(Snapshot(), server.ProcessId);
        result["processTree"] = TreeJson(treeAtStart, server.ProcessId);

        // 3. Fence.
        result["fence"] = await FenceAsync(client, layout);

        // 4. Graceful stop. Survivors are judged against every process seen in the tree.
        var treeBeforeStop = Descendants(Snapshot(), server.ProcessId);
        result["processTreeBeforeStop"] = ProcsJson(treeBeforeStop);
        result["gracefulStop"] = await StopAsync(server, client, treeAtStart.Concat(treeBeforeStop).DistinctBy(p => p.Pid).ToList(), output);

        // 4b. bwrap only: does --unshare-net break readiness or the loopback client?
        if (mode == "bwrap")
        {
            result["unshareNet"] = await UnshareNetAsync(layout);
        }

        // 5. Hard kill of the owner.
        result["hardKill"] = await HardKillAsync(mode, layout);
        return 0;
    }

    static string? Refusal(string mode) => mode switch
    {
        "none" => null,
        "bwrap" or "bwrap-nopid" or "bwrap-tini" => OperatingSystem.IsLinux() ? null : $"mode {mode} runs only on Linux",
        "seatbelt" => OperatingSystem.IsMacOS() ? null : "mode seatbelt runs only on macOS",
        _ => $"unknown mode '{mode}'; use none, bwrap, bwrap-nopid, bwrap-tini or seatbelt",
    };

    // tini -s (subreaper, since bwrap's own init is pid 1) or dumb-init, by absolute path.
    static string[]? ResolveInit()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/usr/bin", "/usr/sbin", "/bin", "/sbin", "/usr/local/bin"]).Distinct().ToList();
        string? Find(string name) => dirs.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
        if (Find("tini") is { } tini)
        {
            return [tini, "-s", "--"];
        }

        return Find("dumb-init") is { } dumbInit ? [dumbInit, "--"] : null;
    }

    // ---------- layout and command ----------

    static Layout CreateLayout(string mode)
    {
        // The root must sit outside every path the sandbox leaves writable (/tmp, /private/var/folders),
        // or a write to outside/ would pass the fence for the wrong reason.
        var baseDir = Environment.GetEnvironmentVariable("PROBE_ROOT") is { Length: > 0 } explicitRoot
            ? explicitRoot
            : OperatingSystem.IsWindows()
                ? Path.GetTempPath()
                : Environment.GetEnvironmentVariable("RUNNER_TEMP") is { Length: > 0 } runnerTemp
                    ? runnerTemp
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sandbox-probe");
        var root = RealPath(Path.Combine(Path.GetFullPath(baseDir), $"sandbox-probe-{mode}-{Guid.NewGuid():N}"[..^24]));
        var layout = LayoutFor(root);
        foreach (var dir in new[] { layout.Work, layout.Outside, layout.State })
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(Path.Combine(layout.Work, "README.txt"), "probe work tree\n");
        Git(layout.Work, "init", "-q");
        Git(layout.Work, "add", "README.txt");
        Git(layout.Work, "-c", "user.name=probe", "-c", "user.email=probe@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "probe initial commit");
        return layout;
    }

    static Layout LayoutFor(string root) =>
        new(root, Path.Combine(root, "work"), Path.Combine(root, "outside"), Path.Combine(root, "state"));

    // Seatbelt matches resolved paths; /var and /tmp are symlinks into /private on macOS.
    static string RealPath(string path) =>
        OperatingSystem.IsMacOS() && (path.StartsWith("/var/", StringComparison.Ordinal) || path.StartsWith("/tmp/", StringComparison.Ordinal))
            ? "/private" + path
            : path;

    static void Git(string cwd, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in arguments)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed ({p.ExitCode}): {stderr}");
        }
    }

    static Dictionary<string, string> Env(Layout l) => new()
    {
        ["XDG_CONFIG_HOME"] = Path.Combine(l.State, "config"),
        ["XDG_DATA_HOME"] = Path.Combine(l.State, "data"),
        ["XDG_STATE_HOME"] = Path.Combine(l.State, "state"),
        ["XDG_CACHE_HOME"] = Path.Combine(l.State, "cache"),
        ["OPENCODE_CONFIG_DIR"] = Path.Combine(l.State, "opencode-config"),
        // The harness sets these; the probe mirrors it.
        ["OPENCODE_FILEWATCHER_DISABLE"] = "1",
        ["OPENCODE_DISABLE_FILEWATCHER"] = "1",
    };

    static string[] Command(string mode, Layout l, bool unshareNet)
    {
        var opencode = ResolveOpenCode();
        switch (mode)
        {
            case "bwrap" or "bwrap-nopid" or "bwrap-tini":
                var pidNamespace = mode != "bwrap-nopid";
                var argv = new List<string> { "bwrap", "--ro-bind", "/", "/", "--dev", "/dev" };
                // A fresh procfs needs a pid namespace the sandbox owns; without one, bind the host's.
                argv.AddRange(pidNamespace ? ["--proc", "/proc"] : ["--ro-bind", "/proc", "/proc"]);
                argv.AddRange(["--tmpfs", "/tmp", "--bind", l.Work, l.Work, "--bind", l.State, l.State]);
                if (pidNamespace)
                {
                    argv.Add("--unshare-pid");
                }

                argv.Add("--die-with-parent");
                if (unshareNet)
                {
                    argv.Add("--unshare-net");
                }

                argv.Add("--");
                if (mode == "bwrap-tini")
                {
                    argv.AddRange(ResolveInit() ?? throw new InvalidOperationException("init missing"));
                }

                argv.AddRange([opencode, "serve"]);
                return [.. argv];
            case "seatbelt":
                return ["sandbox-exec", "-p", SeatbeltProfile(l), opencode, "serve"];
            default:
                return [opencode, "serve"];
        }
    }

    static string SeatbeltProfile(Layout l) => $"""
        (version 1)
        (allow default)
        (deny file-write*)
        (allow file-write*
          (subpath "{l.Work}")
          (subpath "{l.State}")
          (subpath "/tmp")
          (subpath "/private/tmp")
          (subpath "/private/var/folders")
          (subpath "/dev"))
        """;

    // The harness's resolution: on Windows start opencode.exe behind the npm shim directly; elsewhere
    // give the wrapper an absolute path so PATH inside the sandbox does not matter.
    static string ResolveOpenCode()
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (OperatingSystem.IsWindows())
        {
            foreach (var dir in dirs)
            {
                if (File.Exists(Path.Combine(dir, "opencode.cmd")))
                {
                    var exe = Path.Combine(dir, "node_modules", "@opencode", "cli", "bin", "opencode.exe");
                    if (File.Exists(exe))
                    {
                        return exe;
                    }
                }
            }

            return "opencode";
        }

        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, "opencode");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "opencode";
    }

    static async Task<OpenCodeServer> Start(string[] command, Layout layout, OpenCodeServerOutput output) =>
        await OpenCodeServer.StartAsync(new OpenCodeServerOptions
        {
            Command = command,
            WorkingDirectory = layout.Work,
            Environment = Env(layout),
            Output = output,
            ReadinessTimeout = Readiness,
        });

    static async Task<JsonObject> PollInfoAsync(OpenCodeClient client, TimeSpan budget)
    {
        var sw = Stopwatch.StartNew();
        var attempts = 0;
        string? last = null;
        while (sw.Elapsed < budget)
        {
            attempts++;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await client.Server.GetInfoAsync(cancellationToken: cts.Token);
                if (!response.IsError && response.ServerInfo is { } info)
                {
                    return new JsonObject
                    {
                        ["ok"] = true,
                        ["version"] = info.Version,
                        ["pid"] = info.Pid,
                        ["attempts"] = attempts,
                        ["ms"] = sw.ElapsedMilliseconds,
                    };
                }

                last = $"status {response.Status}: {response.Error}";
            }
            catch (Exception ex)
            {
                last = $"{ex.GetType().Name}: {ex.Message}";
            }

            await Task.Delay(500);
        }

        return new JsonObject { ["ok"] = false, ["attempts"] = attempts, ["lastError"] = last, ["ms"] = sw.ElapsedMilliseconds };
    }

    static JsonArray StderrTail(OpenCodeServerOutput output)
    {
        try
        {
            return ToArray(output.GetSnapshot().StandardError.TakeLast(30));
        }
        catch (Exception ex)
        {
            return ToArray([$"(no output: {ex.Message})"]);
        }
    }

    // ---------- fence ----------

    static async Task<JsonObject> FenceAsync(OpenCodeClient client, Layout l)
    {
        string Q(string path) => "'" + (OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path) + "'";
        string W(string name) => Q(Path.Combine(l.Work, name));
        var probes = new (string Name, string Command, string Target)[]
        {
            ("inside", $"touch {W("inside.txt")} 2> {W("inside.err")}; echo $? > {W("inside.rc")}", Path.Combine(l.Work, "inside.txt")),
            ("escape", $"touch {Q(Path.Combine(l.Outside, "escape.txt"))} 2> {W("escape.err")}; echo $? > {W("escape.rc")}", Path.Combine(l.Outside, "escape.txt")),
            ("git", $"git -C {Q(l.Work)} log -1 --oneline > {W("git.txt")} 2>&1; echo $? > {W("git.rc")}", Path.Combine(l.Work, "git.txt")),
            ("net", $"curl -sS -m 10 https://example.com -o /dev/null -w '%{{http_code}}' > {W("net.txt")} 2>&1; echo $? > {W("net.rc")}", Path.Combine(l.Work, "net.txt")),
        };

        var fence = new JsonObject();
        var shellIds = new Dictionary<string, string>();
        foreach (var (name, command, _) in probes)
        {
            var entry = new JsonObject { ["command"] = command };
            fence[name] = entry;
            try
            {
                var response = await client.Shells.CreateShellAsync(new ShellCreateRequest
                {
                    Command = command,
                    Cwd = l.Work,
                    Location = new LocationSelector { Directory = l.Work },
                    Timeout = 60_000,
                });
                entry["createStatus"] = response.Status;
                if (response.IsError || response.Shell is null)
                {
                    entry["createError"] = response.Error?.ToString() ?? response.RawBody;
                    continue;
                }

                entry["shell"] = response.Shell.Shell;
                entry["shellPid"] = response.Shell.Pid;
                shellIds[name] = response.Shell.Id;
            }
            catch (Exception ex)
            {
                entry["createError"] = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        // The .rc files mark completion: each command writes its exit status last.
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(60)
               && !shellIds.Keys.All(n => File.Exists(Path.Combine(l.Work, n + ".rc")) && new FileInfo(Path.Combine(l.Work, n + ".rc")).Length > 0))
        {
            await Task.Delay(250);
        }

        fence["waitMs"] = sw.ElapsedMilliseconds;
        foreach (var (name, _, target) in probes)
        {
            var entry = (JsonObject)fence[name]!;
            entry["rc"] = ReadOrNull(Path.Combine(l.Work, name + ".rc"));
            entry["target"] = target;
            entry["targetExists"] = File.Exists(target);
            entry["targetContent"] = File.Exists(target) ? ReadOrNull(target) : null;
            entry["stderr"] = ReadOrNull(Path.Combine(l.Work, name + ".err"));
            if (shellIds.TryGetValue(name, out var id))
            {
                try
                {
                    var shell = await client.Shells.GetShellClient(id).GetAsync(new ShellRequest());
                    entry["finalStatus"] = shell.Shell?.Status.ToString();
                    entry["finalExit"] = shell.Shell?.Exit;
                    entry["finalSignal"] = shell.Shell?.Signal;
                }
                catch (Exception ex)
                {
                    entry["finalError"] = $"{ex.GetType().Name}: {ex.Message}";
                }
            }
        }

        return fence;
    }

    static string? ReadOrNull(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Trim();
            return text.Length > 2000 ? text[..2000] : text;
        }
        catch (Exception ex)
        {
            return $"(unreadable: {ex.Message})";
        }
    }

    // ---------- stop ----------

    static async Task<JsonObject> StopAsync(OpenCodeServer server, OpenCodeClient client, List<Proc> tree, OpenCodeServerOutput output)
    {
        var stop = new JsonObject();
        client.Dispose();
        var sw = Stopwatch.StartNew();
        try
        {
            await server.DisposeAsync();
            stop["exception"] = null;
        }
        catch (Exception ex)
        {
            stop["exception"] = $"{ex.GetType().Name}: {ex.Message}";
        }

        stop["disposeMs"] = sw.ElapsedMilliseconds;
        stop["exitedCompletedAtDisposeReturn"] = server.Exited.IsCompleted;
        try
        {
            var exit = await server.Exited.WaitAsync(TimeSpan.FromSeconds(10));
            stop["exitedCompleted"] = true;
            stop["exit"] = exit.ToString();
            stop["exitKind"] = exit.Kind.ToString();
            stop["exitCode"] = exit.ExitCode;
            stop["exitSignal"] = exit.Signal;
        }
        catch (TimeoutException)
        {
            stop["exitedCompleted"] = false;
        }

        stop["stderrTail"] = StderrTail(output);
        await Task.Delay(TimeSpan.FromSeconds(3));
        var survivors = Survivors(tree);
        stop["survivorsAfter3s"] = ProcsJson(survivors);
        stop["cleanup"] = Cleanup(survivors, tree);
        return stop;
    }

    static async Task<JsonObject> UnshareNetAsync(Layout l)
    {
        var entry = new JsonObject();
        var command = Command("bwrap", l, unshareNet: true);
        entry["command"] = ToArray(command);
        var output = new OpenCodeServerOutput();
        var sw = Stopwatch.StartNew();
        OpenCodeServer server;
        try
        {
            server = await Start(command, l, output);
        }
        catch (Exception ex)
        {
            entry["started"] = false;
            entry["startMs"] = sw.ElapsedMilliseconds;
            entry["error"] = $"{ex.GetType().Name}: {ex.Message}";
            entry["stderrTail"] = StderrTail(output);
            return entry;
        }

        entry["started"] = true;
        entry["startMs"] = sw.ElapsedMilliseconds;
        var client = server.CreateClient();
        entry["info"] = await PollInfoAsync(client, TimeSpan.FromSeconds(15));
        var tree = Descendants(Snapshot(), server.ProcessId);
        entry["stop"] = await StopAsync(server, client, tree, output);
        return entry;
    }

    // ---------- hard kill ----------

    public static async Task<int> HoldAsync(string mode, string root)
    {
        var layout = LayoutFor(root);
        var output = new OpenCodeServerOutput();
        var server = await Start(Command(mode, layout, unshareNet: false), layout, output);
        var client = server.CreateClient();
        var info = await PollInfoAsync(client, TimeSpan.FromSeconds(30));
        Console.WriteLine($"READY {server.ProcessId} {info["pid"]}");
        Console.Out.Flush();
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    static async Task<JsonObject> HardKillAsync(string mode, Layout l)
    {
        var entry = new JsonObject();
        var psi = SelfStartInfo();
        psi.ArgumentList.Add("--hold");
        psi.ArgumentList.Add(mode);
        psi.ArgumentList.Add(l.Root);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        entry["holdCommand"] = ToArray([psi.FileName, .. psi.ArgumentList]);

        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderr = new StringBuilder();
        using var child = new Process { StartInfo = psi };
        child.OutputDataReceived += (_, e) =>
        {
            if (e.Data?.StartsWith("READY ", StringComparison.Ordinal) == true)
            {
                ready.TrySetResult(e.Data);
            }
        };
        child.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr) stderr.AppendLine(e.Data);
            }
        };
        child.Start();
        child.BeginOutputReadLine();
        child.BeginErrorReadLine();
        entry["holdPid"] = child.Id;

        string line;
        try
        {
            line = await ready.Task.WaitAsync(TimeSpan.FromSeconds(150));
        }
        catch (TimeoutException)
        {
            entry["ready"] = false;
            entry["holdStderr"] = stderr.ToString();
            var started = Descendants(Snapshot(), child.Id).Where(p => p.Pid != child.Id).ToList();
            try { child.Kill(entireProcessTree: false); } catch { }
            entry["cleanup"] = Cleanup(started, started);
            return entry;
        }

        entry["ready"] = true;
        entry["readyLine"] = line;
        var tree = Descendants(Snapshot(), child.Id).Where(p => p.Pid != child.Id).ToList();
        entry["treeBeforeKill"] = ProcsJson(tree);

        // Process.Kill sends SIGKILL on Linux and macOS and calls TerminateProcess on Windows.
        child.Kill(entireProcessTree: false);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        entry["holdExitCode"] = child.ExitCode;

        await Task.Delay(TimeSpan.FromSeconds(3));
        var survivors3 = Survivors(tree);
        entry["survivorsAfter3s"] = ProcsJson(survivors3);
        await Task.Delay(TimeSpan.FromSeconds(12));
        var survivors15 = Survivors(tree);
        entry["survivorsAfter15s"] = ProcsJson(survivors15);
        entry["cleanup"] = Cleanup(survivors15, tree);
        lock (stderr) entry["holdStderr"] = stderr.ToString();
        return entry;
    }

    static ProcessStartInfo SelfStartInfo()
    {
        var host = Environment.ProcessPath!;
        var psi = new ProcessStartInfo(host);
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }

        return psi;
    }

    // ---------- process table ----------

    static List<Proc> Snapshot()
    {
        var procs = new List<Proc>();
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo("powershell.exe");
            // An encoded command sidesteps Windows PowerShell's quote parsing.
            const string script = "[Console]::OutputEncoding = [Text.Encoding]::UTF8; "
                + "Get-CimInstance Win32_Process | ForEach-Object { \"{0}`t{1}`t{2}\" -f $_.ProcessId, $_.ParentProcessId, "
                + "$(if ($_.CommandLine) { $_.CommandLine -replace '[\\r\\n]+', ' ' } else { $_.Name }) }";
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            {
                psi.ArgumentList.Add(a);
            }
        }
        else
        {
            psi = new ProcessStartInfo("ps");
            foreach (var a in new[] { "-A", "-ww", "-o", "pid=,ppid=,pgid=,command=" })
            {
                psi.ArgumentList.Add(a);
            }
        }

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        foreach (var raw in text.Split('\n'))
        {
            var lineText = raw.TrimEnd('\r');
            if (OperatingSystem.IsWindows())
            {
                var parts = lineText.Split('\t', 3);
                if (parts.Length == 3 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var ppid))
                {
                    procs.Add(new Proc(pid, ppid, -1, parts[2]));
                }
            }
            else
            {
                var parts = lineText.Trim().Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var ppid) && int.TryParse(parts[2], out var pgid))
                {
                    procs.Add(new Proc(pid, ppid, pgid, parts.Length == 4 ? parts[3] : ""));
                }
            }
        }

        return procs;
    }

    // The watched set: the root the probe started and its descendants, found only by walking ppid links
    // down from the root. Never by process group: a child started with Process.Start shares the probe's
    // group, which holds the probe's callers (the shell, `dotnet run`, the CI step). The probe itself
    // and its ancestors are never in the set.
    static List<Proc> Descendants(List<Proc> all, int root)
    {
        var found = new Dictionary<int, Proc>();
        if (all.FirstOrDefault(p => p.Pid == root) is { } rootProc)
        {
            found[root] = rootProc;
        }

        var queue = new Queue<int>([root]);
        var visited = new HashSet<int>();
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!visited.Add(parent))
            {
                continue;
            }

            foreach (var p in all.Where(p => p.Ppid == parent && p.Pid != parent))
            {
                found[p.Pid] = p;
                queue.Enqueue(p.Pid);
            }
        }

        var protectedPids = SelfAndAncestors(all);
        return found.Values.Where(p => !protectedPids.Contains(p.Pid)).OrderBy(p => p.Pid).ToList();
    }

    // The probe's own pid and every ancestor up the ppid chain.
    static HashSet<int> SelfAndAncestors(List<Proc> all)
    {
        var byPid = all.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        var result = new HashSet<int>();
        var pid = Environment.ProcessId;
        while (pid > 0 && result.Add(pid) && byPid.TryGetValue(pid, out var p))
        {
            pid = p.Ppid;
        }

        return result;
    }

    // A process survives when its pid is alive with the same command line, which guards pid reuse.
    static List<Proc> Survivors(IEnumerable<Proc> earlier)
    {
        var now = Snapshot().ToDictionary(p => p.Pid);
        return earlier.Where(p => now.TryGetValue(p.Pid, out var q) && q.Cmd == p.Cmd).ToList();
    }

    // Kills only survivors that are in the watched set and are neither the probe nor its ancestor; each
    // kill ends that one process, not a tree. Anything else is recorded as skipped.
    static JsonArray Cleanup(List<Proc> survivors, List<Proc> watched)
    {
        var done = new JsonArray();
        var watchedPids = watched.Select(p => p.Pid).ToHashSet();
        var protectedPids = SelfAndAncestors(Snapshot());
        foreach (var p in survivors)
        {
            if (!watchedPids.Contains(p.Pid) || protectedPids.Contains(p.Pid))
            {
                done.Add((JsonNode?)JsonValue.Create($"skipped {p.Pid}: not in the watched set or the probe or its ancestor"));
                continue;
            }

            try
            {
                using var proc = Process.GetProcessById(p.Pid);
                proc.Kill(entireProcessTree: false);
                done.Add((JsonNode?)JsonValue.Create($"killed {p.Pid}"));
            }
            catch (Exception ex)
            {
                done.Add((JsonNode?)JsonValue.Create($"{p.Pid}: {ex.Message}"));
            }
        }

        return done;
    }

    static JsonObject TreeJson(List<Proc> tree, int launcherPid)
    {
        var showing = tree.Where(p => p.Cmd.Contains(AppendedTail, StringComparison.Ordinal)).ToList();
        var launcher = tree.FirstOrDefault(p => p.Pid == launcherPid);
        return new JsonObject
        {
            ["launcherPid"] = launcherPid,
            ["launcherCommand"] = launcher?.Cmd,
            ["processes"] = ProcsJson(tree),
            ["expectedTail"] = AppendedTail,
            ["pidsShowingTail"] = ToArray(showing.Select(p => p.Pid.ToString())),
            ["pidsEndingWithTail"] = ToArray(showing.Where(p => p.Cmd.TrimEnd().EndsWith(AppendedTail, StringComparison.Ordinal)).Select(p => p.Pid.ToString())),
            // True when a process other than the wrapper itself carries the tail unchanged after `serve`.
            ["serverShowsTail"] = showing.Any(p => !IsWrapper(p.Cmd)),
            ["afterServe"] = ToArray(tree.Select(p => AfterServe(p.Cmd)).OfType<string>().Distinct()),
        };
    }

    static bool IsWrapper(string cmd)
    {
        var first = cmd.TrimStart('"').Split(' ', 2)[0];
        var name = Path.GetFileName(first.TrimEnd('"'));
        return name is "bwrap" or "sandbox-exec" or "tini" or "dumb-init";
    }

    static string? AfterServe(string cmd)
    {
        var i = cmd.IndexOf(" serve", StringComparison.Ordinal);
        return i < 0 ? null : cmd[(i + 1)..];
    }

    static JsonArray ProcsJson(IEnumerable<Proc> procs)
    {
        var array = new JsonArray();
        foreach (var p in procs)
        {
            array.Add((JsonNode?)new JsonObject { ["pid"] = p.Pid, ["ppid"] = p.Ppid, ["pgid"] = p.Pgid, ["command"] = Shorten(p.Cmd) });
        }

        return array;
    }

    // The seatbelt profile is long; keep the process record readable.
    static string Shorten(string cmd) => cmd.Length > 600 ? cmd[..300] + " … " + cmd[^250..] : cmd;

    static JsonArray ToArray(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add((JsonNode?)JsonValue.Create(item));
        }

        return array;
    }

    // ---------- summary ----------

    static string Summary(JsonObject r)
    {
        string S(JsonNode? n) => n switch
        {
            null => "-",
            JsonValue v => Cell(v.ToString()),
            _ => Cell(n.ToJsonString()),
        };
        string Cell(string s)
        {
            s = s.Replace('\n', ' ').Replace('\r', ' ').Replace("|", "\\|");
            return s.Length > 60 ? s[..57] + "..." : s;
        }
        string Fence(string name)
        {
            var e = r["fence"]?[name];
            if (e is null)
            {
                return "-";
            }

            return name switch
            {
                "inside" or "escape" => (e["targetExists"]?.GetValue<bool>() == true ? "written" : "blocked") + $" rc={S(e["rc"])}",
                _ => $"{S(e["targetContent"])} rc={S(e["rc"])}",
            };
        }
        int Count(JsonNode? n) => n is JsonArray a ? a.Count : -1;

        var sb = new StringBuilder();
        sb.AppendLine($"### sandbox-probe: {r["mode"]} on {r["os"]}");
        sb.AppendLine();
        if (r["refused"] is { } refused)
        {
            sb.AppendLine($"Refused: {refused}");
            return sb.ToString();
        }

        if (r["skipped"] is { } skipped)
        {
            sb.AppendLine($"Skipped: {skipped}");
            return sb.ToString();
        }

        if (r["fatal"] is { } fatal)
        {
            sb.AppendLine($"Fatal: {Cell(fatal.ToString())}");
            sb.AppendLine();
        }

        var rows = new List<(string, string)>
        {
            ("started", S(r["readiness"]?["started"])),
            ("start / ready ms", $"{S(r["readiness"]?["startMs"])} / {S(r["readiness"]?["readyMs"])}"),
            ("start error", S(r["readiness"]?["error"])),
            ("version / info pid / launcher pid", $"{S(r["readiness"]?["info"]?["version"])} / {S(r["readiness"]?["info"]?["pid"])} / {S(r["readiness"]?["launcherPid"])}"),
            ("tree size", Count(r["processTree"]?["processes"]).ToString()),
            ("server shows `serve --stdio --port 0`", S(r["processTree"]?["serverShowsTail"])),
            ("fence inside", Fence("inside")),
            ("fence escape", Fence("escape")),
            ("fence git", Fence("git")),
            ("fence net", Fence("net")),
            ("stop ms / exception", $"{S(r["gracefulStop"]?["disposeMs"])} / {S(r["gracefulStop"]?["exception"])}"),
            ("stop exit", $"{S(r["gracefulStop"]?["exit"])} (Exited completed: {S(r["gracefulStop"]?["exitedCompleted"])})"),
            ("stop survivors after 3 s", Count(r["gracefulStop"]?["survivorsAfter3s"]).ToString()),
            ("hard kill ready", S(r["hardKill"]?["ready"])),
            ("hard kill survivors 3 s / 15 s", $"{Count(r["hardKill"]?["survivorsAfter3s"])} / {Count(r["hardKill"]?["survivorsAfter15s"])}"),
        };
        if (r["unshareNet"] is { } un)
        {
            rows.Add(("--unshare-net started / info ok", $"{S(un["started"])} / {S(un["info"]?["ok"])}"));
            rows.Add(("--unshare-net error", S(un["error"] ?? un["info"]?["lastError"])));
        }

        sb.AppendLine("| measure | value |");
        sb.AppendLine("|---|---|");
        foreach (var (k, v) in rows)
        {
            sb.AppendLine($"| {k} | {v} |");
        }

        return sb.ToString();
    }
}
