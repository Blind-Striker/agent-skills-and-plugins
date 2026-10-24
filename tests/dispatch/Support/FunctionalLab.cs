using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenCode.Sdk;
using OpenCode.Sdk.Models;

namespace Dispatch.Tests.Support;

internal sealed class FunctionalLab : IDisposable
{
    readonly CodexHome? codexHome;
    readonly string? codexProfiles;
    public TempTree Tree { get; }
    public Dictionary<string, string?> Environment { get; }
    public string Backend { get; }
    public string Route { get; }
    public string Tasks => Path.Combine(Tree.Root, "tasks.jsonc");
    public string LedgerRoot => Path.Combine(Tree.DispatchHome, "state", "ledgers");
    public string RunFolder => Directory.GetDirectories(LedgerRoot).Single();
    public string Run => Path.GetFileName(RunFolder);
    public string TaskFolder => Path.Combine(RunFolder, "probe");
    public JsonObject Record => ReadObject(Path.Combine(TaskFolder, "task.json"));
    public JsonObject Result => ReadObject(Path.Combine(TaskFolder, "result.json"));
    public string Workspace => Record["workspace"]!["path"]!.GetValue<string>();
    public string Patch => File.ReadAllText(Path.Combine(TaskFolder, "diff.patch"));

    internal FunctionalLab(string backend, string route, TempTree tree, string? userCodexHome = null, string? temporaryRoot = null)
    {
        Tree = tree;
        Backend = backend;
        Route = route;
        Environment = Tree.ChildEnvironment();
        try
        {
            if (backend == "codex")
            {
                var userHome = userCodexHome ?? System.Environment.GetEnvironmentVariable("CODEX_HOME")
                    ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".codex");
                // The auth hardlink, not the checkout's volume, determines where this home can live.
                codexProfiles = Path.Combine(temporaryRoot ?? Path.GetTempPath(), $"dispatch-functional-codex-{Guid.NewGuid():N}");
                if (Worktree.IsInside(codexProfiles, userHome))
                    Skip.Test("Functional Codex home needs a temporary folder outside the user's Codex home.");
                try
                {
                    codexHome = CodexHome.CreateProbe(new(codexProfiles, null, new Dictionary<string, string>()),
                        userHome, Tree.Repository, project: "on", windowsSandbox: CodexHome.ReadWindowsSandbox(userHome));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Skip.Test($"Functional Codex home has no usable temporary folder on the auth volume ({exception.GetType().Name}).");
                    throw;
                }
                if (!codexHome.Temporary)
                    Skip.Test($"Functional Codex home requires a usable temporary folder on the auth volume and hardlinked file auth: {codexHome.Reason}");
                Environment["CODEX_HOME"] = codexHome.Home;
            }
            else
            {
                // Keep the credential database in place; worker configuration is a temporary clean copy.
                Environment["XDG_DATA_HOME"] = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                    ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local", "share");
                // The harness reads provider/model definitions here and writes its clean copy under control/profiles.
                Environment["OPENCODE_CONFIG_DIR"] = System.Environment.GetEnvironmentVariable("OPENCODE_CONFIG_DIR")
                    ?? Path.Combine(System.Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                        ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".config"), "opencode");
                Environment["OPENCODE_DISABLE_CLAUDE_CODE"] = "1";
            }
            var config = Directory.CreateDirectory(Path.Combine(Tree.DispatchHome, "config")).FullName;
            File.WriteAllText(Path.Combine(config, "config.jsonc"), new JsonObject
            {
                ["schemaVersion"] = 2,
                ["locations"] = new JsonObject { ["worktreeRoot"] = Tree.WorktreeRoot },
            }.ToJsonString());
        }
        catch
        {
            DisposeCodexHome();
            throw;
        }
    }

    public static async Task<FunctionalLab> CreateAsync(string backend, CancellationToken ct, bool requireSandbox = false)
    {
        if (System.Environment.GetEnvironmentVariable("DISPATCH_FUNCTIONAL") != "1")
            Skip.Test("Real backends require DISPATCH_FUNCTIONAL=1.");
        var variable = "DISPATCH_FUNCTIONAL_ROUTE_" + backend.ToUpperInvariant();
        var route = System.Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(route)) Skip.Test($"{variable} is unset.");
        if (backend == "opencode" && requireSandbox)
        {
            if (Ready.FenceKind is null) Skip.Test("opencode runs unfenced on Windows.");
            if (await Ready.FenceAsync(ct) is { } refusal) Skip.Test(refusal);
        }
        if (backend == "codex")
        {
            var source = System.Environment.GetEnvironmentVariable("CODEX_HOME")
                ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".codex");
            if (!File.Exists(Path.Combine(source, "auth.json")))
            {
                var variables = WorkerEnvironment.Snapshot();
                var login = await CodexProcess.CommandAsync(new(Path.Combine(source, "unused-profiles"), null, new Dictionary<string, string>()),
                    TestPaths.Harness, variables, version: false, ct);
                if (login.ExitCode != 0) Skip.Test("Codex is signed out; run codex login.");
            }
        }
        var tree = new TempTree();
        FunctionalLab lab;
        try { lab = new FunctionalLab(backend, route!, tree); }
        catch { tree.Dispose(); throw; }
        try
        {
            await LabRepo.CreateAsync(lab.Tree, ct);
            if (backend == "codex") await lab.CheckCodexAsync(requireSandbox, ct);
            else await lab.CheckOpenCodeAsync(ct);
            return lab;
        }
        catch { lab.Dispose(); throw; }
    }

    async Task CheckOpenCodeAsync(CancellationToken ct)
    {
        var root = Directory.CreateDirectory(Path.Combine(Tree.Root, "auth-probe")).FullName;
        var variables = Environment.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!);
        await using var host = await HarnessServer.StartAsync(false, new(root, null, variables), root,
            new("full", "off", "full", "on", "worktree"), ct);
        var where = new LocationSelector { Directory = Tree.Repository };
        var providers = await Ready.PollAsync(() => host.Client.Providers.ListProvidersAsync(new ProviderListRequest { Location = where }, cancellationToken: ct),
            TimeSpan.FromSeconds(60), "functional provider metadata", ct);
        var provider = providers.Providers.FirstOrDefault(x => x.Id == Route.Split('/')[0]);
        if (provider?.IntegrationId is not { } integrationId) return;
        var integrations = await Ready.PollAsync(() => host.Client.Integrations.ListIntegrationsAsync(new IntegrationListRequest { Location = where }, cancellationToken: ct),
            TimeSpan.FromSeconds(60), "functional sign-in metadata", ct);
        var integration = integrations.Integrations.Single(x => x.Id == integrationId);
        // Connection metadata, not the credential-list API: no keys or tokens are read or printed.
        if (integration.Methods.Count > 0 && integration.Connections.Count == 0)
            Skip.Test($"opencode provider {provider.Id} is signed out; connect its integration.");
    }

    async Task CheckCodexAsync(bool requireSandbox, CancellationToken ct)
    {
        var variables = Environment.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value!);
        await using var process = CodexProcess.Start(new(Path.Combine(Tree.Root, "preflight"), null, new Dictionary<string, string>()), Tree.Repository, variables);
        await using var rpc = new CodexRpc(process.Stdin, process.Stdout);
        await rpc.RequestAsync("initialize", new CodexInitializeParams(new("dispatch-functional", "1"), new(false, [])),
            CodexJson.Wire.CodexInitializeParams, cancellationToken: ct);
        await rpc.NotifyAsync("initialized", ct);
        var account = (await rpc.RequestAsync("account/read", new CodexGetAccountParams(), CodexJson.Wire.CodexGetAccountParams,
            cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexGetAccountResponse)!;
        if (account.Account is null || account.Account.Value.ValueKind == JsonValueKind.Null)
            Skip.Test("Codex is signed out; run codex login.");
        if (OperatingSystem.IsWindows() && requireSandbox)
        {
            var readiness = (await rpc.RequestAsync("windowsSandbox/readiness", JsonSerializer.Deserialize("null", CodexJson.Wire.JsonElement),
                CodexJson.Wire.JsonElement, cancellationToken: ct)).Deserialize(CodexJson.Wire.CodexWindowsSandboxReadinessResponse)!.Status;
            TestContext.Current!.Output.WriteLine($"windowsSandbox/readiness: {readiness}");
            if (readiness != "ready") Skip.Test($"Windows sandbox status is {readiness}; operator setup is required.");
        }
    }

    public JsonObject CodeTask(string prompt, string access = "full") => new()
    {
        ["id"] = "probe", ["kind"] = "code", ["backend"] = Backend, ["route"] = Route,
        ["base"] = "main", ["profile"] = Backend == "opencode" ? "clean" : "full", ["access"] = access, ["rawPrompt"] = true,
        ["prompt"] = prompt, ["scope"] = new JsonArray("inside.txt", "continued.txt", "started.txt"), ["timeoutMinutes"] = 3,
    };

    public void WriteTask(JsonObject task, JsonObject? policy = null)
    {
        var document = new JsonObject { ["schemaVersion"] = 2, ["run"] = "functional", ["tasks"] = new JsonArray(task) };
        if (policy is not null) document["policy"] = policy;
        File.WriteAllText(Tasks, document.ToJsonString());
    }

    public Task<CapturedResult> StartAsync(HarnessProcess process, CancellationToken ct) =>
        process.RunAsync(["run", Tasks, "--repo", Tree.Repository], ct, Environment, documentedEntry: true);

    public async Task<CapturedResult> CommandAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var process = new HarnessProcess(Tree);
        var result = await process.RunAsync(arguments, ct, Environment, documentedEntry: true);
        TestContext.Current!.Output.WriteLine($"dispatch {string.Join(' ', arguments)}: exit {result.ExitCode}\n{result.Stdout}{result.Stderr}");
        return result;
    }

    public async Task<CapturedResult> CompleteAsync(Task<CapturedResult> running)
    {
        var result = await running;
        TestContext.Current!.Output.WriteLine($"dispatch run: exit {result.ExitCode}\n{result.Stdout}{result.Stderr}");
        var failure = Directory.Exists(LedgerRoot) && Directory.GetDirectories(LedgerRoot).Length == 1
            && File.Exists(Path.Combine(TaskFolder, "result.json")) ? Result : null;
        if (IsSignedOut(result.Stdout) || IsSignedOut(result.Stderr) || IsSignedOut(failure?["executionError"]?.GetValue<string>() ?? ""))
            Skip.Test($"{Backend} is signed out; authenticate the requested route.");
        return result;
    }

    static bool IsSignedOut(string text) => new[] { "is signed out", "missing a login", "MissingCredential", "NoCredential", "invalid_api_key", "authentication_error", "\"status\":401" }
        .Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    public JsonObject[] Events()
    {
        var path = Path.Combine(RunFolder, "events.jsonl");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        return text[..(text.LastIndexOf('\n') + 1)].Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
    }

    public async Task<JsonObject> WaitForEventAsync(string type, Task<CapturedResult> running, CancellationToken ct,
        Func<JsonObject, bool>? predicate = null)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromMinutes(2))
        {
            if (Directory.Exists(LedgerRoot) && Directory.GetDirectories(LedgerRoot).Length == 1)
            {
                var path = Path.Combine(RunFolder, "events.jsonl");
                if (File.Exists(path) && Events().FirstOrDefault(x => x["type"]!.GetValue<string>() == type && (predicate?.Invoke(x) ?? true)) is { } found)
                    return found;
            }
            if (running.IsCompleted)
            {
                var result = await CompleteAsync(running);
                throw new InvalidOperationException($"run ended before event {type}: exit {result.ExitCode}");
            }
            await Task.Delay(100, ct);
        }
        throw new InvalidOperationException($"run did not produce event {type} within two minutes");
    }

    public async Task AssertDoneAsync(CapturedResult command, bool structured = false)
    {
        command.RequireSuccess("functional dispatch");
        await Assert.That(Result["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(Result["backend"]!.GetValue<string>()).IsEqualTo(Backend);
        await Assert.That(Result["routeRequested"]!.GetValue<string>()).IsEqualTo($"{Backend}:{Route}");
        // A live backend reports the route it ran; an unreported or substituted route is not done.
        await Assert.That(Result["routeVerified"]!.GetValue<bool>()).IsTrue();
        await Assert.That(Events().Last(x => x["type"]!.GetValue<string>() == "final")["data"]!["state"]!.GetValue<string>()).IsEqualTo("done");
        await Assert.That(File.Exists(Path.Combine(TaskFolder, "diff.patch"))).IsEqualTo(!structured);
    }

    public void PrintEvidence()
    {
        if (!Directory.Exists(LedgerRoot) || Directory.GetDirectories(LedgerRoot).Length != 1) return;
        var output = TestContext.Current!.Output;
        output.WriteLine($"Ledger: {RunFolder}");
        if (File.Exists(Path.Combine(TaskFolder, "task.json")))
            output.WriteLine($"Recorded profile: {Record["settings"]?["profile"]}");
        foreach (var file in new[] { Path.Combine(TaskFolder, "result.json"), Path.Combine(RunFolder, "events.jsonl"), Path.Combine(TaskFolder, "diff.patch") })
            if (File.Exists(file)) output.WriteLine($"{Path.GetFileName(file)}:\n{File.ReadAllText(file)}");
    }

    static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    void DisposeCodexHome()
    {
        try { codexHome?.Dispose(); }
        finally
        {
            if (codexProfiles is not null && Directory.Exists(codexProfiles))
                Directory.Delete(codexProfiles, recursive: true);
        }
    }

    public void Dispose()
    {
        try { PrintEvidence(); }
        finally
        {
            try { DisposeCodexHome(); }
            finally { Tree.Dispose(); }
        }
    }
}
