// One supervisor per run: cancel and collect check it, and only the holder appends events
// for running tasks.
sealed class SupervisorLock : IDisposable
{
    readonly FileStream _stream;

    SupervisorLock(FileStream stream) => _stream = stream;

    static string PathFor(string run) => Path.Combine(Locations.ControlDir(run), "supervisor.lock");

    public static SupervisorLock Acquire(string run)
    {
        Directory.CreateDirectory(Locations.ControlDir(run));
        try
        {
            var stream = new FileStream(PathFor(run), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            var pid = Encoding.UTF8.GetBytes(System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            stream.Write(pid);
            stream.Flush();
            return new SupervisorLock(stream);
        }
        catch (IOException)
        {
            throw new HarnessException($"run {run} is already supervised by another process");
        }
    }

    public static bool IsHeld(string run)
    {
        var path = PathFor(run);
        if (!File.Exists(path)) return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Dispose() => _stream.Dispose();
}
