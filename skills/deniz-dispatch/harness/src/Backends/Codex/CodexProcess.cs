using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

sealed record CodexProcessIdentity(int Pid, DateTime StartTime);

/// <summary>
/// Owns an app-server, its Windows job or optional Linux session, and observed descendants.
/// Descendant snapshots also track children that create their own Unix sessions. They cannot
/// discover a child orphaned before a snapshot, or prove an unrecorded escaped child has stopped.
/// A successful stop confirms only the recorded PID/start-time identities and the Windows job.
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
        ProcessGroup = processGroup;
        stderrDrain = DrainStderrAsync();
    }

    public int Id { get; }
    public DateTime StartTime { get; }
    public int? ProcessGroup { get; }
    public bool HasExited => process.HasExited;
    public int? ExitCode => HasExited ? process.ExitCode : null;
    public string StderrTail => stderr.Text;
    public long StderrDroppedLines => stderr.DroppedLines;
    public Stream Stdin => process.StandardInput.BaseStream;
    public Stream Stdout => process.StandardOutput.BaseStream;
    public IReadOnlyList<CodexProcessIdentity> Descendants { get { lock (gate) return descendants.Values.ToArray(); } }

    // Supervisor snapshots are best effort: a wrapper can exit before its children are observed.
    public void RefreshDescendants()
    {
        lock (gate)
        {
            var parents = ReadParents();
            var owners = new HashSet<int>();
            if (IsAlive(new(Id, StartTime))) owners.Add(Id);
            foreach (var identity in descendants.Values)
                if (IsAlive(identity)) owners.Add(identity.Pid);
            bool added;
            do
            {
                added = false;
                foreach (var (pid, parent) in parents)
                {
                    if (!owners.Contains(parent) || owners.Contains(pid)) continue;
                    try
                    {
                        using var child = Process.GetProcessById(pid);
                        if (child.HasExited) continue;
                        descendants[pid] = new(pid, child.StartTime.ToUniversalTime());
                        owners.Add(pid);
                        added = true;
                    }
                    catch (ArgumentException) { continue; } // Exited between the snapshot and lookup.
                    catch (InvalidOperationException) { continue; } // Exited before its start time was read.
                }
            } while (added);
        }
    }

    public Task<InterruptResult> StopAsync()
    {
        lock (gate) return stopping ??= StopCoreAsync();
    }

    async Task<InterruptResult> StopCoreAsync()
    {
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
            if (!IsAlive(identity)) continue;
            try
            {
                using var child = Process.GetProcessById(identity.Pid);
                if (child.StartTime.ToUniversalTime() != identity.StartTime) continue;
                child.Kill(entireProcessTree: true);
                killed++;
            }
            catch (ArgumentException) { continue; } // Already gone.
            catch (InvalidOperationException) { continue; } // Exit raced the kill.
            catch (Win32Exception exception) { RecordError("kill descendant", exception); }
            catch (AggregateException exception) { RecordError("kill descendant tree", exception); }
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
        if (!process.HasExited || Descendants.Any(IsAlive)) return false;
        if (job is null || job.IsClosed) return true;
        if (!Native.QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Native.JobAccounting>(), IntPtr.Zero))
            return false;
        return accounting.ActiveProcesses == 0;
    }

    bool HasGroupMember(int group) => new[] { new CodexProcessIdentity(Id, StartTime) }.Concat(Descendants)
        .Any(identity => IsAlive(identity) && Native.getpgid(identity.Pid) == group);

    static bool IsAlive(CodexProcessIdentity identity)
    {
        try
        {
            using var child = Process.GetProcessById(identity.Pid);
            if (child.HasExited || child.StartTime.ToUniversalTime() != identity.StartTime) return false;
            // A reparented Unix zombie is already dead, but may await reaping by init.
            if (OperatingSystem.IsLinux())
            {
                var stat = File.ReadAllText($"/proc/{identity.Pid}/stat");
                return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z";
            }
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (Win32Exception) { return true; } // Access failure is not proof of exit.
        catch (UnauthorizedAccessException) { return true; }
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
        try { await StopAsync(); }
        finally
        {
            job?.Dispose(); // KILL_ON_JOB_CLOSE covers exception and supervisor-exit paths too.
            process.Dispose();
            disposed = true;
        }
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

    static Dictionary<int, int> ReadParents()
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
                    var stat = File.ReadAllText(Path.Combine(path, "stat"));
                    var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                    parents[pid] = int.Parse(fields[1], CultureInfo.InvariantCulture);
                }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
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

    static string ResolveExecutable(string? configured, IReadOnlyDictionary<string, string> environment)
    {
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
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, "codex" + extension);
                    if (File.Exists(candidate)) return Executables.Resolve(candidate);
                }
            }
            throw new HarnessException("required executable is not on PATH: codex");
        }
        catch (HarnessException exception)
        {
            throw new HarnessException($"cannot start codex: {exception.Message}. Install with npm install -g @openai/codex@0.163.0 or set backends.codex.executable in user config");
        }
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
