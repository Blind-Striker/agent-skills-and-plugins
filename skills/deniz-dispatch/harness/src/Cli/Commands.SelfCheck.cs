static partial class Commands
{
    public static async Task<int> SelfCheckAsync(Args a, CancellationToken ct)
    {
        var checks = new List<ProbeCheck>();
        void Check(string name, bool ok, string detail)
        {
            checks.Add(new(name, ok, detail));
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
        }
        var repo = Path.GetFullPath(a.Get("repo") ?? throw new HarnessException("selfcheck needs --repo"));
        var layers = await ConfigLayers.LoadAsync(repo, new TaskFile(), null, a, ct);
        var allow = layers.Allow;
        Check("allow-list", true, allow.IsRestricted ? $"{allow.Count} opencode entries" : "no route restriction");
        Check("policy", true, layers.Policy.Describe());
        var worktree = Path.Combine(Locations.WorktreeRoot, $"selfcheck-{DateTime.Now:HHmmss}");
        var add = await Git.Default.RunAsync(repo, ct, "worktree", "add", "--detach", worktree, "HEAD");
        Check("worktree", add.ExitCode == 0, add.ExitCode == 0 ? worktree : add.Stderr.Trim());
        if (add.ExitCode != 0) return 1;
        try
        {
            var selected = BackendScope.Selected(a);
            await using var scope = new BackendScope([selected]);
            var backend = scope.Backends[selected];
            var probe = await backend.ProbeAsync(new(worktree, TaskStarter.RulesFor(layers.Policy)), ct);
            foreach (var check in probe.Checks ?? []) Check(check.Name, check.Ok, check.Detail);
            var listing = await backend.ListRoutesAsync(worktree, ct);
            var allowedRoutes = selected == "codex"
                ? (layers.Effective.Restrictions?.Routes?.Value ?? []).Where(x => x.StartsWith("codex:", StringComparison.Ordinal)).Select(CodexBackend.ParseRoute)
                : allow.Routes;
            foreach (var route in allowedRoutes)
            {
                var model = listing.Routes.FirstOrDefault(x => x.Route.Provider == route.Provider && x.Route.Model == route.Model);
                var manual = listing.Agents.Any(x => x.Manual && RouteRef.TryParse(x.Model ?? "", out var pinned, out _) && pinned!.Matches(route));
                var reason = model is null ? "not listed at this location" : !model.Enabled ? "listed but disabled"
                    : route.VariantOrDefault != "default" && !model.Variants.Contains(route.Variant!) ? "variant not listed" : manual ? "manual-class" : null;
                Check($"allowed route {route}", reason is null, reason ?? "listed");
            }
            foreach (var agent in selected == "opencode" ? allow.Agents : [])
            {
                var entry = listing.Agents.FirstOrDefault(x => x.Name == agent);
                Check($"allowed agent {agent}", entry?.Usable == true, entry?.Reason ?? (entry is null ? "agent not found at this location" : "listed"));
            }
        }
        catch (HarnessException ex) { Check("live", false, $"{ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            var remove = await Git.Default.RunAsync(repo, CancellationToken.None, "worktree", "remove", "--force", worktree);
            Check("worktree removed", remove.ExitCode == 0, remove.ExitCode == 0 ? worktree : remove.Stderr.Trim());
        }
        var failed = checks.Count(x => !x.Ok);
        Console.WriteLine(failed == 0 ? "selfcheck passed" : $"selfcheck: {failed} check(s) failed");
        return failed == 0 ? 0 : 1;
    }
}
