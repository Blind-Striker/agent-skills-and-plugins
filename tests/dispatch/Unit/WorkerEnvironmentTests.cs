namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class WorkerEnvironmentTests
{
    public static IEnumerable<(string Name, string Value, bool Removed)> Cases()
    {
        foreach (var name in new[] { "FAKE_TOKEN", "CLIENT_SECRET", "DB_PASSWORD", "DB_PASSWD", "API_KEY", "APIKEY", "ACCESS_KEY", "PRIVATE_KEY", "CREDENTIAL_FILE", "AUTH", "SSH_AUTH_SOCK", "PAT", "GH_TEST", "GITHUB_TEST", "NUGET_TEST", "OPENCODE_SERVER_TEST", "DB_PWD", "SMTP_PASS", "PASS", "GPG_PASSPHRASE", "SIGNING_KEY", "SSH_KEY", "KEY", "SENTRY_DSN", "DSN", "DATABASE_URL", "CONNECTION_STRING", "CONNECTIONSTRING", "ConnectionStrings__Main", "SESSION_COOKIE", "KUBECONFIG", "NETRC", "BASH_ENV", "ENV" })
            yield return (name, "synthetic", true);
        foreach (var name in new[] { "PWD", "OLDPWD", "PASSTHROUGH_MODE", "KEYBOARD_LAYOUT", "PATH", "HOME", "USERPROFILE", "DBUS_SESSION_BUS_ADDRESS" })
            yield return (name, "synthetic", false);
        yield return ("DATABASE_REPLICA", "postgres://user:pass@host", true);
        yield return ("OTHER", "prefix custom+scheme://user:pass@host/path", true);
        yield return ("OTHER", "postgres://user@host/path", false);
        yield return ("OTHER", "postgres://host:1234/path", false);
        foreach (var name in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy" })
        {
            yield return (name, "http://host:8080", false);
            yield return (name, "http://user:pass@host:8080", false);
        }
        yield return ("db_pwd", "synthetic", true);
        yield return ("session_cookie", "synthetic", true);
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task ScrubList_Table(string name, string value, bool removed)
    {
        var snapshot = new Dictionary<string, string> { [name] = value };
        var result = WorkerEnvironment.Scrub(snapshot);
        await Assert.That(result.Removed.Contains(name)).IsEqualTo(removed);
        await Assert.That(result.Variables.ContainsKey(name)).IsEqualTo(!removed);
        await Assert.That(snapshot[name]).IsEqualTo(value);
        if (!removed) await Assert.That(result.Variables[name]).IsEqualTo(value);
    }

    [Test]
    public async Task ScrubList_RepoLayerAddsPattern()
    {
        var result = WorkerEnvironment.Scrub(new Dictionary<string, string> { ["EXTRA_NAME"] = "synthetic", ["HTTPS_PROXY"] = "http://user:pass@host" }, ["^extra_", "^HTTPS_PROXY$"]);
        await Assert.That(result.Variables).IsEmpty();
        await Assert.That(result.Removed.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScrubList_NoLayerRemovesBuiltin()
    {
        var result = WorkerEnvironment.Scrub(new Dictionary<string, string> { ["FAKE_TOKEN"] = "synthetic" }, []);
        await Assert.That(result.Removed).Contains("FAKE_TOKEN");
    }

    [Test]
    public async Task Scrub_MapsSnapshotToCleanMapAndRemovedNames()
    {
        var snapshot = new Dictionary<string, string> { ["KEEP"] = "visible", ["Z_TOKEN"] = "synthetic-z", ["A_KEY"] = "synthetic-a" };
        var result = WorkerEnvironment.Scrub(snapshot);
        await Assert.That(string.Join(",", result.Removed)).IsEqualTo("A_KEY,Z_TOKEN");
        await Assert.That(result.Variables.Single().Key).IsEqualTo("KEEP");
        await Assert.That(result.Passthrough(["a_key"])["A_KEY"]).IsEqualTo("synthetic-a");
        await Assert.That(result.Passthrough(["KEEP", "MISSING"])).IsEmpty();
        await Assert.That(snapshot.Count).IsEqualTo(3);
    }
}
