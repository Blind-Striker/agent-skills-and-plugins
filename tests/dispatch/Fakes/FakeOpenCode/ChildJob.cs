using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Fixture containment for the SDK's silent-breakaway job: unlike a libuv server, this fake
// otherwise has no child job. Hold a non-inheritable, non-breakaway job before spawning tools.
internal sealed class ChildJob : SafeHandleZeroOrMinusOneIsInvalid
{
    ChildJob() : base(ownsHandle: true) { }

    // The job includes this process. Closing it in a using statement would kill the fake before
    // it returns its intended exit code. Keep it rooted until the OS closes it on process exit.
    static ChildJob? current;

    public static void HoldForProcess() => current ??= Create();

    static ChildJob? Create()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var job = CreateJobObjectW(IntPtr.Zero, null);
        try
        {
            if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
            if (SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using var self = Process.GetCurrentProcess();
            if (AssignProcessToJobObject(job, self.SafeHandle) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return job;
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    protected override bool ReleaseHandle() => CloseHandle(handle) != 0;

    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits
    {
        public long ProcessTime, JobTime;
        public uint Flags;
        public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses;
        public nuint Affinity;
        public uint Priority, Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    // No SECURITY_ATTRIBUTES: the job handle cannot be inherited and keep the job alive.
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    static extern ChildJob CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern int SetInformationJobObject(ChildJob job, int informationClass, ref ExtendedLimits information, uint length);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    static extern int AssignProcessToJobObject(ChildJob job, SafeProcessHandle process);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    static extern int CloseHandle(IntPtr handle);
}
