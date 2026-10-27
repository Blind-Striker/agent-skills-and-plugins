using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;
using Microsoft.Win32.SafeHandles;
using TUnit.Assertions.Enums;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class CodexProcessTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Windows_walk_discovers_a_child_born_between_the_old_snapshot_and_parent_termination(bool recovery, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        await WithWindowsWalkAsync(async (process, owner) =>
        {
            using var childTree = new TempTree();
            CodexProcess? child = null;
            var nativeOpen = process.NativeOpenProcess;
            var terminate = process.TerminateProcess;
            var handles = new Dictionary<SafeProcessHandle, int>();
            var trace = new List<string>();
            process.ReadSnapshot = _ => child is null ? new() : new() { [child.Id] = (owner.Id, child.StartTime) };
            process.NativeOpenProcess = (access, inherit, pid) =>
            {
                trace.Add($"open {pid}");
                var handle = nativeOpen(access, inherit, pid);
                handles.Add(handle, pid);
                return handle;
            };
            process.TerminateProcess = (handle, exit) =>
            {
                var pid = handles[handle];
                trace.Add($"terminate {pid}");
                if (pid == owner.Id)
                    // Birth is real; ancestry is scripted by ReadSnapshot, without a pipe-read wait in this callback.
                    child = CodexProcess.Start(Context(childTree), childTree.Repository, Scenario(childTree, "{\"hang\":true}"));
                using var holder = Process.GetProcessById(pid);
                var killed = terminate(handle, exit);
                holder.WaitForExit(5000);
                return killed;
            };
            try
            {
                var result = await StopWalkAsync(process, owner, recovery, ct);
                await Assert.That(child).IsNotNull();
                await Assert.That(trace).IsEquivalentTo(new[] { $"open {owner.Id}", $"terminate {owner.Id}", $"open {child!.Id}", $"terminate {child.Id}" }, CollectionOrdering.Matching);
                await Assert.That(child.HasExited).IsTrue();
                await Assert.That(result.Stopped).IsTrue();
                foreach (var handle in handles.Keys) await Assert.That(handle.IsClosed).IsTrue();
            }
            finally { if (child is not null) await child.DisposeAsync(); }
        }, ct);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Timeout(60_000)]
    public async Task Windows_walk_records_children_that_cannot_be_terminated(bool recovery, bool denyOpen, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        await WithWindowsWalkAsync(async (process, owner) =>
        {
            using var childTree = new TempTree();
            await using var child = CodexProcess.Start(Context(childTree), childTree.Repository, Scenario(childTree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
            using var reader = new StreamReader(child.Stdout);
            await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
            var nativeOpen = process.NativeOpenProcess;
            var terminate = process.TerminateProcess;
            var walking = false;
            var trace = new List<string>();
            process.ReadSnapshot = _ => walking ? new() { [child.Id] = (owner.Id, child.StartTime) } : new();
            process.NativeOpenProcess = (access, inherit, pid) =>
            {
                trace.Add($"open {pid}");
                if (pid == child.Id && denyOpen) throw new System.ComponentModel.Win32Exception(5, "injected child denial");
                return nativeOpen(access, inherit, pid);
            };
            process.TerminateProcess = (handle, exit) =>
            {
                if (!walking)
                {
                    walking = true;
                    trace.Add($"terminate {owner.Id}");
                    using var holder = Process.GetProcessById(owner.Id);
                    var killed = terminate(handle, exit);
                    holder.WaitForExit(5000);
                    return killed;
                }
                trace.Add($"terminate {child.Id}");
                Marshal.SetLastPInvokeError(5);
                return false;
            };
            var result = await StopWalkAsync(process, owner, recovery, ct);
            await Assert.That(result.Stopped).IsFalse();
            await Assert.That(child.HasExited).IsFalse();
            await Assert.That(owner.HasExited).IsTrue();
            await Assert.That(trace).IsEquivalentTo(denyOpen
                ? new[] { $"open {owner.Id}", $"terminate {owner.Id}", $"open {child.Id}" }
                : new[] { $"open {owner.Id}", $"terminate {owner.Id}", $"open {child.Id}", $"terminate {child.Id}" }, CollectionOrdering.Matching);
            var error = $"kill descendant pid={child.Id}: " + (denyOpen ? "injected child denial" : new System.ComponentModel.Win32Exception(5).Message);
            if (recovery)
                await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; pid={owner.Id}; recordedStartTime={owner.StartTime:O}; currentStartTime={owner.StartTime:O}; handle=alive; {error}; pid={child.Id}; recordedStartTime={child.StartTime:O}; currentStartTime={child.StartTime:O}; handle=alive");
            else
            {
                await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { owner.Id, child.Id }, CollectionOrdering.Matching);
                await Assert.That(process.Descendants.Single(identity => identity.Pid == child.Id).StartTime).IsEqualTo(child.StartTime);
                await Assert.That(result.Detail).IsEqualTo($"exit={process.ExitCode}; processGroup=false; groupKill=True; treeKill=False; descendantsKilled=1; stderrDroppedLines=0; {error}; pid={owner.Id}; recordedStartTime={owner.StartTime:O}; currentStartTime={owner.StartTime:O}; handle=alive; pid={child.Id}; recordedStartTime={child.StartTime:O}; currentStartTime={child.StartTime:O}; handle=alive\nstderr:\n");
            }
        }, ct);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Timeout(60_000)]
    public async Task Windows_failed_walk_snapshot_still_terminates_the_verified_process(bool recovery, bool nativeFailure, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        await WithWindowsWalkAsync(async (process, owner) =>
        {
            var walking = false;
            var terminate = process.TerminateProcess;
            var trace = new List<string>();
            process.TerminateProcess = (handle, exit) =>
            {
                trace.Add("terminate");
                walking = true;
                using var holder = Process.GetProcessById(owner.Id);
                var killed = terminate(handle, exit);
                holder.WaitForExit(5000);
                return killed;
            };
            process.ReadSnapshot = _ =>
            {
                // Refreshes succeed, but every snapshot within the walk fails, regardless of order.
                if (!walking && trace.Count == 0 && !owner.HasExited) return new();
                trace.Add("snapshot failed");
                if (nativeFailure) throw new System.ComponentModel.Win32Exception(unchecked((int)0xC0000001), "NtQuerySystemInformation injected failure");
                throw new IOException("injected walk snapshot failure");
            };
            var nativeOpen = process.NativeOpenProcess;
            process.NativeOpenProcess = (access, inherit, pid) =>
            {
                var handle = nativeOpen(access, inherit, pid);
                walking = true;
                return handle;
            };
            var result = await StopWalkAsync(process, owner, recovery, ct);
            await Assert.That(owner.HasExited).IsTrue();
            await Assert.That(result.Stopped).IsFalse();
            await Assert.That(trace.Take(2)).IsEquivalentTo(new[] { "terminate", "snapshot failed" }, CollectionOrdering.Matching);
            var error = $"walk descendant pid={owner.Id}: " + (nativeFailure ? "NtQuerySystemInformation injected failure" : "injected walk snapshot failure");
            if (recovery)
                await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; pid={owner.Id}; recordedStartTime={owner.StartTime:O}; currentStartTime={owner.StartTime:O}; handle=alive; {error}");
            else
            {
                var message = nativeFailure ? "NtQuerySystemInformation injected failure" : "injected walk snapshot failure";
                await Assert.That(result.Detail).IsEqualTo($"exit={process.ExitCode}; processGroup=false; groupKill=True; treeKill=False; descendantsKilled=1; stderrDroppedLines=0; {error}; descendant snapshot: {message}; descendant snapshot: {message}; pid={owner.Id}; recordedStartTime={owner.StartTime:O}; currentStartTime={owner.StartTime:O}; handle=alive\nstderr:\n");
            }
        }, ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Windows_walk_visits_each_pid_creation_pair_once_even_with_equal_time_cycles(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();
        const int first = int.MaxValue, second = int.MaxValue - 1;
        var trace = new List<string>();
        var handles = new Dictionary<SafeProcessHandle, int>();
        var current = 0;
        var result = await CodexProcess.StopRecordedAsync(new JsonObject { ["pid"] = self.Id, ["processStartTime"] = start.ToString("O") }, ct,
            nativeOpenProcess: (access, inherit, pid) =>
            {
                trace.Add($"open {pid}");
                // A regression must fail at the trace oracle rather than overflow the runner's stack.
                if (handles.Count == 8) throw new InvalidOperationException("walk exceeded deterministic fixture bound");
                var handle = OpenNativeProcess(access, inherit, self.Id);
                handles.Add(handle, pid);
                current = pid;
                return handle;
            }, terminateProcess: (handle, _) =>
            {
                trace.Add($"terminate {handles[handle]}");
                return true; // Valid native success with no effect: never terminate the runner.
            }, readSnapshot: _ =>
            {
                trace.Add($"snapshot {current}");
                return current == self.Id ? new() { [first] = (self.Id, start), [second] = (self.Id, start) }
                    : current == first ? new() { [self.Id] = (first, start) }
                    : new() { [first] = (second, start.AddTicks(1)) };
            });
        await Assert.That(trace).IsEquivalentTo(new[]
        {
            $"open {self.Id}", $"terminate {self.Id}", $"snapshot {self.Id}",
            $"open {first}", $"terminate {first}", $"snapshot {first}",
            $"open {second}", $"terminate {second}", $"snapshot {second}", $"open {first}",
        }, CollectionOrdering.Matching);
        await Assert.That(result.Stopped).IsFalse();
        await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; pid={self.Id}; recordedStartTime={start:O}; currentStartTime={start:O}; handle=alive");
        foreach (var handle in handles.Keys) await Assert.That(handle.IsClosed).IsTrue();
        await Assert.That(self.HasExited).IsFalse();
    }

    [Test]
    [Timeout(60_000)]
    public async Task Windows_walk_depth_cap_records_the_unwalked_identity_and_unknown_subtree(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native child walk only");
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();
        var opened = new List<int>();
        var current = self.Id;
        var result = await CodexProcess.StopRecordedAsync(new JsonObject { ["pid"] = self.Id, ["processStartTime"] = start.ToString("O") }, ct,
            openProcess: pid => opened.Count >= 64 ? throw new ArgumentException("fixture identities have exited after the walk") : Process.GetProcessById(pid),
            nativeOpenProcess: (access, inherit, pid) =>
            {
                opened.Add(pid);
                current = pid;
                return OpenNativeProcess(access, inherit, self.Id);
            }, terminateProcess: (_, _) => true,
            readSnapshot: _ => current == self.Id ? new() { [int.MaxValue] = (self.Id, start) }
                : current > int.MaxValue - 65 ? new() { [current - 1] = (current, start) } : new());
        await Assert.That(opened).IsEquivalentTo(new[] { self.Id }.Concat(Enumerable.Range(0, 63).Select(index => int.MaxValue - index)), CollectionOrdering.Matching);
        await Assert.That(result.Stopped).IsFalse();
        await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; pid={self.Id}; recordedStartTime={start:O}; currentStartTime={start:O}; handle=alive; walk descendant pid={int.MaxValue - 63}: child walk depth limit 64 reached; remaining descendants unknown");
        await Assert.That(self.HasExited).IsFalse();
    }

    static async Task WithWindowsWalkAsync(Func<CodexProcess, CodexProcess, Task> test, CancellationToken ct)
    {
        using var tree = new TempTree();
        using var ownerTree = new TempTree();
        const string scenario = "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}";
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, scenario));
        await using var owner = CodexProcess.Start(Context(ownerTree), ownerTree.Repository, Scenario(ownerTree, scenario));
        using var reader = new StreamReader(process.Stdout);
        using var ownerReader = new StreamReader(owner.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await ownerReader.ReadLineAsync(ct)).Contains("ready");
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        var nativeOpen = process.NativeOpenProcess;
        var terminate = process.TerminateProcess;
        process.ReadSnapshot = _ => new() { [owner.Id] = (process.Id, owner.StartTime) };
        process.RefreshDescendants();
        using (var root = Process.GetProcessById(process.Id))
        {
            root.Kill();
            await root.WaitForExitAsync(ct);
        }
        try { await test(process, owner); }
        finally
        {
            process.ReadSnapshot = snapshot;
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.NativeOpenProcess = nativeOpen;
            process.TerminateProcess = terminate;
        }
    }

    static Task<InterruptResult> StopWalkAsync(CodexProcess process, CodexProcess owner, bool recovery, CancellationToken ct) => recovery
        ? CodexProcess.StopRecordedAsync(new JsonObject { ["pid"] = owner.Id, ["processStartTime"] = owner.StartTime.ToString("O") }, ct,
            readSnapshot: process.ReadSnapshot, nativeOpenProcess: process.NativeOpenProcess, terminateProcess: process.TerminateProcess)
        : process.StopAsync();

    [Test]
    [Timeout(60_000)]
    public async Task Stop_check_does_not_reuse_a_snapshot_older_than_an_adopted_identity(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var tree = new TempTree();
        using var ownerTree = new TempTree();
        using var childTree = new TempTree();
        const string scenario = "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}";
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, scenario));
        await using var owner = CodexProcess.Start(Context(ownerTree), ownerTree.Repository, Scenario(ownerTree, scenario));
        await using var child = CodexProcess.Start(Context(childTree), childTree.Repository, Scenario(childTree, scenario));
        using var reader = new StreamReader(process.Stdout);
        using var ownerReader = new StreamReader(owner.Stdout);
        using var childReader = new StreamReader(child.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await ownerReader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await childReader.ReadLineAsync(ct)).Contains("ready");
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        process.ReadSnapshot = _ => new();
        await Assert.That((await process.StopAsync()).Stopped).IsTrue(); // Empty the job before isolating snapshot evidence.
        process.OpenProcess = pid => pid == process.Id ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        process.QueryLimitedStartTime = _ => process.StartTime;
        process.ReadSnapshot = _ => new() { [owner.Id] = (process.Id, owner.StartTime) };
        process.RefreshDescendants();
        process.OpenProcess = pid => pid == owner.Id || pid == child.Id
            ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        process.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5);
        var reads = new List<string>();
        process.ReadSnapshot = _ =>
        {
            if (reads.Count == 0)
            {
                // Pause an old refresh after capture, adopt with a newer refresh, then return the old bytes.
                reads.Add("old refresh");
                process.RefreshDescendants();
                return new();
            }
            reads.Add(reads.Count == 1 ? "adopting refresh" : "stop check");
            return reads.Count == 2
                ? new() { [owner.Id] = (process.Id, owner.StartTime), [child.Id] = (owner.Id, child.StartTime) }
                : new() { [child.Id] = (owner.Id, child.StartTime) };
        };
        try
        {
            var wait = typeof(CodexProcess).GetMethod("WaitAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var stopped = await (Task<bool>)wait.Invoke(process, [TimeSpan.Zero, false])!;
            await Assert.That(stopped).IsFalse();
            await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { owner.Id, child.Id }, CollectionOrdering.Matching);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == child.Id).StartTime).IsEqualTo(child.StartTime);
            await Assert.That(reads).IsEquivalentTo(new[] { "old refresh", "adopting refresh", "stop check", "stop check" }, CollectionOrdering.Matching);
            await Assert.That(child.HasExited).IsFalse();
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.ReadSnapshot = snapshot;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Windows_tree_termination_discovers_children_born_after_the_last_refresh(bool recovery, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native tree termination only");
        using var tree = new TempTree();
        using var descendantTree = new TempTree();
        using var unrelatedTree = new TempTree();
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        await using var unrelated = CodexProcess.Start(Context(unrelatedTree), unrelatedTree.Repository, Scenario(unrelatedTree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        var environment = Scenario(descendantTree, "{\"notify\":\"ready\",\"params\":{}}\n{\"expect\":\"spawn\"}\n{\"spawnChild\":true}\n{\"spawnChild\":true}\n{\"notify\":\"born\",\"params\":{}}\n{\"hang\":true}");
        await using var descendant = CodexProcess.Start(Context(descendantTree), descendantTree.Repository, environment);
        using var reader = new StreamReader(process.Stdout);
        using var descendantReader = new StreamReader(descendant.Stdout);
        using var unrelatedReader = new StreamReader(unrelated.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await descendantReader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await unrelatedReader.ReadLineAsync(ct)).Contains("ready");
        var snapshot = process.ReadSnapshot;
        ObservedProcess[] born = [];
        process.ReadSnapshot = read =>
        {
            var parents = snapshot(read).Where(pair => born.Any(identity => identity.Pid == pair.Key)).ToDictionary();
            parents[descendant.Id] = (process.Id, descendant.StartTime);
            if (born.Length > 0)
            {
                parents[int.MaxValue] = (descendant.Id, descendant.StartTime.AddTicks(-1)); // Older occupant: never open.
                parents[unrelated.Id] = (descendant.Id, descendant.StartTime.AddDays(1)); // Reused child: open, but never terminate.
            }
            return parents;
        };
        process.RefreshDescendants();
        await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { descendant.Id }, CollectionOrdering.Matching);
        using (var root = Process.GetProcessById(process.Id))
        {
            root.Kill();
            await root.WaitForExitAsync(ct);
        }
        var nativeOpen = process.NativeOpenProcess;
        var terminate = process.TerminateProcess;
        var handles = new Dictionary<int, SafeProcessHandle>();
        var trace = new List<string>();
        var childPid = 0;
        var grandchildPid = 0;
        process.NativeOpenProcess = (access, inherit, pid) =>
        {
            trace.Add($"open {pid}");
            var handle = nativeOpen(access, inherit, pid);
            handles.Add(pid, handle);
            if (pid == descendant.Id)
            {
                descendant.Stdin.Write(Encoding.UTF8.GetBytes("{\"method\":\"spawn\",\"params\":{}}\n"));
                descendant.Stdin.Flush();
                var ready = descendantReader.ReadLineAsync(ct).AsTask().GetAwaiter().GetResult();
                if (ready is null || !ready.Contains("born", StringComparison.Ordinal)) throw new InvalidOperationException("late children did not report birth");
                born = ObserveRecordedProcesses(environment);
                var parents = snapshot(File.ReadAllText);
                childPid = born.Single(identity => parents[identity.Pid].Parent == descendant.Id).Pid;
                grandchildPid = born.Single(identity => parents[identity.Pid].Parent == childPid).Pid;
            }
            return handle;
        };
        process.TerminateProcess = (handle, exit) =>
        {
            var pid = handles.Single(pair => ReferenceEquals(pair.Value, handle)).Key;
            trace.Add($"terminate {pid}");
            using var holder = Process.GetProcessById(pid);
            var killed = terminate(handle, exit);
            holder.WaitForExit(5000);
            return killed;
        };
        try
        {
            var result = recovery ? await CodexProcess.StopRecordedAsync(new JsonObject
            {
                ["descendants"] = new JsonArray(new JsonObject { ["pid"] = descendant.Id, ["startTime"] = descendant.StartTime.ToString("O") }),
            }, ct, readSnapshot: process.ReadSnapshot, nativeOpenProcess: process.NativeOpenProcess, terminateProcess: process.TerminateProcess)
                : await process.StopAsync();
            await Assert.That(trace).IsEquivalentTo(new[]
            {
                $"open {descendant.Id}", $"terminate {descendant.Id}", $"open {childPid}", $"terminate {childPid}",
                $"open {grandchildPid}", $"terminate {grandchildPid}", $"open {unrelated.Id}",
            }, CollectionOrdering.Matching);
            await Assert.That(result.Stopped).IsTrue();
            foreach (var identity in born) await Assert.That(IsAlive(identity)).IsFalse();
            foreach (var handle in handles.Values) await Assert.That(handle.IsClosed).IsTrue();
            await Assert.That(unrelated.HasExited).IsFalse();
            using var untouched = Process.GetProcessById(unrelated.Id);
            await Assert.That(untouched.StartTime.ToUniversalTime()).IsEqualTo(unrelated.StartTime);
        }
        finally
        {
            process.NativeOpenProcess = nativeOpen;
            process.TerminateProcess = terminate;
            process.ReadSnapshot = snapshot;
            await CleanupAsync(born);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Windows_walk_still_discovers_children_when_their_parent_exits_during_open(bool recovery, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows pinned process identity only");
        await WithWindowsWalkAsync(async (process, owner) =>
        {
            using var childTree = new TempTree();
            var environment = Scenario(childTree, "{\"spawnChild\":true}\n{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}");
            await using var child = CodexProcess.Start(Context(childTree), childTree.Repository, environment);
            using var reader = new StreamReader(child.Stdout);
            await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
            var grandchild = ObserveRecordedProcesses(environment).Single(identity => identity.Pid != child.Id);
            var nativeOpen = process.NativeOpenProcess;
            var walking = false;
            var opened = new List<int>();
            process.ReadSnapshot = _ => walking ? new()
            {
                [grandchild.Pid] = (child.Id, grandchild.StartTime),
                [child.Id] = (owner.Id, child.StartTime),
            } : new();
            process.NativeOpenProcess = (access, inherit, pid) =>
            {
                opened.Add(pid);
                var handle = nativeOpen(access, inherit, pid);
                walking = true;
                if (pid == child.Id)
                {
                    using var holder = Process.GetProcessById(pid);
                    holder.Kill();
                    holder.WaitForExit(5000);
                }
                return handle;
            };
            try
            {
                var result = await StopWalkAsync(process, owner, recovery, ct);
                await Assert.That(opened).IsEquivalentTo(new[] { owner.Id, child.Id, grandchild.Pid }, CollectionOrdering.Matching);
                await Assert.That(result.Stopped).IsTrue();
                await Assert.That(IsAlive(grandchild)).IsFalse();
                if (!recovery)
                    await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { owner.Id, child.Id, grandchild.Pid }, CollectionOrdering.Matching);
            }
            finally { await CleanupAsync([grandchild]); }
        }, ct);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Reused_descendant_pid_tracks_the_new_real_child(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) Skip.Test("Windows and Linux identity seams");
        using var tree = new TempTree();
        var environment = Scenario(tree, "{\"spawnChild\":true,\"setsid\":true}\n{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}");
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        var child = ObserveRecordedProcesses(environment).Single(identity => identity.Pid != process.Id);
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        var reused = false;
        process.ReadSnapshot = _ => new()
        {
            [child.Pid] = (process.Id, reused ? child.StartTime : child.StartTime.AddDays(1)),
            [process.Id] = (Environment.ProcessId, process.StartTime),
        };
        process.OpenProcess = pid => !reused && pid == child.Pid ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        process.QueryLimitedStartTime = pid => pid == child.Pid ? child.StartTime.AddDays(1) : limited(pid);
        process.ReadProcStat = path =>
        {
            var text = File.ReadAllText(path);
            if (reused || path != $"/proc/{child.Pid}/stat") return text;
            var split = text.LastIndexOf(')') + 2;
            var fields = text[split..].Split(' ');
            fields[19] = (long.Parse(fields[19], System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return text[..split] + string.Join(' ', fields);
        };
        // Linux records kernel metadata through ReadProcStat, not the denied Windows handle path.
        if (OperatingSystem.IsLinux()) process.OpenProcess = Process.GetProcessById;
        try
        {
            process.RefreshDescendants();
            var old = process.Descendants.Single(identity => identity.Pid == child.Pid);
            reused = true;
            process.RefreshDescendants();
            await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { child.Pid }, CollectionOrdering.Matching);
            var current = process.Descendants.Single(identity => identity.Pid == child.Pid);
            await Assert.That(current.StartTime).IsEqualTo(child.StartTime);
            await Assert.That(current).IsNotEqualTo(old);
            if (OperatingSystem.IsLinux())
            {
                await Assert.That(current.LinuxStartTicks).IsEqualTo(Support.ObservedProcess.Capture(child.Pid).StartTime);
                await Assert.That(getpgid(child.Pid)).IsEqualTo(child.Pid);
            }
            await Assert.That((await process.StopAsync()).Stopped).IsTrue();
            await Assert.That(IsAlive(child)).IsFalse();
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.ReadProcStat = File.ReadAllText;
            process.ReadSnapshot = snapshot;
            await CleanupAsync([child]);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Snapshot_handle_mismatch_is_not_recorded(bool limitedHandle, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot creation times only");
        using var tree = new TempTree();
        using var otherTree = new TempTree();
        var environment = Scenario(tree, "{\"spawnChild\":true}\n{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}");
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        await using var unrelated = CodexProcess.Start(Context(otherTree), otherTree.Repository, Scenario(otherTree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        using var reader = new StreamReader(process.Stdout);
        using var otherReader = new StreamReader(unrelated.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await otherReader.ReadLineAsync(ct)).Contains("ready");
        var child = ObserveRecordedProcesses(environment).Single(identity => identity.Pid != process.Id);
        var snapshot = process.ReadSnapshot;
        process.ReadSnapshot = _ => new()
        {
            [unrelated.Id] = (process.Id, unrelated.StartTime.AddDays(1)),
            [child.Pid] = (process.Id, child.StartTime),
            [process.Id] = (Environment.ProcessId, process.StartTime),
        };
        if (limitedHandle) process.OpenProcess = pid => pid == process.Id ? Process.GetProcessById(pid) : throw new System.ComponentModel.Win32Exception(5);
        try
        {
            process.RefreshDescendants();
            await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { child.Pid }, CollectionOrdering.Matching);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == child.Pid).StartTime).IsEqualTo(child.StartTime);
            await Assert.That((await process.StopAsync()).Stopped).IsTrue();
            await Assert.That(unrelated.HasExited).IsFalse();
            using var holder = Process.GetProcessById(unrelated.Id);
            await Assert.That(holder.StartTime.ToUniversalTime()).IsEqualTo(unrelated.StartTime);
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.ReadSnapshot = snapshot;
            await CleanupAsync([child]);
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Denied_identities_share_one_snapshot_per_refresh(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var tree = new TempTree();
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        using var reader = new StreamReader(process.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        var snapshots = 0;
        process.ReadSnapshot = _ =>
        {
            snapshots++;
            return new()
            {
                [int.MaxValue] = (process.Id, process.StartTime.AddSeconds(1)),
                [int.MaxValue - 1] = (process.Id, process.StartTime.AddSeconds(2)),
                [process.Id] = (Environment.ProcessId, process.StartTime),
            };
        };
        process.OpenProcess = pid => pid == process.Id ? Process.GetProcessById(pid) : throw new System.ComponentModel.Win32Exception(5);
        process.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5);
        try
        {
            process.RefreshDescendants();
            snapshots = 0;
            process.RefreshDescendants();
            await Assert.That(snapshots).IsEqualTo(1);
            await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { int.MaxValue, int.MaxValue - 1 }, CollectionOrdering.Matching);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == int.MaxValue).StartTime).IsEqualTo(process.StartTime.AddSeconds(1));
            await Assert.That(process.Descendants.Single(identity => identity.Pid == int.MaxValue - 1).StartTime).IsEqualTo(process.StartTime.AddSeconds(2));
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.ReadSnapshot = snapshot;
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Windows_termination_uses_one_owned_handle_after_managed_observation(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows native termination only");
        using var tree = new TempTree();
        var environment = Scenario(tree, "{\"spawnChild\":true}\n{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}");
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        var child = ObserveRecordedProcesses(environment).Single(identity => identity.Pid != process.Id);
        using var holder = Process.GetProcessById(child.Pid);
        SafeProcessHandle? opened = null, terminated = null;
        var trace = new List<string>();
        var exited = false;
        try
        {
            var result = await CodexProcess.StopRecordedAsync(new JsonObject
            {
                ["pid"] = child.Pid, ["processStartTime"] = child.StartTime.ToString("O"),
            }, ct, openProcess: pid => { trace.Add($"observe {pid}"); return Process.GetProcessById(pid); },
                nativeOpenProcess: (access, inherit, pid) =>
                {
                    trace.Add($"native-open {access:X} {inherit} {pid}");
                    return opened = process.NativeOpenProcess(access, inherit, pid);
                }, terminateProcess: (handle, exit) =>
                {
                    trace.Add($"terminate {exit}");
                    terminated = handle;
                    var killed = process.TerminateProcess(handle, exit);
                    exited = holder.WaitForExit(5000);
                    return killed;
                }, readSnapshot: _ => new()); // Isolate the original single-identity handle oracle; late trees are tested separately.
            await Assert.That(result.Stopped).IsTrue();
            await Assert.That(exited).IsTrue();
            await Assert.That(IsAlive(child)).IsFalse();
            await Assert.That(terminated).IsSameReferenceAs(opened);
            await Assert.That(opened!.IsClosed).IsTrue();
            await Assert.That(trace).IsEquivalentTo(new[]
            {
                $"observe {child.Pid}", $"native-open 1001 False {child.Pid}", "terminate 1",
                $"observe {child.Pid}", $"observe {child.Pid}",
            }, CollectionOrdering.Matching);
            await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; pid={child.Pid}; recordedStartTime={child.StartTime:O}; currentStartTime={child.StartTime:O}; handle=alive");
        }
        finally { await CleanupAsync([child]); }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Recorded_denied_identities_share_one_snapshot_per_pass(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        var start = DateTime.UnixEpoch;
        var snapshots = 0;
        var state = new JsonObject
        {
            ["pid"] = int.MaxValue, ["processStartTime"] = start.ToString("O"),
            ["descendants"] = new JsonArray(new JsonObject { ["pid"] = int.MaxValue - 1, ["startTime"] = start.ToString("O") }),
        };
        var result = await CodexProcess.StopRecordedAsync(state, ct,
            openProcess: _ => throw new System.ComponentModel.Win32Exception(5, "open denied"),
            limitedStartTime: _ => throw new System.ComponentModel.Win32Exception(5, "query denied"),
            readSnapshot: _ => { snapshots++; return new(); });
        await Assert.That(result.Stopped).IsTrue();
        await Assert.That(snapshots).IsEqualTo(3); // Kill pass, wait pass and independent final recheck.
        await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; observe pid={int.MaxValue - 1}: query denied; pid={int.MaxValue - 1}; recordedStartTime={start:O}; currentStartTime=unknown; snapshot=absent; observe pid={int.MaxValue}: query denied; pid={int.MaxValue}; recordedStartTime={start:O}; currentStartTime=unknown; snapshot=absent");
    }

    [Test]
    [Timeout(60_000)]
    public async Task Unconfirmed_disposal_retains_process_resources_for_confirmed_retry(CancellationToken ct)
    {
        using var tree = new TempTree();
        await using var process = CodexProcess.Start(Context(tree), tree.Repository,
            Scenario(tree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        using var reader = new StreamReader(process.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        var stop = new TaskCompletionSource<InterruptResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.SetPendingStop(stop.Task);
        var disposing = process.DisposeAsync().AsTask();
        stop.SetResult(new(false, "pid survives"));
        await Assert.That(async () => await disposing).ThrowsNothing();
        await Assert.That(process.HasExited).IsFalse();
        await Assert.That((await process.CleanupAsync()).Stopped).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Reused_windows_pid_is_not_terminated(bool recovery, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var tree = new TempTree();
        using var otherTree = new TempTree();
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, "{\"notify\":\"ready\",\"params\":{}}"));
        await using var unrelated = CodexProcess.Start(Context(otherTree), otherTree.Repository,
            Scenario(otherTree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        using var reader = new StreamReader(process.Stdout);
        using var otherReader = new StreamReader(unrelated.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        await Assert.That(await otherReader.ReadLineAsync(ct)).Contains("ready");
        var recorded = unrelated.StartTime.AddDays(1);
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        var trace = new List<string>();
        process.ReadSnapshot = _ => new()
        {
            [process.Id] = (Environment.ProcessId, process.StartTime),
            [unrelated.Id] = (process.Id, recorded),
        };
        process.OpenProcess = pid => pid == unrelated.Id ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        process.QueryLimitedStartTime = pid => pid == unrelated.Id ? recorded : limited(pid);
        process.RefreshDescendants();
        process.ReadSnapshot = _ => new() { [unrelated.Id] = (0, unrelated.StartTime) };
        Process Denied(int pid)
        {
            if (pid != unrelated.Id) return Process.GetProcessById(pid);
            trace.Add("open");
            throw new System.ComponentModel.Win32Exception(5);
        }
        DateTime? Limited(int pid)
        {
            trace.Add("limited");
            throw new System.ComponentModel.Win32Exception(5);
        }
        process.OpenProcess = Denied;
        process.QueryLimitedStartTime = Limited;
        try
        {
            var result = recovery ? await CodexProcess.StopRecordedAsync(new JsonObject
            {
                ["descendants"] = new JsonArray(new JsonObject { ["pid"] = unrelated.Id, ["startTime"] = recorded.ToString("O") }),
            }, ct, Denied, Limited, readSnapshot: process.ReadSnapshot) : await process.StopAsync();
            await Assert.That(result.Stopped).IsTrue();
            await Assert.That(unrelated.HasExited).IsFalse();
            using var holder = Process.GetProcessById(unrelated.Id);
            await Assert.That(holder.StartTime.ToUniversalTime()).IsEqualTo(unrelated.StartTime);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == unrelated.Id).StartTime).IsEqualTo(recorded);
            await Assert.That(trace.Count).IsGreaterThan(0);
            // Every open must be a liveness query, never an extra termination open.
            foreach (var pair in trace.Chunk(2))
                await Assert.That(pair).IsEquivalentTo(new[] { "open", "limited" }, CollectionOrdering.Matching);
            if (recovery) await Assert.That(trace).IsEquivalentTo(new[] { "open", "limited", "open", "limited", "open", "limited" }, CollectionOrdering.Matching);
            await Assert.That(result.Detail).Contains($"pid={unrelated.Id}; recordedStartTime={recorded:O}; currentStartTime={unrelated.StartTime:O}; snapshot=reused");
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.ReadSnapshot = snapshot;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [Timeout(60_000)]
    public async Task Denied_termination_then_snapshot_absence_confirms_stop(bool denyOpen, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var tree = new TempTree();
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, "{\"notify\":\"ready\",\"params\":{}}\n{\"expect\":\"exit\",\"reply\":{}}\n{\"exit\":0}"));
        using var reader = new StreamReader(process.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        using var holder = Process.GetProcessById(process.Id);
        var pid = process.Id;
        var start = process.StartTime;
        var trace = new List<string>();
        SafeProcessHandle? opened = null, terminated = null;
        var exited = false;
        void Deny()
        {
            process.Stdin.Write(Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"exit\",\"params\":{}}\n"));
            process.Stdin.Flush();
            exited = holder.WaitForExit(5000);
            Marshal.SetLastPInvokeError(5);
        }
        Process Denied(int _)
        {
            trace.Add("open");
            throw new System.ComponentModel.Win32Exception(5, "injected open denial");
        }
        DateTime? Limited(int _)
        {
            trace.Add("limited");
            throw new System.ComponentModel.Win32Exception(5, "injected query denial");
        }
        var result = await CodexProcess.StopRecordedAsync(new JsonObject { ["pid"] = pid, ["processStartTime"] = start.ToString("O") },
            ct, Denied, Limited, readSnapshot: _ =>
            {
                trace.Add("snapshot");
                return process.HasExited ? new() : new() { [pid] = (0, start) };
            }, nativeOpenProcess: (access, inherit, id) =>
            {
                trace.Add($"native-open {access:X} {inherit} {id}");
                if (denyOpen)
                {
                    Deny();
                    return new SafeProcessHandle(IntPtr.Zero, true);
                }
                return opened = OpenNativeProcess(access, inherit, id);
            }, terminateProcess: (handle, exit) =>
            {
                trace.Add($"terminate {exit}");
                terminated = handle;
                Deny();
                return false;
            });
        await Assert.That(exited).IsTrue();
        await Assert.That(result.Stopped).IsTrue();
        await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; observe pid={pid}: injected query denial; pid={pid}; recordedStartTime={start:O}; currentStartTime={start:O}; snapshot=alive; kill descendant pid={pid}: {new System.ComponentModel.Win32Exception(5).Message}; pid={pid}; recordedStartTime={start:O}; currentStartTime=unknown; snapshot=absent");
        var expected = new List<string> { "open", "limited", "snapshot", $"native-open 1001 False {pid}" };
        if (!denyOpen)
        {
            expected.Add("terminate 1");
            expected.Add("snapshot"); // N1: discover children after terminating their pinned parent.
            await Assert.That(terminated).IsSameReferenceAs(opened);
            await Assert.That(opened!.IsClosed).IsTrue();
        }
        expected.AddRange(["open", "limited", "snapshot", "open", "limited", "snapshot"]);
        await Assert.That(trace).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Failed_snapshot_is_shared_and_keeps_stop_uncertain_without_termination(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();
        var trace = new List<string>();
        var kills = new List<int>();
        var state = new JsonObject
        {
            ["pid"] = self.Id, ["processStartTime"] = start.ToString("O"),
            ["descendants"] = new JsonArray(new JsonObject { ["pid"] = int.MaxValue, ["startTime"] = start.ToString("O") }),
        };
        var result = await CodexProcess.StopRecordedAsync(state, ct,
            openProcess: pid => { trace.Add($"open {pid}"); throw new System.ComponentModel.Win32Exception(5, "open denied"); },
            limitedStartTime: pid => { trace.Add($"limited {pid}"); throw new System.ComponentModel.Win32Exception(5, "query denied"); },
            readSnapshot: _ => { trace.Add("snapshot"); throw new IOException("snapshot failed"); },
            nativeOpenProcess: (_, _, pid) => { kills.Add(pid); throw new InvalidOperationException("must not terminate an uncertain identity"); });
        await Assert.That(result.Stopped).IsFalse();
        await Assert.That(result.Detail).IsEqualTo($"recorded process recovery; observe pid={int.MaxValue}: query denied; snapshot pid={int.MaxValue}; recordedStartTime={start:O}: snapshot failed; observe pid={self.Id}: query denied; snapshot pid={self.Id}; recordedStartTime={start:O}: snapshot failed");
        await Assert.That(trace.Take(5)).IsEquivalentTo(new[] { $"open {int.MaxValue}", $"limited {int.MaxValue}", "snapshot", $"open {self.Id}", $"limited {self.Id}" }, CollectionOrdering.Matching);
        await Assert.That(trace[5]).IsEqualTo($"open {self.Id}");
        await Assert.That(kills).IsEmpty();
        await Assert.That(self.HasExited).IsFalse();
        await Assert.That(self.StartTime.ToUniversalTime()).IsEqualTo(start);
    }

    [Test]
    [Timeout(60_000)]
    public async Task Unreadable_handle_records_snapshot_identity(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows snapshot identities only");
        using var tree = new TempTree();
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, Scenario(tree, "{\"notify\":\"ready\",\"params\":{}}\n{\"hang\":true}"));
        using var reader = new StreamReader(process.Stdout);
        await Assert.That(await reader.ReadLineAsync(ct)).Contains("ready");
        const int child = int.MaxValue, grandchild = int.MaxValue - 1, replacementChild = int.MaxValue - 2;
        var start = process.StartTime.AddSeconds(1);
        var snapshot = process.ReadSnapshot;
        var limited = process.QueryLimitedStartTime;
        var diagnostics = new List<string>();
        var reused = false;
        process.OpenProcess = pid => pid == process.Id ? Process.GetProcessById(pid) : throw new System.ComponentModel.Win32Exception(5, "injected open denial");
        process.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5, "injected query denial");
        process.DescendantUnobservable = (pid, detail) => diagnostics.Add($"{pid}: {detail}");
        process.ReadSnapshot = _ => new()
        {
            [grandchild] = (child, start.AddSeconds(1)), // Child must prove ancestry before this entry is adopted.
            [child] = (process.Id, reused ? start.AddSeconds(2) : start),
            [process.Id] = (Environment.ProcessId, process.StartTime),
            [replacementChild] = (reused ? child : 0, start.AddSeconds(3)),
        };
        try
        {
            process.RefreshDescendants();
            await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { child, grandchild }, CollectionOrdering.Matching);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == child).StartTime).IsEqualTo(start);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == child).Confirmable).IsTrue();
            await Assert.That(process.Descendants.Single(identity => identity.Pid == grandchild).StartTime).IsEqualTo(start.AddSeconds(1));
            await Assert.That(diagnostics).IsEquivalentTo(new[]
            {
                $"{child}: injected query denial; snapshot identity recordedStartTime={start:O}",
                $"{grandchild}: injected query denial; snapshot identity recordedStartTime={start.AddSeconds(1):O}",
            }, CollectionOrdering.Matching);
            reused = true;
            process.RefreshDescendants();
            await Assert.That(process.Descendants.Select(identity => identity.Pid)).IsEquivalentTo(new[] { child, grandchild, replacementChild }, CollectionOrdering.Matching);
            await Assert.That(process.Descendants.Single(identity => identity.Pid == child).StartTime).IsEqualTo(start.AddSeconds(2));
            process.RefreshDescendants();
            await Assert.That(diagnostics).IsEquivalentTo(new[]
            {
                $"{child}: injected query denial; snapshot identity recordedStartTime={start:O}",
                $"{grandchild}: injected query denial; snapshot identity recordedStartTime={start.AddSeconds(1):O}",
                $"{child}: injected query denial; snapshot identity recordedStartTime={start.AddSeconds(2):O}",
                $"{replacementChild}: injected query denial; snapshot identity recordedStartTime={start.AddSeconds(3):O}",
            }, CollectionOrdering.Matching);
        }
        finally
        {
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limited;
            process.ReadSnapshot = snapshot;
        }
    }

    [Test]
    [Arguments(-3600)]
    [Arguments(0)]
    [Arguments(1)]
    [Timeout(60_000)]
    public async Task Snapshot_creation_time_rejects_reuse_but_keeps_later_unreadable_children(int secondsAfterParent, CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"notify":"ready","params":{}}
            """);
        await using var process = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(process.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        const int candidate = int.MaxValue;
        var unobservable = new List<int>();
        var snapshot = process.ReadSnapshot;
        var limitedStart = process.QueryLimitedStartTime;
        process.ReadSnapshot = _ => new()
        {
            [process.Id] = (Environment.ProcessId, process.StartTime),
            [candidate] = (process.Id, process.StartTime.AddSeconds(secondsAfterParent)),
        };
        process.OpenProcess = pid => pid == candidate ? throw new System.ComponentModel.Win32Exception(5) : Process.GetProcessById(pid);
        process.QueryLimitedStartTime = _ => throw new System.ComponentModel.Win32Exception(5);
        process.DescendantUnobservable = (pid, _) => unobservable.Add(pid);
        try
        {
            process.RefreshDescendants();
            await Assert.That(process.Descendants.Select(identity => identity.Pid).ToArray())
                .IsEquivalentTo(secondsAfterParent >= 0 ? [candidate] : Array.Empty<int>(), CollectionOrdering.Matching);
            await Assert.That(unobservable).IsEquivalentTo(secondsAfterParent >= 0 ? [candidate] : Array.Empty<int>(), CollectionOrdering.Matching);
            if (secondsAfterParent >= 0)
                await Assert.That(process.Descendants.Single(identity => identity.Pid == candidate).StartTime)
                    .IsEqualTo(OperatingSystem.IsWindows() ? process.StartTime.AddSeconds(secondsAfterParent) : (DateTime?)null);
        }
        finally
        {
            process.ReadSnapshot = snapshot;
            process.OpenProcess = Process.GetProcessById;
            process.QueryLimitedStartTime = limitedStart;
        }
    }

    [Test]
    [Timeout(60_000)]
    public async Task Windows_snapshot_reports_kernel_creation_time_and_independent_child_parent(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) Skip.Test("Windows system process snapshot only");
        using var tree = new TempTree();
        var environment = Scenario(tree, """
            {"notify":"ready","params":{}}
            """);
        await using var child = CodexProcess.Start(Context(tree), tree.Repository, environment);
        using var reader = new StreamReader(child.Stdout, Encoding.UTF8, false, leaveOpen: true);
        await Assert.That(await reader.ReadLineAsync(cancellationToken)).Contains("ready");
        using var current = Process.GetCurrentProcess();
        var snapshot = child.ReadSnapshot(File.ReadAllText);
        await Assert.That(snapshot[current.Id].CreationTime).IsEqualTo(current.StartTime.ToUniversalTime());
        await Assert.That(snapshot[child.Id].Parent).IsEqualTo(current.Id);
    }

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
        var snapshot = process.ReadSnapshot;
        process.ReadSnapshot = read => snapshot(read).ToDictionary(pair => pair.Key,
            pair => (pair.Value.Parent, children.Any(child => child.Pid == pair.Key) ? null : pair.Value.CreationTime));
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
            process.ReadSnapshot = snapshot;
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
        // macOS cannot read an exited process's start time and throws instead of reporting the exit.
        catch (System.ComponentModel.Win32Exception) when (OperatingSystem.IsMacOS()) { return false; }
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
            catch (System.ComponentModel.Win32Exception) when (OperatingSystem.IsMacOS()) { continue; } // Exit raced cleanup.
        }
    }

    // Match production's access rights and owned CloseHandle-safe process handle.
    [DllImport("kernel32.dll", EntryPoint = "OpenProcess", ExactSpelling = true, SetLastError = true)]
    static extern SafeProcessHandle OpenNativeProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    // pid_t getpgid(pid_t) / getsid(pid_t), borrowed process identities, no owned native memory.
    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    static extern int getpgid(int pid);
    [DllImport("libc", ExactSpelling = true, SetLastError = true)]
    static extern int getsid(int pid);
}
