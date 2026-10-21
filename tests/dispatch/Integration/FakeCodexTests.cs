using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
public sealed class FakeCodexTests
{
    [Test]
    [Timeout(180_000)]
    public async Task The_fake_replays_initialize_and_thread_start_and_records_exact_wire_bytes(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var executable = Path.Combine(TestPaths.Tests, "Fakes", "FakeCodex", "bin", configuration, "net10.0",
            OperatingSystem.IsWindows() ? "codex.exe" : "codex");
        await Assert.That(File.Exists(executable)).IsTrue();
        var environment = tree.ChildEnvironment();
        var home = Directory.CreateDirectory(Path.Combine(tree.Root, "codex-selftest")).FullName;
        environment["CODEX_HOME"] = home;
        var record = Path.Combine(tree.Root, "record");
        environment["FAKE_CODEX_SCENARIOS"] = Path.Combine(TestPaths.Tests, "fixtures", "scenarios", "codex", "handshake");
        environment["FAKE_CODEX_RECORD"] = record;
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = tree.Repository,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("app-server");
        start.Environment.Clear();
        foreach (var (name, value) in environment) start.Environment[name] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("fake did not start");
        using var cancellation = cancellationToken.Register(() => Kill(process));
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var reader = new StreamReader(process.StandardOutput.BaseStream, new UTF8Encoding(false, true), false);
        var initialize = "{\"id\":17,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"test-client\",\"version\":\"1\",\"title\":\"İyi günler 世界\"},\"capabilities\":{\"experimentalApi\":false}}}\r\n";
        var initialized = "{\"method\":\"initialized\"}\n";
        var thread = new JsonObject
        {
            ["id"] = "thread-request",
            ["method"] = "thread/start",
            ["params"] = new JsonObject
            {
                ["cwd"] = tree.Repository, ["model"] = "model-y", ["sandbox"] = "read-only",
                ["approvalPolicy"] = "on-request", ["approvalsReviewer"] = "user", ["ephemeral"] = true,
            },
        }.ToJsonString() + "\n";
        try
        {
            await SendAsync(initialize);
            var initializeLine = await reader.ReadLineAsync(cancellationToken);
            var initializedReply = JsonNode.Parse(initializeLine!)!.AsObject();
            await Assert.That(initializedReply.ContainsKey("jsonrpc")).IsFalse();
            await Assert.That(initializedReply["id"]!.GetValue<int>()).IsEqualTo(17);
            await Assert.That(initializedReply["result"]!["codexHome"]!.GetValue<string>()).IsEqualTo(home);
            await Assert.That(initializedReply["result"]!["userAgent"]!.GetValue<string>()).IsEqualTo("fake-codex");

            await SendAsync(initialized + thread);
            var threadLine = await reader.ReadLineAsync(cancellationToken);
            var threadReply = JsonNode.Parse(threadLine!)!.AsObject();
            await Assert.That(threadReply.ContainsKey("jsonrpc")).IsFalse();
            await Assert.That(threadReply["id"]!.GetValue<string>()).IsEqualTo("thread-request");
            await Assert.That(threadReply["result"]!["thread"]!["id"]!.GetValue<string>()).IsEqualTo("thread-main");
            await Assert.That(threadReply["result"]!["cwd"]!.GetValue<string>()).IsEqualTo(tree.Repository);
            await Assert.That(threadReply["result"]!["sandbox"]!["type"]!.GetValue<string>()).IsEqualTo("readOnly");
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
            await Assert.That(await stderr).IsEqualTo("");
            await Assert.That(process.ExitCode).IsEqualTo(0);
            await Assert.That(await reader.ReadToEndAsync(cancellationToken)).IsEqualTo("");

            var wire = File.ReadAllLines(Directory.GetFiles(record, "wire.jsonl", SearchOption.AllDirectories).Single())
                .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
            await Assert.That(wire.Length).IsEqualTo(5);
            var received = wire.Where(line => line["direction"]!.GetValue<string>() == "in").ToArray();
            await Assert.That(string.Join("\n", received.Select(line => line["raw"]!.GetValue<string>()))).IsEqualTo(
                string.Join("\n", new[] { initialize, initialized, thread }.Select(text => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)))));
            await Assert.That(received.Select(line => line["message"]!["method"]!.GetValue<string>()).ToArray())
                .IsEquivalentTo(new[] { "initialize", "initialized", "thread/start" });
            await Assert.That(wire.Count(line => line["unexpected"]!.GetValue<bool>())).IsEqualTo(0);
            var sent = wire.Where(line => line["direction"]!.GetValue<string>() == "out").ToArray();
            await Assert.That(string.Join("\n", sent.Select(line => line["raw"]!.GetValue<string>()))).IsEqualTo(
                string.Join("\n", new[] { initializeLine, threadLine }.Select(text => Convert.ToBase64String(Encoding.UTF8.GetBytes(text + "\n")))));
        }
        finally
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        async Task SendAsync(string text)
        {
            await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(text), cancellationToken);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        }
    }

    static void Kill(Process process)
    {
        if (process.HasExited) return;
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited)
        {
            // A concurrent normal exit already satisfied cleanup.
            return;
        }
    }
}
