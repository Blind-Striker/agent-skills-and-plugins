// Mutating commands retain the same lock handle for their entire operation.
sealed class SupervisorLock : IDisposable
{
    readonly FileStream _stream;

    SupervisorLock(FileStream stream) => _stream = stream;

    public static SupervisorLock Acquire(string run) => AcquireFolder(Locations.RequireControlFolder(run));

    public static SupervisorLock AcquireFolder(string control) => TryAcquireFolder(control)
        ?? throw new HarnessException($"control folder {control} is already supervised by another process");

    public static SupervisorLock? TryAcquireFolder(string control)
    {
        var path = Path.Combine(control, "supervisor.lock");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new SupervisorLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException) when (Directory.Exists(control))
            {
                // A display probe must not turn a starting supervisor into a refusal.
                if (timer.Elapsed < TimeSpan.FromMilliseconds(200)) { Thread.Sleep(1); continue; }
                return null;
            }
        }
    }

    public static bool IsHeld(string run)
    {
        string control;
        try { control = Locations.ControlDir(run); }
        catch (HarnessException) { return false; }
        return IsFolderHeld(control);
    }

    public static bool IsFolderHeld(string control)
    {
        var path = Path.Combine(control, "supervisor.lock");
        if (!File.Exists(path)) return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Dispose() => _stream.Dispose();
}
