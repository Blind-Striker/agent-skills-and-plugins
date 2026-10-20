static partial class Commands
{
    // Live check without inference: start, bus, listings at a worktree location, and a session
    // created with the policy's session rules and removed again. No prompt is ever sent.
    public static async Task<int> SelfCheckAsync(Args a, CancellationToken ct)
    {
        var checks = new List<(string Name, bool Ok, string Detail)>();
        void Check(string name, bool ok, string detail)
        {
            checks.Add((name, ok, detail));
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
        }

        var allow = AllowList.Load();
        Check("allow-list", true, $"{allow.Count} entries in {Locations.AllowListFile}");
        var policy = Policy.Load(a.Get("policy"));
        Check("policy", true, policy.Describe());
        var repo = Path.GetFullPath(a.Get("repo") ?? throw new HarnessException("selfcheck needs --repo"));
        var worktree = Path.Combine(Locations.WorktreeRoot, $"selfcheck-{DateTime.Now:HHmmss}");
        var add = await Git.RunAsync(repo, ct, "worktree", "add", "--detach", worktree, "HEAD");
        Check("worktree", add.ExitCode == 0, add.ExitCode == 0 ? worktree : add.Output.Trim());
        if (add.ExitCode != 0)
        {
            return 1;
        }

        try
        {
            var started = Stopwatch.StartNew();
            await using var host = await HarnessServer.StartAsync(subscribe: true, ct);
            Check("server", true, $"opencode {host.Version} pid {host.Pid} ready in {started.Elapsed.TotalSeconds:0.0}s (OPENCODE_DISABLE_FILEWATCHER=1, command {host.CommandDescription})");
            Check("pin", host.Version == Constants.PinnedUpstream, $"server {host.Version}, SDK pin {Constants.PinnedUpstream}");
            Check("bus", true, "EventServerConnected received before any session call");

            var catalog = await Catalog.LoadAsync(host.Client, worktree, TimeSpan.FromSeconds(90), ct);
            var pinned = catalog.Agents.Count(x => x.Model is not null);
            Check("agents", catalog.Agents.Count > 0, $"{catalog.Agents.Count} agents at the worktree, {pinned} pin a model, {catalog.Manual.Count} manual-class");
            Check("models", catalog.Models.Count > 0, $"{catalog.Models.Count} models, {catalog.Models.Count(x => x.Enabled)} enabled, {catalog.Models.Select(x => x.ProviderId).Distinct().Count()} providers");
            foreach (var route in allow.Routes)
            {
                Check($"allowed route {route}", catalog.CheckRoute(route) is null && !catalog.IsManualRoute(route), catalog.CheckRoute(route) ?? (catalog.IsManualRoute(route) ? "manual-class" : "listed"));
            }

            foreach (var agent in allow.Agents)
            {
                Check($"allowed agent {agent}", catalog.CheckAgent(agent) is null, catalog.CheckAgent(agent) ?? "listed");
            }

            var mcp = await Catalog.McpServerNamesAsync(host.Client, worktree, ct);
            var rules = policy.Rules();
            Check("mcp", true, $"{mcp.Count} MCP servers at the worktree, allowed: {string.Join(", ", mcp)}");

            // No Agent and no Model: the harness never sends Agent without Model.
            var created = await host.Client.Sessions.CreateSessionAsync(new SessionCreateRequest
            {
                Title = "dispatch selfcheck (no prompt)",
                Location = new LocationPublicRef { Directory = worktree },
                Permissions = rules,
                Metadata = Json.Metadata("selfcheck", "selfcheck"),
            }, cancellationToken: ct);
            var session = host.Client.Sessions.GetSessionClient(created.Session.Id);
            Check("session create", true, $"{created.Session.Id} project {created.Session.ProjectId}");

            var read = (await session.GetAsync(cancellationToken: ct)).Session;
            var stored = read.Permissions ?? [];
            var same = stored.Count == rules.Count && stored.Zip(rules).All(p => p.First == p.Second);
            Check("rules stored", same, $"{stored.Count} rules read back, {rules.Count} sent, order and content {(same ? "identical" : "differ")}");
            Check("location", Locations.Same(read.Location.Directory, worktree), read.Location.Directory);
            var active = await host.Client.Sessions.GetActiveAsync(cancellationToken: ct);
            Check("idle", !active.Active.ContainsKey(read.Id), $"session absent from GetActiveAsync ({active.Active.Count} active)");
            var pending = await session.ListRequestsAsync(cancellationToken: ct);
            var forms = await session.ListFormsAsync(cancellationToken: ct);
            Check("blockers", pending.Requests.Count == 0 && forms.Forms.Count == 0, $"{pending.Requests.Count} permission requests, {forms.Forms.Count} forms");

            await session.RemoveAsync(cancellationToken: ct);
            var gone = await session.GetAsync(OpenCodeRequestOptions.NoThrow, ct);
            Check("session remove", gone.IsError && gone.Error is SessionNotFoundError, $"read after remove: HTTP {gone.Status} {gone.Error?.Tag}");
        }
        catch (Exception ex) when (ex is OpenCodeException or HarnessException)
        {
            Check("live", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            var remove = await Git.RunAsync(repo, CancellationToken.None, "worktree", "remove", "--force", worktree);
            Check("worktree removed", remove.ExitCode == 0, remove.ExitCode == 0 ? worktree : remove.Output.Trim());
        }

        var failed = checks.Count(x => !x.Ok);
        Console.WriteLine(failed == 0 ? "selfcheck passed" : $"selfcheck: {failed} check(s) failed");
        return failed == 0 ? 0 : 1;
    }
}
