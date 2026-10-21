using System.Diagnostics;
using System.Text;

namespace Dispatch.Tests.Support;

internal sealed class HarnessProcess(TempTree tree) : IDisposable
{
    readonly CapturedProcess child = new();

    public Task<CapturedResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var childEnvironment = tree.ChildEnvironment();
        if (environment is not null)
        {
            foreach (var (name, value) in environment) childEnvironment[name] = value;
        }
        return child.RunAsync("dotnet", ["dispatch.cs", "--", .. arguments], TestPaths.Harness, childEnvironment, cancellationToken);
    }

    public void Kill() => child.Kill();

    public void Dispose() => child.Dispose();
}

internal sealed record CapturedResult(int ExitCode, string Stdout, string Stderr)
{
    public void RequireSuccess(string operation)
    {
        if (ExitCode != 0)
            throw new InvalidOperationException($"{operation} exited {ExitCode}\nstdout:\n{Stdout}\nstderr:\n{Stderr}");
    }
}

// Shared ownership for the harness, its build gate, and lab git. No shell expansion or merged streams.
internal sealed class CapturedProcess : IDisposable
{
    Process? process;

    public async Task<CapturedResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (process is not null) throw new InvalidOperationException("a process owner can only be started once");
        var start = new ProcessStartInfo(FindExecutable(executable))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var (name, value) in environment) start.Environment[name] = value;
        // Callers cannot accidentally opt a child back into persistent build servers.
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["UseSharedCompilation"] = "false";

        var child = Process.Start(start) ?? throw new InvalidOperationException($"could not start {executable}");
        process = child;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync(cancellationToken);
            return new CapturedResult(child.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            KillTree(child);
            await child.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
    }

    static string FindExecutable(string name)
    {
        var filename = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.GetFullPath(Path.Combine(directory, filename));
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"required executable is not on PATH: {filename}");
    }

    static void KillTree(Process child)
    {
        if (child.HasExited) return;
        try
        {
            child.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (child.HasExited)
        {
            // A normal exit raced the kill; it has already achieved the cleanup postcondition.
            return;
        }
    }

    public void Kill()
    {
        if (process is { } child) KillTree(child);
    }

    public void Dispose()
    {
        if (process is not { } child) return;
        KillTree(child);
        child.WaitForExit();
        child.Dispose();
        process = null;
    }
}
