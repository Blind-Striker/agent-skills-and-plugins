using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class CodexProcessTests
{
    [Test]
    [Arguments(false, 2)]
    [Arguments(false, 3)]
    [Arguments(true, 2)]
    [Arguments(true, 3)]
    [Timeout(60_000)]
    public async Task Vanished_proc_liveness_read_confirms_stop_without_throwing(bool recovery, int errno, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux proc filesystem exit races only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"notify":"ready","params":{}}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        var reads = 0;
        var injected = false;
        string Read(string path)
        {
            if (path == $"/proc/{process.Id}/stat" && (recovery || ++reads == 2))
            {
                injected = true;
                throw new IOException("proc entry exited during liveness read", errno);
            }
            return File.ReadAllText(path);
        }
        process.ReadProcStat = Read;
        try
        {
            var stopped = recovery
                ? await CodexProcess.StopRecordedAsync(new JsonObject { ["pid"] = process.Id, ["processStartTime"] = process.StartTime.ToString("O") },
                    cancellationToken, readProcStat: Read)
                : await process.StopAsync();
            await Assert.That(injected).IsTrue();
            await Assert.That(stopped.Stopped).IsTrue();
            await Assert.That(stopped.Detail.Contains("descendant snapshot", StringComparison.Ordinal)).IsFalse();
        }
        finally { process.ReadProcStat = File.ReadAllText; }
    }

    [Test]
    [Arguments(false, "match")]
    [Arguments(true, "match")]
    [Arguments(false, "ticks")]
    [Arguments(true, "ticks")]
    [Arguments(false, "boot")]
    [Arguments(true, "boot")]
    [Arguments(false, "legacy")]
    [Arguments(true, "legacy")]
    [Arguments(false, "unreadable-boot")]
    [Arguments(true, "unreadable-boot")]
    [Timeout(60_000)]
    public async Task Linux_recorded_stop_uses_kernel_identity_not_observer_wall_clock(bool descendant, string identityKind, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux kernel process identities only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        var observed = Support.ObservedProcess.Capture(process.Id);
        var record = new JsonObject
        {
            ["pid"] = process.Id,
            [descendant ? "startTime" : "processStartTime"] = process.StartTime.AddMinutes(identityKind is "match" or "legacy" ? -1 : 0).ToString("O"),
        };
        if (identityKind != "legacy")
        {
            record["linuxBootId"] = identityKind == "boot" ? Guid.Empty.ToString() : File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
            record["linuxStartTicks"] = observed.StartTime + (identityKind == "ticks" ? 1 : 0);
        }
        var state = descendant ? new JsonObject { ["descendants"] = new JsonArray(record) } : record;
        if (!descendant) state["processGroup"] = true;

        var result = await CodexProcess.StopRecordedAsync(state, cancellationToken, readProcStat: path =>
            identityKind == "unreadable-boot" && path == "/proc/sys/kernel/random/boot_id"
                ? throw new FileNotFoundException("boot identity unavailable") : File.ReadAllText(path));

        await Assert.That(observed.IsAlive()).IsEqualTo(identityKind != "match");
        await Assert.That(result.Stopped).IsEqualTo(identityKind is not ("legacy" or "unreadable-boot"));
        if (identityKind == "legacy") await Assert.That(result.Detail).Contains("cannot safely terminate");
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Timeout(60_000)]
    public async Task Vanished_proc_entry_does_not_make_stop_unconfirmed(int errno, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux proc filesystem exit races only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"notify":"ready","params":{}}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        var injected = false;
        process.ReadProcStat = path =>
        {
            if (!injected)
            {
                injected = true;
                throw new IOException("proc entry exited during snapshot", errno);
            }
            return File.ReadAllText(path);
        };

        var stopped = await process.StopAsync();

        await Assert.That(injected).IsTrue();
        await Assert.That(stopped.Stopped).IsTrue();
        await Assert.That(stopped.Detail.Contains("descendant snapshot", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Unknown_identity_cannot_adopt_or_kill_children_after_pid_reuse(bool childInitiallyObservable, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true}
            {"spawnChild":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        var identities = ObserveRecordedProcesses(environment);
        var children = identities.Where(identity => identity.Pid != process.Id).ToArray();
        var unreadable = 0;
        var reused = false;
        process.DescendantUnobservable = (pid, _) => unreadable = pid;
        process.OpenProcess = pid =>
        {
            if (!reused && children.Any(identity => identity.Pid == pid))
            {
                // Only the first generation is observable in the initial snapshot.
                if (unreadable == 0 || pid == unreadable) throw new System.ComponentModel.Win32Exception(5);
                if (!childInitiallyObservable) throw new ArgumentException("process not present in this snapshot");
            }
            return Process.GetProcessById(pid);
        };
        process.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5);
        try
        {
            process.RefreshDescendants();
            await Assert.That(process.Descendants.Single(identity => identity.Pid == unreadable).StartTime).IsNull();
            await Assert.That(process.Descendants.Where(identity => children.Any(child => child.Pid == identity.Pid) && identity.Pid != unreadable)).IsEmpty();
            using (var wrapper = Process.GetProcessById(process.Id))
            {
                wrapper.Kill();
                await wrapper.WaitForExitAsync(cancellationToken);
            }
            // The lookup now represents an unrelated occupant of the recorded PID, without
            // relying on OS PID recycling. Its child becomes observable only after replacement.
            reused = true;
            process.RefreshDescendants();
            var adopted = process.Descendants.Where(identity => children.Any(child => child.Pid == identity.Pid) && identity.Pid != unreadable).ToArray();
            var state = new JsonObject
            {
                ["descendants"] = new JsonArray(process.Descendants.Select(identity => (JsonNode)new JsonObject
                {
                    ["pid"] = identity.Pid, ["startTime"] = identity.StartTime?.ToString("O"),
                }).ToArray()),
            };
            var stopped = await CodexProcess.StopRecordedAsync(state, cancellationToken);
            foreach (var child in children) await Assert.That(IsAlive(child)).IsTrue();
            await Assert.That(adopted).IsEmpty();
            await Assert.That(process.Descendants.Single(identity => identity.Pid == unreadable).StartTime).IsNull();
            await Assert.That(stopped.Stopped).IsFalse();
            await Assert.That(stopped.Detail).Contains("cannot safely terminate by PID");
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            await CleanupAsync(identities);
        }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Recorded_stop_rechecks_handle_creation_time_after_liveness_passes(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows limited process handles only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        var child = ObserveRecordedProcesses(environment).Single(identity => identity.Pid != process.Id);
        var recordedStart = child.StartTime.AddMinutes(-1);
        var state = new JsonObject
        {
            ["descendants"] = new JsonArray(new JsonObject { ["pid"] = child.Pid, ["startTime"] = recordedStart.ToString("O") }),
        };
        var limitedReads = 0;
        var opens = 0;
        Process Denied(int pid)
        {
            if (pid != child.Pid) throw new ArgumentException("unexpected PID");
            opens++;
            throw new System.ComponentModel.Win32Exception(5);
        }
        // Liveness observes the old identity; the actual termination handle belongs to its replacement.
        DateTime? LimitedStart(int _) => ++limitedReads == 1 ? recordedStart : child.StartTime;
        try
        {
            var stopped = await CodexProcess.StopRecordedAsync(state, cancellationToken, Denied, LimitedStart);
            await Assert.That(IsAlive(child)).IsTrue();
            await Assert.That(opens).IsGreaterThanOrEqualTo(2);
            await Assert.That(limitedReads).IsGreaterThanOrEqualTo(2);
            await Assert.That(stopped.Stopped).IsTrue();
        }
        finally { await CleanupAsync([child]); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Recorded_stop_uses_limited_access_without_killing_a_mismatched_identity(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows limited process handles only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        var child = ObserveRecordedProcesses(environment).Single(identity => identity.Pid != process.Id);
        var state = new JsonObject
        {
            ["descendants"] = new JsonArray(new JsonObject { ["pid"] = child.Pid, ["startTime"] = child.StartTime.AddMinutes(-1).ToString("O") }),
        };
        Process Denied(int pid) => pid == child.Pid ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        try
        {
            var mismatch = await CodexProcess.StopRecordedAsync(state, cancellationToken, Denied);
            await Assert.That(mismatch.Stopped).IsTrue();
            await Assert.That(IsAlive(child)).IsTrue();
            state["descendants"]![0]!["startTime"] = child.StartTime.ToString("O");
            var stopped = await CodexProcess.StopRecordedAsync(state, cancellationToken, Denied);
            await Assert.That(stopped.Stopped).IsTrue();
            await Assert.That(IsAlive(child)).IsFalse();
            await Assert.That((await process.StopAsync()).Stopped).IsTrue();
        }
        finally { await CleanupAsync([child]); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(180_000)]
    public async Task Start_uses_configured_executable_or_PATH_shim_with_exact_streams_and_environment(bool configured, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var shimFolder = Directory.CreateDirectory(Path.Combine(tree.Root, "shim with spaces & punctuation")).FullName;
        var shim = Path.Combine(shimFolder, OperatingSystem.IsWindows() ? "codex.cmd" : "codex");
        File.WriteAllText(shim, OperatingSystem.IsWindows()
            ? $"@echo off\r\n\"{Executable}\" %*\r\n"
            : $"#!/bin/sh\nexec \"{Executable}\" \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var environment = Scenario(tree, """
            {"expect":"roundtrip","params":{"text":"İyi günler 世界"},"reply":{"text":"İyi günler 世界"}}
            """);
        environment["PATH"] = shimFolder;
        environment["PATHEXT"] = ".EXE;.CMD";
        var context = new BackendContext(tree.Root, configured ? Executable : null,
            new Dictionary<string, string> { ["BACKEND_ONLY"] = "synthetic" });
        if (configured)
        {
            File.WriteAllText(shim, OperatingSystem.IsWindows() ? "@exit /b 91\r\n" : "#!/bin/sh\nexit 91\n");
        }
        await using var process = CodexProcess.Start(context, tree.Repository, environment);
        var request = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"roundtrip\",\"params\":{\"text\":\"İyi günler 世界\"}}\n");
        await process.Stdin.WriteAsync(request, cancellationToken);
        await process.Stdin.FlushAsync(cancellationToken);
        using var reader = new StreamReader(process.Stdout, new UTF8Encoding(false, true), false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("İyi günler 世界");
        var record = JsonNode.Parse(File.ReadAllText(Directory.GetFiles(environment["FAKE_CODEX_RECORD"], "start.json", SearchOption.AllDirectories).Single()))!;
        await Assert.That(record["argv"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray()).IsEquivalentTo(new[] { "app-server" });
        await Assert.That(record["cwd"]!.GetValue<string>()).IsEqualTo(tree.Repository);
        await Assert.That(record["environmentNames"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray()).Contains("BACKEND_ONLY");
        using (var child = Process.GetProcessById(record["pid"]!.GetValue<int>()))
            await Assert.That(child.PriorityClass).IsEqualTo(ProcessPriorityClass.BelowNormal);
        await Assert.That((await process.StopAsync()).Stopped).IsTrue();
        var received = File.ReadAllLines(Directory.GetFiles(environment["FAKE_CODEX_RECORD"], "wire.jsonl", SearchOption.AllDirectories).Single())
            .Select(line => JsonNode.Parse(line)!)
            .Single(line => line["direction"]!.GetValue<string>() == "in");
        await Assert.That(Convert.FromBase64String(received["raw"]!.GetValue<string>()).SequenceEqual(request)).IsTrue();
    }

    [Test]
    [Arguments(500, 20, 200)]
    [Arguments(100, 2000, 17)]
    [Arguments(1, 100000, 1)]
    [Timeout(180_000)]
    public async Task Stderr_keeps_only_the_bounded_tail(int lineCount, int lineWidth, int expectedLines, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var lines = Enumerable.Range(0, lineCount).Select(index => $"{index:D4}:" + new string('ü', lineWidth) + "\n").ToArray();
        var scenario = new JsonObject { ["stderr"] = string.Concat(lines) }.ToJsonString() + "\n"
            + "{\"notify\":\"ready\",\"params\":{}}";
        var environment = Scenario(tree, scenario);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        await Assert.That((await process.StopAsync()).Stopped).IsTrue();
        var tail = process.StderrTail;
        await Assert.That(Encoding.UTF8.GetByteCount(tail)).IsLessThanOrEqualTo(64 * 1024);
        await Assert.That(tail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length).IsEqualTo(expectedLines);
        await Assert.That(tail).EndsWith(lineWidth > 65536 ? new string('ü', 100) + "\n" : lines[^1]);
        if (lineWidth < 65536) await Assert.That(tail).DoesNotContain("0000:");
        await Assert.That(tail).DoesNotContain("�");
        await Assert.That(process.StderrDroppedLines).IsEqualTo(lineCount - expectedLines);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    [Timeout(180_000)]
    public async Task Stop_leaves_no_wrapper_child_or_grandchild_alive(bool ownSession, bool wrapperExits, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true,"setsid":SESSION}
            {"spawnChild":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """.Replace("SESSION", ownSession ? "true" : "false", StringComparison.Ordinal));
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        process.RefreshDescendants();
        var identities = ObserveRecordedProcesses(environment);
        await Assert.That(identities.Length).IsEqualTo(3);
        foreach (var identity in identities) await Assert.That(IsAlive(identity)).IsTrue();
        try
        {
            if (wrapperExits)
            {
                using var wrapper = Process.GetProcessById(process.Id);
                wrapper.Kill();
                await wrapper.WaitForExitAsync(cancellationToken);
            }
            var stopped = await process.StopAsync();
            await Assert.That(stopped.Stopped).IsTrue();
            foreach (var identity in identities) await Assert.That(IsAlive(identity)).IsFalse();
            await Assert.That((await process.StopAsync()).Stopped).IsTrue();
        }
        finally { await CleanupAsync(identities); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Stop_job_kills_an_early_orphan_before_any_refresh_on_Windows(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows job objects only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true,"orphan":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Contains("ready");
        var children = ObserveRecordedProcesses(environment).Where(identity => identity.Pid != process.Id).ToArray();
        try
        {
            var clock = Stopwatch.StartNew();
            while (!process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20, cancellationToken);
            await Assert.That(process.HasExited).IsTrue();
            await Assert.That(children.Length).IsEqualTo(1);
            await Assert.That(process.Descendants.Count).IsEqualTo(0);
            foreach (var child in children) await Assert.That(IsAlive(child)).IsTrue();
            var result = await process.StopAsync();
            foreach (var child in children) await Assert.That(IsAlive(child)).IsFalse();
            await Assert.That(result.Stopped).IsTrue();
        }
        finally { await CleanupAsync(children); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Stop_kills_all_descendants_launched_through_a_cmd_shim_on_Windows(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows batch shims only");
        using var tree = new TempTree();
        var directory = Directory.CreateDirectory(Path.Combine(tree.Root, "shim with spaces & punctuation")).FullName;
        var shim = Path.Combine(directory, "codex.cmd");
        File.WriteAllText(shim, $"@echo off\r\n\"{Executable}\" %*\r\n");
        var environment = Scenario(tree, """
            {"spawnChild":true,"orphan":true}
            {"spawnChild":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        var context = new BackendContext(tree.Root, shim, new Dictionary<string, string>());
        await using var process = CodexProcess.Start(context, tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Contains("ready");
        var identities = ObserveRecordedProcesses(environment);
        try
        {
            var clock = Stopwatch.StartNew();
            while (!process.HasExited && clock.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20, cancellationToken);
            await Assert.That(process.HasExited).IsTrue();
            await Assert.That(identities.Length).IsEqualTo(3);
            await Assert.That(identities.Count(IsAlive)).IsEqualTo(2);
            await Assert.That(process.Descendants.Count).IsEqualTo(0);
            var result = await process.StopAsync();
            foreach (var identity in identities) await Assert.That(IsAlive(identity)).IsFalse();
            await Assert.That(result.Stopped).IsTrue();
        }
        finally { await CleanupAsync(identities); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Linux_launch_creates_a_session_and_group_whose_signal_reaches_the_grandchild(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux()) Skip.Test("Linux setsid launcher only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true}
            {"spawnChild":true}
            {"termMarker":"$cwd/grandchild-term.txt"}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        // This case requires util-linux, rather than testing the documented no-setsid fallback.
        _ = Executables.Resolve("setsid", environment["PATH"]);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Contains("ready");
        var identities = ObserveRecordedProcesses(environment);
        try
        {
            await Assert.That(identities.Length).IsEqualTo(3);
            await Assert.That(process.ProcessGroup).IsEqualTo(process.Id);
            await Assert.That(getsid(process.Id)).IsEqualTo(process.Id);
            foreach (var identity in identities)
            {
                await Assert.That(IsAlive(identity)).IsTrue();
                await Assert.That(getpgid(identity.Pid)).IsEqualTo(process.Id);
            }
            var result = await process.StopAsync();
            foreach (var identity in identities) await Assert.That(IsAlive(identity)).IsFalse();
            await Assert.That(File.Exists(Path.Combine(tree.Repository, "grandchild-term.txt"))).IsTrue();
            await Assert.That(File.ReadAllText(Path.Combine(tree.Repository, "grandchild-term.txt"))).IsEqualTo("SIGTERM\n");
            await Assert.That(result.Detail).Contains("groupKill=True");
            await Assert.That(result.Stopped).IsTrue();
        }
        finally { await CleanupAsync(identities); }
    }

    [Test]
    [Timeout(180_000)]
    public async Task Launch_without_a_session_launcher_reports_no_group_and_stops_tracked_descendants(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"spawnChild":true}
            {"notify":"ready","params":{}}
            {"hang":true}
            """);
        environment["PATH"] = Directory.CreateDirectory(Path.Combine(tree.Root, "empty-path")).FullName;
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Contains("ready");
        var identities = ObserveRecordedProcesses(environment);
        try
        {
            await Assert.That(process.ProcessGroup).IsNull();
            await Assert.That(identities.Length).IsEqualTo(2);
            foreach (var identity in identities) await Assert.That(IsAlive(identity)).IsTrue();
            process.RefreshDescendants();
            var result = await process.StopAsync();
            foreach (var identity in identities) await Assert.That(IsAlive(identity)).IsFalse();
            await Assert.That(result.Stopped).IsTrue();
        }
        finally { await CleanupAsync(identities); }
    }

    [Test]
    public async Task Missing_executable_is_a_clear_setup_refusal()
    {
        using var tree = new TempTree();
        var missing = Path.Combine(tree.Root, "missing-codex");
        var context = new BackendContext(tree.Root, missing, new Dictionary<string, string>());
        var exception = await Assert.ThrowsAsync<HarnessException>(() => Task.Run(() =>
            CodexProcess.Start(context, tree.Repository, new Dictionary<string, string>())));
        await Assert.That(exception!.Message).Contains("codex");
        await Assert.That(exception.Message).Contains("npm install");
        await Assert.That(exception.Message).Contains(missing);
    }

    static BackendContext Context(TempTree tree) => new(tree.Root, Executable, new Dictionary<string, string>());

    static string Executable => Path.Combine(TestPaths.Tests, "Fakes", "FakeCodex", "bin",
        new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name, "net10.0", OperatingSystem.IsWindows() ? "codex.exe" : "codex");

    static Dictionary<string, string> Scenario(TempTree tree, string steps)
    {
        var scenarios = Directory.CreateDirectory(Path.Combine(tree.Root, "scenarios")).FullName;
        File.WriteAllText(Path.Combine(scenarios, "preflight.jsonl"), steps + "\n");
        var environment = tree.ChildEnvironment().Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        environment["FAKE_CODEX_SCENARIOS"] = scenarios;
        environment["FAKE_CODEX_RECORD"] = Path.Combine(tree.Root, "record");
        return environment;
    }

    sealed record ObservedProcess(int Pid, DateTime StartTime);

    static ObservedProcess[] ObserveRecordedProcesses(Dictionary<string, string> environment) =>
        Directory.GetFiles(environment["FAKE_CODEX_RECORD"], "start.json", SearchOption.AllDirectories)
            .Select(path => JsonNode.Parse(File.ReadAllText(path))!)
            .Select(record =>
            {
                var pid = record["pid"]!.GetValue<int>();
                try
                {
                    // Unix wall-clock start times can differ between observers. Capture them here,
                    // independently of the owner's list, just as subsequent survival checks do.
                    using var process = Process.GetProcessById(pid);
                    return new ObservedProcess(pid, process.StartTime.ToUniversalTime());
                }
                catch (ArgumentException) { return new ObservedProcess(pid, record["startTime"]!.GetValue<DateTime>()); } // Early wrapper already exited.
                catch (InvalidOperationException) { return new ObservedProcess(pid, record["startTime"]!.GetValue<DateTime>()); } // Exit raced observation.
            }).ToArray();

    static bool IsAlive(ObservedProcess identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() != identity.StartTime) return false;
            if (!OperatingSystem.IsLinux()) return true;
            var stat = File.ReadAllText($"/proc/{identity.Pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z";
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    static async Task CleanupAsync(IEnumerable<ObservedProcess> identities)
    {
        foreach (var identity in identities)
        {
            if (!IsAlive(identity)) continue;
            try
            {
                using var process = Process.GetProcessById(identity.Pid);
                if (process.StartTime.ToUniversalTime() != identity.StartTime) continue;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException) { continue; } // Exit raced cleanup.
            catch (InvalidOperationException) { continue; } // Exit raced cleanup.
        }
    }

    // pid_t getpgid(pid_t) / getsid(pid_t), borrowed process identities, no owned native memory.
    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    static extern int getpgid(int pid);
    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    static extern int getsid(int pid);
}
