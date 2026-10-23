using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

sealed record CodexProcessIdentity(int Pid, DateTime? StartTime, string? LinuxBootId = null, long? LinuxStartTicks = null)
{
    public bool Confirmable => OperatingSystem.IsLinux() ? LinuxBootId is not null && LinuxStartTicks is not null : StartTime is not null;
}

/// <summary>
/// Owns an app-server, its Windows job or optional Linux session, and observed descendants.
/// Descendant snapshots also track children that create their own Unix sessions. They cannot
/// discover a child orphaned before a snapshot, or prove an unrecorded escaped child has stopped.
/// A successful stop confirms only the recorded process identities and the Windows job.
/// Windows job assignment occurs after launch; children born before assignment can escape it.
/// </summary>
sealed class CodexProcess : IAsyncDisposable
{
    readonly Process process;
    readonly SafeFileHandle? job;
    readonly Task stderrDrain;
    readonly ErrorTail stderr = new();
    readonly object gate = new();
    readonly Dictionary<int, CodexProcessIdentity> descendants = [];
    readonly HashSet<int> unobservable = [];
    readonly List<string> errors = [];
    Task<InterruptResult>? stopping;
    bool disposed;
    bool trackingReliable = true;

    CodexProcess(Process process, SafeFileHandle? job, int? processGroup)
    {
        this.process = process;
        this.job = job;
        Id = process.Id;
        StartTime = process.StartTime.ToUniversalTime();
        Identity = ReadIdentity(Id, StartTime, File.ReadAllText);
        ProcessGroup = processGroup;
        stderrDrain = DrainStderrAsync();
    }

    public int Id { get; }
    public DateTime StartTime { get; }
    public CodexProcessIdentity Identity { get; }
    public int? ProcessGroup { get; }
    public bool HasExited => process.HasExited;
    public int? ExitCode => HasExited ? process.ExitCode : null;
    public string StderrTail => stderr.Text;
    public long StderrDroppedLines => stderr.DroppedLines;
    public Stream Stdin => process.StandardInput.BaseStream;
    public Stream Stdout => process.StandardOutput.BaseStream;
    public IReadOnlyList<CodexProcessIdentity> Descendants { get { lock (gate) return descendants.Values.ToArray(); } }
    internal Func<int, Process> OpenProcess { get; set; } = Process.GetProcessById;
    internal Func<int, DateTime?> QueryLimitedStartTime { get; set; } = ReadLimitedStartTime;
    internal Action<int, string>? DescendantUnobservable { get; set; }
    internal Func<string, string> ReadProcStat { get; set; } = File.ReadAllText;

    // Supervisor snapshots are best effort: a wrapper can exit before its children are observed.
    public void RefreshDescendants()
    {
        lock (gate)
        {
            var parents = ReadParents(ReadProcStat);
            var owners = new HashSet<int>();
            if (IsAlive(Identity, OpenProcess, QueryLimitedStartTime, ReadProcStat, confirmedOnly: true)) owners.Add(Id);
            foreach (var identity in descendants.Values)
                if (identity.Confirmable && IsAlive(identity, OpenProcess, QueryLimitedStartTime, ReadProcStat, confirmedOnly: true)) owners.Add(identity.Pid);
            bool added;
            do
            {
                added = false;
                foreach (var (pid, parent) in parents)
                {
                    if (!owners.Contains(parent) || owners.Contains(pid)) continue;
                    try
                    {
                        var start = ReadStartTime(pid, OpenProcess, QueryLimitedStartTime);
                        if (start is null) continue;
                        descendants[pid] = ReadIdentity(pid, start.Value, ReadProcStat);
                    }
                    catch (ArgumentException) { continue; } // Exited between the snapshot and lookup.
                    catch (InvalidOperationException) { continue; } // Exited before its start time was read.
                    catch (FileNotFoundException) { continue; }
                    catch (DirectoryNotFoundException) { continue; }
                    catch (IOException exception) when (OperatingSystem.IsLinux() && exception.HResult is 2 or 3) { continue; }
                    catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException)
                    {
                        descendants.TryAdd(pid, new(pid, null));
                        if (unobservable.Add(pid)) DescendantUnobservable?.Invoke(pid, exception.Message);
                        continue; // Keep the uncertain identity for stop reporting, never as ancestry proof.
                    }
                    owners.Add(pid);
                    added = true;
                }
            } while (added);
        }
    }

    internal void SetPendingStop(Task<InterruptResult> pendingStop)
    {
        lock (gate) stopping = pendingStop;
    }

    public Task<InterruptResult> StopAsync()
    {
        lock (gate)
        {
            if (stopping is null || stopping.IsCompleted && (!stopping.IsCompletedSuccessfully || !stopping.Result.Stopped))
                stopping = StopCoreAsync();
            return stopping;
        }
    }

    async Task<InterruptResult> StopCoreAsync()
    {
        trackingReliable = true;
        var groupKill = false;
        var treeKill = false;
        var killed = 0;
        TryRefreshDescendants();
        try { process.StandardInput.Close(); }
        catch (IOException exception) { RecordError("close stdin", exception); }
        await WaitAsync(TimeSpan.FromSeconds(5), rootOnly: true);

        if (OperatingSystem.IsWindows())
        {
            groupKill = true;
            if (!Native.TerminateJobObject(job!, 1)) RecordError("terminate job", new Win32Exception(Marshal.GetLastWin32Error()));
        }
        else if (ProcessGroup is { } group && HasGroupMember(group))
        {
            groupKill = true;
            Signal(-group, 15);
            if (!await WaitAsync(TimeSpan.FromSeconds(5), rootOnly: false)) Signal(-group, 9);
        }

        // Also used on Windows as a second net for children born before job assignment.
        foreach (var identity in Descendants.Reverse())
        {
            if (!IsAlive(identity, OpenProcess, QueryLimitedStartTime, ReadProcStat)) continue;
            if (KillRecorded(identity, OpenProcess, RecordError, ReadProcStat)) killed++;
        }
        if (!process.HasExited)
        {
            treeKill = true;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { treeKill = true; }
            catch (Win32Exception exception) { RecordError("kill tree", exception); }
            catch (AggregateException exception) { RecordError("kill tree", exception); }
        }
        var stopped = await WaitAsync(TimeSpan.FromSeconds(5), rootOnly: false) && trackingReliable;
        // An escaped process holding stderr must not make an unconfirmed stop hang forever.
        if (stopped && !await DrainFinishedAsync()) stopped = false;
        // Recheck after the drain too: Stopped must not outlive its identity-based evidence.
        TryRefreshDescendants();
        stopped = stopped && trackingReliable && TreeStopped();
        var exit = process.HasExited ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "running";
        return new(stopped, $"exit={exit}; processGroup={ProcessGroup?.ToString(CultureInfo.InvariantCulture) ?? "false"}; groupKill={groupKill}; treeKill={treeKill}; descendantsKilled={killed}; stderrDroppedLines={StderrDroppedLines}; {string.Join("; ", errors)}\nstderr:\n{StderrTail}");
    }

    async Task DrainStderrAsync()
    {
        var buffer = new byte[4096];
        while (await process.StandardError.BaseStream.ReadAsync(buffer) is var read && read != 0)
            stderr.Append(buffer.AsSpan(0, read));
    }

    async Task<bool> DrainFinishedAsync()
    {
        try { await stderrDrain.WaitAsync(TimeSpan.FromSeconds(5)); return true; }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        { RecordError("drain stderr", exception); return false; }
    }

    async Task<bool> WaitAsync(TimeSpan timeout, bool rootOnly)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            TryRefreshDescendants();
            if (rootOnly ? process.HasExited : TreeStopped()) return true;
            await Task.Delay(100);
        } while (clock.Elapsed < timeout);
        return rootOnly ? process.HasExited : TreeStopped();
    }

    void TryRefreshDescendants()
    {
        try { RefreshDescendants(); }
        catch (Exception exception) when (exception is IOException or Win32Exception or UnauthorizedAccessException)
        {
            trackingReliable = false;
            RecordError("descendant snapshot", exception);
        }
    }

    bool TreeStopped()
    {
        if (!process.HasExited || Descendants.Any(identity => IsAlive(identity, OpenProcess, QueryLimitedStartTime, ReadProcStat))) return false;
        if (job is null || job.IsClosed) return true;
        if (!Native.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Native.JobAccounting>(), IntPtr.Zero))
            return false;
        return accounting.ActiveProcesses == 0;
    }

    bool HasGroupMember(int group) => new[] { Identity }.Concat(Descendants)
        .Any(identity => identity.Confirmable && IsAlive(identity, OpenProcess, QueryLimitedStartTime, ReadProcStat, confirmedOnly: true) && Native.getpgid(identity.Pid) == group);

    static bool IsAlive(CodexProcessIdentity identity, Func<int, Process> openProcess, Func<int, DateTime?> limitedStartTime,
        Func<string, string>? readProcStat = null, bool confirmedOnly = false)
    {
        try
        {
            // A reparented Unix zombie is already dead, but may await reaping by init.
            if (OperatingSystem.IsLinux())
            {
                var read = readProcStat ?? File.ReadAllText;
                var fields = ProcFields(read($"/proc/{identity.Pid}/stat"));
                if (fields[0] == "Z") return false;
                if (!identity.Confirmable) return !confirmedOnly;
                try { return MatchesLinux(identity, fields, read); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { return !confirmedOnly; } // Missing boot metadata does not prove that the process exited.
            }
            var start = ReadStartTime(identity.Pid, openProcess, limitedStartTime);
            return start is not null && (identity.StartTime is null || start == identity.StartTime);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException exception) when (OperatingSystem.IsLinux() && exception.HResult is 2 or 3) { return false; }
        catch (IOException) when (OperatingSystem.IsLinux()) { return !confirmedOnly; } // Unreadable is not gone, nor proof of ownership.
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException)
        {
            if (OperatingSystem.IsLinux() && confirmedOnly) return false;
            // A PID absent from a fresh system snapshot is gone; denied access alone proves nothing.
            try { return ReadParents().ContainsKey(identity.Pid); }
            catch (Exception snapshotError) when (snapshotError is IOException or Win32Exception or UnauthorizedAccessException) { return true; }
        }
    }

    static string[] ProcFields(string stat) => stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

    static CodexProcessIdentity ReadIdentity(int pid, DateTime start, Func<string, string> read)
    {
        if (!OperatingSystem.IsLinux()) return new(pid, start);
        var fields = ProcFields(read($"/proc/{pid}/stat"));
        return new(pid, start, read("/proc/sys/kernel/random/boot_id").Trim(), long.Parse(fields[19], CultureInfo.InvariantCulture));
    }

    static bool MatchesLinux(CodexProcessIdentity identity, string[] fields, Func<string, string> read) =>
        identity.Confirmable && long.Parse(fields[19], CultureInfo.InvariantCulture) == identity.LinuxStartTicks
        && read("/proc/sys/kernel/random/boot_id").Trim() == identity.LinuxBootId;

    static DateTime? ReadStartTime(int pid, Func<int, Process> openProcess, Func<int, DateTime?> limitedStartTime)
    {
        try
        {
            using var child = openProcess(pid);
            return child.HasExited ? null : child.StartTime.ToUniversalTime();
        }
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException)
        { return limitedStartTime(pid); }
    }

    static DateTime? ReadLimitedStartTime(int pid)
    {
        if (!OperatingSystem.IsWindows()) throw new UnauthorizedAccessException("process creation time is unavailable");
        using var handle = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 87) return null; // No process with this PID.
            throw new Win32Exception(error);
        }
        return ReadHandleStartTime(handle);
    }

    static DateTime? ReadHandleStartTime(SafeProcessHandle handle)
    {
        if (!Native.GetExitCodeProcess(handle, out var exit)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (exit != 259) return null; // STILL_ACTIVE; an exit code of 259 remains conservatively live.
        if (!Native.GetProcessTimes(handle, out var creation, out _, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return DateTime.FromFileTimeUtc(creation);
    }

    static bool KillRecorded(CodexProcessIdentity identity, Func<int, Process> openProcess, Action<string, Exception> recordError,
        Func<string, string>? readProcStat = null)
    {
        try
        {
            // Never terminate a recycled PID when no creation time was observable.
            if (!identity.Confirmable) throw new UnauthorizedAccessException("process identity unavailable; cannot safely terminate by PID");
            try
            {
                using var child = openProcess(identity.Pid);
                if (child.HasExited) return false;
                if (OperatingSystem.IsLinux())
                {
                    var read = readProcStat ?? File.ReadAllText;
                    if (!MatchesLinux(identity, ProcFields(read($"/proc/{identity.Pid}/stat")), read)) return false;
                }
                else if (child.StartTime.ToUniversalTime() != identity.StartTime) return false;
                child.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() && exception is (Win32Exception or UnauthorizedAccessException or AggregateException))
            {
                // A sandbox identity may deny the rights requested by Process.Kill's tree walk.
                // Query and terminate through the same owned handle to avoid a PID reuse race.
                using var handle = Native.OpenProcess(0x1001, false, identity.Pid); // QUERY_LIMITED_INFORMATION | TERMINATE
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 87) return false;
                    throw new Win32Exception(error);
                }
                if (ReadHandleStartTime(handle) != identity.StartTime) return false;
                if (!Native.TerminateProcess(handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return true;
        }
        catch (ArgumentException) { return false; } // Already gone.
        catch (InvalidOperationException) { return false; } // Exit raced the kill.
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException exception) when (OperatingSystem.IsLinux() && exception.HResult is 2 or 3) { return false; }
        catch (IOException exception) when (OperatingSystem.IsLinux())
        { recordError($"kill descendant pid={identity.Pid}", exception); return false; }
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException or AggregateException)
        { recordError($"kill descendant pid={identity.Pid}", exception); return false; }
    }

    void Signal(int pid, int signal)
    {
        if (Native.kill(pid, signal) != 0 && Marshal.GetLastPInvokeError() != 3)
            RecordError("signal process group", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    void RecordError(string operation, Exception exception)
    {
        lock (gate) if (errors.Count < 20) errors.Add($"{operation}: {exception.Message}");
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        var stopped = await StopAsync();
        if (!stopped.Stopped) throw new HarnessException($"Codex stop unconfirmed: {stopped.Detail}");
        job?.Dispose();
        process.Dispose();
        disposed = true;
    }

    public static CodexProcess Start(BackendContext context, string directory, IReadOnlyDictionary<string, string> environment)
    {
        var executable = ResolveExecutable(context.Executable, environment);
        var sessionLauncher = ResolveSessionLauncher(environment);
        var batch = OperatingSystem.IsWindows() && Path.GetExtension(executable).ToLowerInvariant() is ".cmd" or ".bat";
        var start = new ProcessStartInfo(sessionLauncher ?? (batch ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : executable))
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        // setsid creates the Unix session before exec; snapshots still cover observed session escapees.
        if (OperatingSystem.IsWindows()) start.CreateNewProcessGroup = true;
        // Windows cannot CreateProcess a batch file. Only a trusted executable path and fixed literals reach cmd.
        if (batch) start.Arguments = $"/d /s /c \"\"{executable}\" app-server\"";
        else
        {
            if (sessionLauncher is not null)
            {
                start.ArgumentList.Add("--");
                start.ArgumentList.Add(executable);
            }
            start.ArgumentList.Add("app-server");
        }
        start.Environment.Clear();
        foreach (var (name, value) in environment) start.Environment[name] = value;
        foreach (var (name, value) in context.Passthrough) start.Environment[name] = value;
        SafeFileHandle? job = null;
        Process? process = null;
        try
        {
            if (OperatingSystem.IsWindows()) job = CreateJob();
            process = Process.Start(start) ?? throw new HarnessException("codex app-server did not start");
            if (job is not null)
            {
                if (!Native.AssignProcessToJobObject(job, process.SafeHandle)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            else if (Native.setpriority(0, process.Id, 10) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError());
            int? processGroup = null;
            if (sessionLauncher is not null)
            {
                // Process.Start can return before setsid runs. Never record an inherited group.
                if (!SpinWait.SpinUntil(() => Native.getpgid(process.Id) == process.Id || process.HasExited, TimeSpan.FromSeconds(5))
                    || Native.getpgid(process.Id) != process.Id)
                    throw new HarnessException("cannot confirm codex app-server session creation");
                processGroup = process.Id;
            }
            return new(process, job, processGroup);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or HarnessException)
        {
            job?.Dispose();
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                finally { process.Dispose(); }
            }
            throw new HarnessException($"cannot start codex app-server: {exception.Message}");
        }
    }

    static string? ResolveSessionLauncher(IReadOnlyDictionary<string, string> environment)
    {
        if (!OperatingSystem.IsLinux()) return null;
        try { return Executables.Resolve("setsid", environment.GetValueOrDefault("PATH") ?? ""); }
        catch (HarnessException) { return null; } // No util-linux: only observed descendants can be tracked.
    }

    static SafeFileHandle CreateJob()
    {
        var handle = Native.CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        var limits = new Native.JobLimits();
        limits.Basic.LimitFlags = 0x2000 | 0x20; // KILL_ON_JOB_CLOSE | PRIORITY_CLASS
        limits.Basic.PriorityClass = 0x4000; // BELOW_NORMAL_PRIORITY_CLASS
        if (Native.SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<Native.JobLimits>())) return handle;
        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        throw new Win32Exception(error);
    }

    static Dictionary<int, int> ReadParents(Func<string, string>? readProcStat = null)
    {
        var parents = new Dictionary<int, int>();
        if (OperatingSystem.IsWindows())
        {
            using var snapshot = Native.CreateToolhelp32Snapshot(2, 0);
            if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>() };
            if (!Native.Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do { parents[(int)entry.Pid] = (int)entry.Parent; } while (Native.Process32NextW(snapshot, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else if (OperatingSystem.IsLinux())
        {
            foreach (var path in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(path), out var pid)) continue;
                try
                {
                    var stat = (readProcStat ?? File.ReadAllText)(Path.Combine(path, "stat"));
                    var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                    parents[pid] = int.Parse(fields[1], CultureInfo.InvariantCulture);
                }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                // Linux reports raw errno in IOException.HResult. A proc entry can disappear
                // after enumeration or while stat is read: ENOENT / ESRCH both mean exited.
                catch (IOException exception) when (exception.HResult is 2 or 3) { continue; }
            }
        }
        else
        {
            var start = new ProcessStartInfo("/bin/ps") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-A", "-o", "pid=,ppid=,lstart=" }) start.ArgumentList.Add(argument);
            using var ps = Process.Start(start) ?? throw new IOException("cannot enumerate descendants with ps");
            var stderr = ps.StandardError.ReadToEndAsync();
            while (ps.StandardOutput.ReadLine() is { } line)
            {
                var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 2 && int.TryParse(fields[0], out var pid) && int.TryParse(fields[1], out var parent)) parents[pid] = parent;
            }
            ps.WaitForExit();
            if (ps.ExitCode != 0) throw new IOException($"cannot enumerate descendants: {stderr.GetAwaiter().GetResult()}");
        }
        return parents;
    }

    internal static string ResolveExecutable(string? configured, IReadOnlyDictionary<string, string> environment)
    {
        if (configured is not null && Path.IsPathFullyQualified(configured)
            && Path.GetExtension(configured).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            throw PowerShellOnly(configured);
        string? powershell = null;
        try
        {
            if (configured is not null)
            {
                if (!Path.IsPathFullyQualified(configured)) throw new HarnessException("backends.codex.executable must be absolute");
                return Executables.Resolve(configured);
            }
            var path = environment.GetValueOrDefault("PATH") ?? "";
            if (!OperatingSystem.IsWindows()) return Executables.Resolve("codex", path);
            var extensions = (environment.GetValueOrDefault("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(extension => new[] { ".com", ".exe", ".bat", ".cmd" }.Contains(extension, StringComparer.OrdinalIgnoreCase));
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var directory = entry.Trim('"');
                if (!Path.IsPathFullyQualified(directory)) continue;
                var script = Path.Combine(directory, "codex.ps1");
                if (powershell is null && File.Exists(script)) powershell = script;
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, "codex" + extension);
                    if (File.Exists(candidate)) return Executables.Resolve(candidate);
                }
            }
            if (powershell is null) throw new HarnessException("required executable is not on PATH: codex");
        }
        catch (HarnessException exception)
        {
            throw new HarnessException($"cannot start codex: {exception.Message}. Install with npm install -g @openai/codex@{CodexBackend.PinnedVersion} or set backends.codex.executable in user config");
        }
        throw PowerShellOnly(powershell);

        static HarnessException PowerShellOnly(string path) => new(
            $"codex resolves only to a PowerShell script ({path}); set backends.codex.executable to its .cmd or .exe");
    }

    public static async Task<ProcessResult> CommandAsync(BackendContext context, string directory, IReadOnlyDictionary<string, string> environment,
        bool version, CancellationToken ct)
    {
        var executable = ResolveExecutable(context.Executable, environment);
        var variables = WorkerEnvironment.Copy(environment);
        foreach (var (name, value) in context.Passthrough) variables[name] = value;
        if (OperatingSystem.IsWindows() && Path.GetExtension(executable).ToLowerInvariant() is ".cmd" or ".bat")
        {
            // ArgumentList applies CreateProcess quoting, not cmd's /s /c quoting. Only a trusted
            // executable path and these fixed literals enter the raw command line, as at app-server start.
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                Arguments = $"/d /s /c \"\"{executable}\" {(version ? "--version" : "login status")}\"",
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.Environment.Clear();
            foreach (var (name, value) in variables) start.Environment[name] = value;
            using var process = Process.Start(start) ?? throw new HarnessException("Codex preflight command did not start");
            using var output = new MemoryStream();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var stdout = process.StandardOutput.BaseStream.CopyToAsync(output, deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), stdout, stderr);
                return new(process.ExitCode, output.ToArray(), await stderr, false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { /* Exit raced cancellation. */ }
                await process.WaitForExitAsync(CancellationToken.None);
                ct.ThrowIfCancellationRequested();
                return new(-1, output.ToArray(), "Codex preflight command timed out", true);
            }
        }
        return await Proc.RunAsync(executable, version ? ["--version"] : ["login", "status"], directory, ct, TimeSpan.FromSeconds(30), environment: variables);
    }

    public static async Task<InterruptResult> StopRecordedAsync(JsonObject state, CancellationToken ct,
        Func<int, Process>? openProcess = null, Func<int, DateTime?>? limitedStartTime = null, Func<string, string>? readProcStat = null)
    {
        openProcess ??= Process.GetProcessById;
        limitedStartTime ??= ReadLimitedStartTime;
        bool Alive(CodexProcessIdentity identity) => IsAlive(identity, openProcess, limitedStartTime, readProcStat);
        var identities = new List<CodexProcessIdentity>();
        CodexProcessIdentity ReadRecord(JsonNode record, string startName) => new(record["pid"]!.GetValue<int>(), record[startName] is { } start
            ? DateTime.Parse(start.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime() : null,
            record["linuxBootId"]?.GetValue<string>(), record["linuxStartTicks"]?.GetValue<long>());
        if (state["pid"] is not null) identities.Add(ReadRecord(state, "processStartTime"));
        foreach (var child in state["descendants"]?.AsArray() ?? [])
            identities.Add(ReadRecord(child!, "startTime"));
        var errors = new List<string>();
        if (!OperatingSystem.IsWindows() && identities.Count > 0 && state["processGroup"]?.GetValue<bool>() == true)
        {
            var group = identities[0].Pid;
            if (identities.Any(identity => identity.Confirmable && IsAlive(identity, openProcess, limitedStartTime, readProcStat, confirmedOnly: true) && Native.getpgid(identity.Pid) == group))
                if (Native.kill(-group, 9) != 0 && Marshal.GetLastPInvokeError() != 3)
                    errors.Add($"kill recorded group: OS error {Marshal.GetLastPInvokeError()}");
        }
        foreach (var identity in identities.AsEnumerable().Reverse())
        {
            ct.ThrowIfCancellationRequested();
            if (!Alive(identity)) continue;
            KillRecorded(identity, openProcess, (operation, exception) => errors.Add($"{operation}: {exception.Message}"), readProcStat);
        }
        var deadline = Stopwatch.StartNew();
        while (identities.Any(Alive) && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(50, ct);
        return new(!identities.Any(Alive), $"recorded process recovery; {string.Join("; ", errors)}");
    }

    sealed class ErrorTail
    {
        readonly byte[] bytes = new byte[64 * 1024];
        readonly object gate = new();
        int head, count, newlines;
        long droppedLines;
        public long DroppedLines { get { lock (gate) return droppedLines; } }

        public void Append(ReadOnlySpan<byte> incoming)
        {
            lock (gate)
            {
                foreach (var value in incoming)
                {
                    if (count == bytes.Length) DropByte();
                    bytes[(head + count++) % bytes.Length] = value;
                    if (value == '\n') newlines++;
                    while (newlines + (value == '\n' ? 0 : 1) > 200)
                    {
                        while (bytes[head] != '\n') DropByte();
                        DropByte();
                    }
                }
            }
        }

        void DropByte()
        {
            if (bytes[head] == '\n') { newlines--; droppedLines++; }
            head = (head + 1) % bytes.Length;
            count--;
        }

        public string Text
        {
            get
            {
                lock (gate)
                {
                    var snapshot = new byte[count];
                    for (var index = 0; index < count; index++) snapshot[index] = bytes[(head + index) % bytes.Length];
                    var offset = 0;
                    // A byte cap can cut a UTF-8 scalar. Do not synthesize replacement characters at either end.
                    while (offset < snapshot.Length && (snapshot[offset] & 0xC0) == 0x80) offset++;
                    var chars = new char[snapshot.Length];
                    Encoding.UTF8.GetDecoder().Convert(snapshot.AsSpan(offset), chars, false, out _, out var written, out _);
                    return new string(chars, 0, written);
                }
            }
        }
    }

    // DllImport keeps the file-based app and source-including tests free of unsafe build settings.
    static class Native
    {
        // DWORD rights / BOOL inheritance / DWORD PID -> owned CloseHandle-safe process handle.
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
        // FILETIME is two DWORDs (one 64-bit value); all output storage belongs to the caller.
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exit);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(SafeProcessHandle process, uint exit);
        [StructLayout(LayoutKind.Sequential)]
        internal struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint LimitFlags;
            public nuint MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct JobLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct JobAccounting
        {
            public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessEntry
        {
            public uint Size, Usage, Pid;
            public nuint DefaultHeap;
            public uint ModuleId, Threads, Parent;
            public int Priority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? Executable;
        }

        [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref JobLimits limits, uint length);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, out JobAccounting accounting, uint length, IntPtr returnedLength);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);
        [DllImport("libc", ExactSpelling = true, SetLastError = true)]
        internal static extern int getpgid(int pid);
        [DllImport("libc", ExactSpelling = true, SetLastError = true)]
        internal static extern int kill(int pid, int signal);
        [DllImport("libc", ExactSpelling = true, SetLastError = true)]
        internal static extern int setpriority(int which, int who, int priority);
    }
}
