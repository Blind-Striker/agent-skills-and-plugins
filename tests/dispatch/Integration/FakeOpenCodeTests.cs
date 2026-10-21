using Dispatch.Tests.Support;
using OpenCode.Sdk;
using System.Net;

namespace Dispatch.Tests.Integration;

[Category("Integration")]
[NotInParallel("processes")]
public sealed class FakeOpenCodeTests
{
    [Test]
    [Timeout(180_000)]
    public async Task The_fake_starts_through_the_sdk_and_answers_server_info(CancellationToken cancellationToken)
    {
        using var tree = new TempTree();
        await Assert.That(File.Exists(FakeOpenCode.Executable)).IsTrue();
        await using var server = await OpenCodeServer.StartAsync(new OpenCodeServerOptions
        {
            Command = [FakeOpenCode.Executable, "serve"],
            WorkingDirectory = tree.Root,
            Environment = FakeOpenCode.Environment(tree, "health.jsonl"),
        }, cancellationToken);
        using var client = server.CreateClient();

        var response = await client.Server.GetInfoAsync(cancellationToken: cancellationToken);

        await Assert.That(response.Status).IsEqualTo(200);
        await Assert.That(response.ServerInfo.Version).IsEqualTo("2.0.32");
        await Assert.That(response.ServerInfo.Pid).IsEqualTo((long)server.ProcessId);
        await Assert.That(server.OwnsProcess).IsTrue();
        using var unauthenticated = new HttpClient();
        using var denied = await unauthenticated.GetAsync(response.ServerInfo.Urls.Single() + "/api/info", cancellationToken);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}
