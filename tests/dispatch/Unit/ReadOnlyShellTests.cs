using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ReadOnlyShellTests
{
    [Test]
    [Arguments("research", "always")]
    [Arguments("structured", "escalate")]
    public async Task Invalid_or_structured_shell_escalation_is_a_named_refusal(string kind, string value)
    {
        var file = Json.ReadTaskFile("""{"schemaVersion":2,"defaults":{"backend":"codex"},"tasks":[{"id":"inspect","kind":"KIND","route":"model-y","readOnlyShell":"VALUE"}]}"""
            .Replace("KIND", kind).Replace("VALUE", value));
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(ConfigResolver.Resolve(file)));
        await Assert.That(error!.Message).Contains("readOnlyShell");
        await Assert.That(error.Message).Contains("task");
    }

    [Test]
    public async Task Read_only_shell_resolves_every_layer_subset_with_provenance()
    {
        for (var mask = 0; mask < 32; mask++)
        {
            var file = Json.ReadTaskFile("""{"schemaVersion":2,"defaults":{"backend":"opencode"},"tasks":[{"id":"inspect","kind":"research","route":"fixture/model"}]}""");
            var layers = new[] { "user", "repo", "task-file-defaults", "task", "cli" };
            var values = Enumerable.Range(0, 5).Select(i => i % 2 == 0 ? "escalate" : "decline").ToArray();
            ConfigPreferences? Preferences(int index) => (mask & (1 << index)) == 0 ? null
                : JsonSerializer.Deserialize("{\"readOnlyShell\":\"" + values[index] + "\"}", InputJson.Read.ConfigPreferences);
            if ((mask & 4) != 0) file.Defaults = JsonSerializer.Deserialize("""{"backend":"opencode","readOnlyShell":"escalate"}""", InputJson.Read.TaskDefaults);
            if ((mask & 8) != 0) file.Tasks![0] = Json.ReadTaskFile("""{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","route":"fixture/model","readOnlyShell":"decline"}]}""").Tasks![0];
            var config = ConfigResolver.Resolve(file, new() { Preferences = Preferences(0) }, new() { Preferences = Preferences(1) }, Preferences(4));
            var settings = JsonSerializer.SerializeToNode(config, LedgerJson.Files.EffectiveConfig)!["tasks"]!["inspect"]!["settings"]!;
            var last = Enumerable.Range(0, 5).Where(i => (mask & (1 << i)) != 0).DefaultIfEmpty(-1).Last();
            await Assert.That(settings["readOnlyShell"]?["value"]?.GetValue<string>()).IsEqualTo(last < 0 ? "decline" : values[last]);
            await Assert.That(settings["readOnlyShell"]?["from"]?.GetValue<string>()).IsEqualTo(last < 0 ? "tool" : layers[last]);
        }
    }
}
