using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Win32.SafeHandles;
using OpenCode.Sdk;

namespace Dispatch.Tests.Support;

internal sealed record ObservedProcess(int Pid, long StartTime)
{
    public static ObservedProcess Capture(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return new(pid, ReadStartTime(process));
    }

    public bool IsAlive()
    {
        try
        {
            using var process = Process.GetProcessById(Pid);
            if (process.HasExited || ReadStartTime(process) != StartTime) return false;
            if (!OperatingSystem.IsLinux()) return true;
            var stat = File.ReadAllText($"/proc/{Pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0] != "Z";
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (IOException exception) when (OperatingSystem.IsLinux() && exception.HResult is 2 or 3) { return false; }
    }

    static long ReadStartTime(Process process)
    {
        if (!OperatingSystem.IsLinux()) return process.StartTime.ToUniversalTime().Ticks;
        var stat = File.ReadAllText($"/proc/{process.Id}/stat");
        return long.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[19], CultureInfo.InvariantCulture);
    }

    public bool? IsInJob()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var process = Process.GetProcessById(Pid);
        if (process.StartTime.ToUniversalTime().Ticks != StartTime) throw new InvalidOperationException("process identity changed");
        if (IsProcessInJob(process.SafeHandle, IntPtr.Zero, out var result) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result != 0;
    }

    public bool? IsInSdkJob()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var process = Process.GetProcessById(Pid);
        if (process.StartTime.ToUniversalTime().Ticks != StartTime) throw new InvalidOperationException("process identity changed");
        if (IsProcessInSpecificJob(process.SafeHandle, SdkJob(), out var result) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result != 0;
    }

    public static uint? SdkJobLimits()
    {
        if (!OperatingSystem.IsWindows()) return null;
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION is 144 bytes on Win64, 112 on Win32.
        // BasicLimitInformation.LimitFlags follows two LARGE_INTEGER time limits.
        var limits = new byte[IntPtr.Size == 8 ? 144 : 112];
        if (QueryInformationJobObject(SdkJob(), 9, limits, (uint)limits.Length, out _) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return BitConverter.ToUInt32(limits, 16);
    }

    // Diagnostic only, for the pinned SDK: query its actual job, not an enclosing runner's job.
    // This handle is borrowed; only the SDK owns it. No private SDK state is changed.
    static SafeHandle SdkJob()
    {
        var type = typeof(OpenCodeServer).Assembly.GetType("OpenCode.Sdk.Internal.KillOnCloseJob", throwOnError: true)!;
        var owner = type.GetProperty("ForCurrentProcess", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        return (SafeHandle)type.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    }

    public async Task CleanupAsync()
    {
        try
        {
            using var process = Process.GetProcessById(Pid);
            if (!IsAlive() || ReadStartTime(process) != StartTime) return;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException)
        {
            // No process has this pid any more; the cleanup postcondition already holds.
            return;
        }
        catch (InvalidOperationException) when (!IsAlive())
        {
            // Exit raced the kill; never treat an unrelated process as this test's child.
            return;
        }
    }

    public static (ObservedProcess Child, ObservedProcess Grandchild) Descendants(JsonNode data) =>
        (new(data["child"]!.GetValue<int>(), data["childStartTime"]!.GetValue<long>()),
         new(data["grandchild"]!.GetValue<int>(), data["grandchildStartTime"]!.GetValue<long>()));

    public static async Task CleanupRecordedChildrenAsync(string root)
    {
        foreach (var path in Directory.GetFiles(root, "children.*.json"))
        {
            var (child, grandchild) = Descendants(JsonNode.Parse(await File.ReadAllTextAsync(path))!);
            try { await grandchild.CleanupAsync(); }
            finally { await child.CleanupAsync(); }
        }
    }

    // HANDLE / optional job HANDLE / BOOL output; the process handle remains caller-owned.
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern int IsProcessInJob(SafeProcessHandle process, IntPtr job, out int result);

    [DllImport("kernel32.dll", EntryPoint = "IsProcessInJob", ExactSpelling = true, SetLastError = true)]
    static extern int IsProcessInSpecificJob(SafeProcessHandle process, SafeHandle job, out int result);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern int QueryInformationJobObject(SafeHandle job, int informationClass, [Out] byte[] information, uint length, out uint returnedLength);
}
