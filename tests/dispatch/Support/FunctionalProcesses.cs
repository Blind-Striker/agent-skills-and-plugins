using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Dispatch.Tests.Support;

internal static class FunctionalProcesses
{
    public static async Task<ObservedProcess[]> DescendantsAsync(int root, TempTree tree, CancellationToken ct)
    {
        var parents = new Dictionary<int, int>();
        if (OperatingSystem.IsWindows())
        {
            using var snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>() };
            if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastWin32Error());
            do { parents[(int)entry.Pid] = (int)entry.Parent; } while (Process32NextW(snapshot, ref entry));
            if (Marshal.GetLastWin32Error() != 18) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else
        {
            using var process = new CapturedProcess();
            var result = await process.RunAsync("ps", ["-A", "-o", "pid=,ppid="], tree.Root, tree.ChildEnvironment(), ct);
            result.RequireSuccess("observe worker descendants");
            foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                parents[int.Parse(fields[0], System.Globalization.CultureInfo.InvariantCulture)] = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        var owned = new HashSet<int> { root };
        while (true)
        {
            var children = parents.Where(x => owned.Contains(x.Value) && !owned.Contains(x.Key)).Select(x => x.Key).ToArray();
            if (children.Length == 0) break;
            owned.UnionWith(children);
        }
        var identities = new List<ObservedProcess>();
        foreach (var pid in owned.Where(x => x != root))
        {
            try { identities.Add(ObservedProcess.Capture(pid)); }
            catch (ArgumentException) { continue; } // Exited between snapshot and capture.
            catch (InvalidOperationException) { continue; } // Exited before its start time was read.
        }
        return identities.ToArray();
    }

    public static async Task<string> CommandLineAsync(ObservedProcess identity, TempTree tree, CancellationToken ct)
    {
        if (!identity.IsAlive()) return "";
        try
        {
            if (OperatingSystem.IsLinux()) return File.ReadAllText($"/proc/{identity.Pid}/cmdline").Replace('\0', ' ');
            using var process = new CapturedProcess();
            var result = OperatingSystem.IsWindows()
                ? await process.RunAsync("pwsh", ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                    $"Get-CimInstance Win32_Process -Filter 'ProcessId = {identity.Pid}' | Select-Object -ExpandProperty CommandLine"], tree.Root, tree.ChildEnvironment(), ct)
                : await process.RunAsync("ps", ["-p", identity.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "-o", "command="], tree.Root, tree.ChildEnvironment(), ct);
            if (!identity.IsAlive()) return "";
            result.RequireSuccess("observe marked worker shell");
            return result.Stdout;
        }
        catch (FileNotFoundException) { return ""; } // Exit raced the command-line read.
        catch (DirectoryNotFoundException) { return ""; }
        catch (IOException exception) when (OperatingSystem.IsLinux() && exception.HResult is 2 or 3) { return ""; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Entry
    {
        public uint Size, Usage, Pid;
        public nuint Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string? Name;
    }

    // Owned snapshot HANDLE; PROCESSENTRY32W is caller-owned and contains a fixed UTF-16 buffer.
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32FirstW(SafeFileHandle snapshot, ref Entry entry);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Process32NextW(SafeFileHandle snapshot, ref Entry entry);
}
