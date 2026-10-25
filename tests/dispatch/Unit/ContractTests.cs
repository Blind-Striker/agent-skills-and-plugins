using System.Text.Json;
using Dispatch.Tests.Support;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ContractTests
{
    [Test]
    public async Task Prepare_contracts_round_trip()
    {
        var spec = Json.ReadTaskFile("""{"schemaVersion":2,"tasks":[{"id":"inspect","kind":"research","prepare":["z-command","a-command"]}]}""").Tasks![0];
        await Assert.That(spec.Prepare).IsEquivalentTo(new[] { "z-command", "a-command" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var record = new TaskRecord { Spec = spec, PrepareRunning = false, PrepareDone = true,
            Prepare = [new() { Command = "z-command", ExitCode = 0, Seconds = 1.2 }, new() { Command = "a-command", ExitCode = -1, Seconds = 2.3, TimedOut = true }] };
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(record, LedgerJson.Files.TaskRecord), LedgerJson.Files.TaskRecord)!;
        await Assert.That(copy.PrepareRunning).IsFalse();
        await Assert.That(copy.PrepareDone).IsTrue();
        var result = new TaskResult { Prepare = copy.Prepare };
        var resultCopy = JsonSerializer.Deserialize(JsonSerializer.Serialize(result, LedgerJson.Files.TaskResult), LedgerJson.Files.TaskResult)!;
        foreach (var entries in new[] { copy.Prepare!, resultCopy.Prepare! })
        {
            await Assert.That(entries.Select(x => x.Command)).IsEquivalentTo(new[] { "z-command", "a-command" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(entries.Select(x => x.ExitCode)).IsEquivalentTo(new[] { 0, -1 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(entries.Select(x => x.Seconds)).IsEquivalentTo(new[] { 1.2, 2.3 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(entries.Select(x => x.TimedOut)).IsEquivalentTo(new[] { false, true }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            foreach (var entry in entries)
            {
                await Assert.That(entry.Advisory).IsNull();
                await Assert.That(entry.NotRun).IsNull();
            }
        }
        var node = JsonSerializer.SerializeToNode(copy, LedgerJson.Files.TaskRecord)!;
        await Assert.That(node["schemaVersion"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(node["prepare"]![0]!.AsObject().ContainsKey("advisory")).IsFalse();
        await Assert.That(node["prepare"]![0]!.AsObject().ContainsKey("notRun")).IsFalse();
        var empty = JsonSerializer.SerializeToNode(new TaskRecord(), LedgerJson.Files.TaskRecord)!;
        foreach (var name in new[] { "prepare", "prepareRunning", "prepareDone" })
            await Assert.That(empty.AsObject().ContainsKey(name)).IsFalse();
        await Assert.That(JsonSerializer.SerializeToNode(new TaskResult(), LedgerJson.Files.TaskResult)!.AsObject().ContainsKey("prepare")).IsFalse();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"schemaVersion\":1}")]
    public async Task TaskFile_V1Refusal_ExplainsTheWholeConversion(string text)
    {
        var error = await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(Json.ReadTaskFile(text)));
        await Assert.That(error!.Message).Contains("add \"schemaVersion\": 2");
        await Assert.That(error.Message).Contains("add a kind to every task");
        await Assert.That(error.Message).Contains("\"defaults\": { \"backend\": \"opencode\" }");
        await Assert.That(error.Message).Contains("rename maxMinutes to timeoutMinutes");
        await Assert.That(error.Message).Contains("delete fallback");
    }

    [Test]
    [Arguments("null")]
    [Arguments("\"\"")]
    public async Task TaskFile_EmptyKind_Refused(string kind)
    {
        await Assert.That(() => Json.ReadTaskFile($$"""{"schemaVersion":2,"tasks":[{"id":"inspect","kind":{{kind}}}]}""")).Throws<JsonException>();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"tasks\":null}")]
    [Arguments("{\"tasks\":[]}")]
    public async Task TaskFile_RequiresTasks(string body)
    {
        var text = body.Insert(1, "\"schemaVersion\":2" + (body.Length > 2 ? "," : ""));
        await Assert.That(() => Json.ReadTaskFile(text)).Throws<JsonException>();
    }

    [Test]
    public async Task Config_V2Members_RoundTrip()
    {
        const string text = """
            { "schemaVersion":2, "$schema":"config.schema.json", "$comment":{"note":"ignored"},
              "preferences":{"backend":"codex","defaultRoutes":{"codex":"model","opencode":"provider/model"},
                "effort":"high","maxParallel":2,"timeoutMinutes":30,"profile":"clean","project":"off",
                "access":"read-only","network":"on","workspace":"in-place","commit":"none","rawPrompt":false},
              "restrictions":{"routes":[],"deny":[{"name":"no-publish","action":"shell","resources":["publish *"]}],
                "escalate":[],"scope":["!vendor/**"],"maxAccess":"workspace","networkAllowed":false,"scrub":["^EXTRA_"]},
              "instructions":["Report findings."],"locations":{"ledgerRoot":"~/ledgers","worktreeRoot":"~/worktrees"},
              "backends":{"opencode":{"executable":"/tools/opencode","env":["PROVIDER_KEY"]},"codex":{"env":[]}} }
            """;
        var config = JsonSerializer.Deserialize(text, InputJson.Read.ConfigFile)!;
        await Assert.That(config.Preferences!.MaxParallel).IsEqualTo(2);
        await Assert.That(config.Restrictions!.Routes).IsEmpty();
        await Assert.That(config.Restrictions.NetworkAllowed).IsFalse();
        await Assert.That(config.Backends!.Opencode!.Env).Contains("PROVIDER_KEY");
        var json = JsonSerializer.SerializeToNode(config, InputJson.Read.ConfigFile)!;
        await Assert.That(json["backends"]!["codex"]!["env"]!.AsArray().Count).IsEqualTo(0);
        await Assert.That(json["preferences"]!["timeoutMinutes"]!.GetValue<int>()).IsEqualTo(30);
        await Assert.That(json["$comment"]).IsNull();
    }

    [Test]
    [Arguments("{\"schemaVersion\":2,\"preferences\":{\"verfiy\":[]}}")]
    [Arguments("{\"schemaVersion\":2,\"backends\":{\"unknown\":{}}}")]
    [Arguments("{\"schemaVersion\":2,\"backends\":{\"codex\":{\"environment\":[]}}}")]
    [Arguments("{\"schemaVersion\":2,\"contracts\":[]}")]
    [Arguments("{\"schemaVersion\":2,\"preferences\":{\"dir\":\"subfolder\"}}")]
    public async Task Config_UnknownMember_Refused(string text)
    {
        await Assert.That(() => JsonSerializer.Deserialize(text, InputJson.Read.ConfigFile)).Throws<JsonException>();
    }

    [Test]
    public async Task Alias_metadata_survives_task_and_frozen_config_roundtrips()
    {
        const string text = """
            {"schemaVersion":2,"contracts":[{"alias":"@sol-worker","path":"/models/sol.md","from":"task",
              "sha256":"selected-hash","version":"worker-model sol v3"}]}
            """;
        var record = JsonSerializer.Deserialize(text, LedgerJson.Files.TaskRecord)!;
        var task = JsonSerializer.SerializeToNode(record, LedgerJson.Files.TaskRecord)!;
        var frozen = new EffectiveConfig { Tasks = new() { ["inspect"] = new() { Contracts = record.Contracts } } };
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(frozen, LedgerJson.Files.EffectiveConfig), LedgerJson.Files.EffectiveConfig)!;
        var config = JsonSerializer.SerializeToNode(copy, LedgerJson.Files.EffectiveConfig)!;
        foreach (var contracts in new[] { task["contracts"]!, config["tasks"]!["inspect"]!["contracts"]! })
        {
            await Assert.That(contracts.AsArray().Count).IsEqualTo(1);
            await Assert.That(contracts[0]!["alias"]?.GetValue<string>()).IsEqualTo("@sol-worker");
            await Assert.That(contracts[0]!["path"]!.GetValue<string>()).IsEqualTo("/models/sol.md");
            await Assert.That(contracts[0]!["from"]!.GetValue<string>()).IsEqualTo("task");
            await Assert.That(contracts[0]!["sha256"]!.GetValue<string>()).IsEqualTo("selected-hash");
            await Assert.That(contracts[0]!["version"]!.GetValue<string>()).IsEqualTo("worker-model sol v3");
        }
    }

    [Test]
    public async Task Ledger_TaskAndFrozenSettings_RoundTrip()
    {
        const string text = """
            {"schemaVersion":2,"spec":{"id":"inspect","kind":"research","shell":"bash","contracts":["contract.md"]},
             "settings":{"backend":{"value":"codex","from":"task"},
               "route":{"value":"codex:model#high","from":"user"},"timeoutMinutes":{"value":60,"from":"tool"},
               "profile":{"value":"clean","from":"task","actual":"clean-partial","reason":"link unavailable"},
               "access":{"value":"read-only","from":"tool","enforcement":"sandbox"},
               "network":{"value":"on","from":"tool","enforcement":"sandbox"},
               "workspace":{"value":"worktree","from":"tool"},"commit":{"value":"harness","from":"worktree"},
               "rawPrompt":{"value":false,"from":"tool"},
               "scope":{"value":["docs/**"],"ceilings":[{"globs":["!vendor/**"],"from":"repo"}]},
               "rules":[{"name":"repo/no-publish","enforcement":"approval-gate"}]},
             "workspace":{"path":"/worktree","baseRef":"main","baseSha":"abc"},
             "session":{"id":"thread","turn":"turn"},"lineage":"unknown",
             "contracts":[{"path":"/contract.md","from":"cli","sha256":"abc","version":"contract v2"}],
             "backendState":{"pid":123,"processGroup":true,"processStartTime":"2030-01-01T00:00:00Z",
               "linuxBootId":"boot","linuxStartTicks":77,
               "descendants":[{"pid":124,"startTime":"2030-01-01T00:00:00+00:00","linuxBootId":"boot","linuxStartTicks":78}],
               "codexHome":"/profile","codexHomeTemporary":true,
               "codexHomeMarkerName":".dispatch-home-0123","codexHomeMarkerText":"secret-marker",
               "profile":"clean-partial","profileReason":"link unavailable","future":true}}
            """;
        var record = JsonSerializer.Deserialize(text, LedgerJson.Files.TaskRecord)!;
        var output = JsonSerializer.SerializeToNode(record, LedgerJson.Files.TaskRecord)!;
        await Assert.That(output["settings"]!["profile"]!["reason"]!.GetValue<string>()).IsEqualTo("link unavailable");
        await Assert.That(output["settings"]!["rules"]![0]!["enforcement"]!.GetValue<string>()).IsEqualTo("approval-gate");
        await Assert.That(output["session"]!["turn"]!.GetValue<string>()).IsEqualTo("turn");
        await Assert.That(output["workspace"]!["branch"]).IsNull();
        await Assert.That(output["backendState"]!["descendants"]![0]!["pid"]!.GetValue<int>()).IsEqualTo(124);
        await Assert.That(output["backendState"]!["future"]).IsNull();
        // A later process recovers a Codex worker only from these recorded fields.
        foreach (var (name, value) in new[] { ("linuxBootId", "boot"), ("codexHomeMarkerName", ".dispatch-home-0123"),
            ("codexHomeMarkerText", "secret-marker"), ("profile", "clean-partial"), ("profileReason", "link unavailable") })
            await Assert.That(output["backendState"]![name]?.GetValue<string>()).IsEqualTo(value);
        await Assert.That(output["backendState"]!["linuxStartTicks"]?.GetValue<long>()).IsEqualTo(77);
        await Assert.That(output["backendState"]!["descendants"]![0]!["linuxStartTicks"]?.GetValue<long>()).IsEqualTo(78);
        var frozen = new EffectiveConfig
        {
            Tasks = new() { ["inspect"] = new FrozenTask { Settings = record.Settings, Contracts = record.Contracts, InputSha256 = "input-hash", ResultSchemaSha256 = "schema-hash" } },
        };
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(frozen, LedgerJson.Files.EffectiveConfig), LedgerJson.Files.EffectiveConfig)!;
        await Assert.That(copy.Tasks["inspect"].Settings.Profile!.Actual).IsEqualTo("clean-partial");
        await Assert.That(copy.Tasks["inspect"].InputSha256).IsEqualTo("input-hash");
        await Assert.That(copy.Tasks["inspect"].ResultSchemaSha256).IsEqualTo("schema-hash");
    }

    [Test]
    public async Task Result_ValidationAndOptionalUsage_RoundTrip()
    {
        const string text = """
            {"schemaVersion":2,"task":"translate","run":"example","kind":"structured","backend":"codex",
             "state":"failed","outcome":"succeeded","failures":["invalid-output"],
             "session":{"id":"thread","turn":"turn"},"asks":2,"workerStopped":false,
             "warnings":["git-config-changed"],"commitMode":"none","verifyShell":"/tools/bash",
             "tokens":{"output":7},"validation":{"ok":false,"errors":[{"at":"/items/0/id","message":"unknown id"}]},"future":true}
            """;
        var result = JsonSerializer.Deserialize(text, LedgerJson.Files.TaskResult)!;
        var output = JsonSerializer.SerializeToNode(result, LedgerJson.Files.TaskResult)!;
        await Assert.That(output["validation"]!["errors"]![0]!["at"]!.GetValue<string>()).IsEqualTo("/items/0/id");
        await Assert.That(output["workerStopped"]!.GetValue<bool>()).IsFalse();
        await Assert.That(output["asks"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(output["cost"]).IsNull();
        await Assert.That(output["tokens"]!["input"]).IsNull();
        await Assert.That(output["tokensComplete"]).IsNull();
        await Assert.That(output["deliverable"]).IsNull();
        await Assert.That(output["permissionAsks"]).IsNull();
        result.State = "done";
        result.Deliverable = "deliverable.json";
        result.Validation = new ValidationResult { Ok = true };
        output = JsonSerializer.SerializeToNode(result, LedgerJson.Files.TaskResult)!;
        await Assert.That(output["deliverable"]!.GetValue<string>()).IsEqualTo("deliverable.json");
        await Assert.That(output["validation"]!["ok"]!.GetValue<bool>()).IsTrue();
    }

    [Test]
    [Arguments("run")]
    [Arguments("config")]
    [Arguments("task")]
    [Arguments("result")]
    [Arguments("event")]
    public async Task LedgerRead_NewerVersion_RefusedWithUpdateHint(string kind)
    {
        var info = kind switch
        {
            "run" => (System.Text.Json.Serialization.Metadata.JsonTypeInfo)LedgerJson.Files.RunInfo,
            "config" => LedgerJson.Files.EffectiveConfig,
            "task" => LedgerJson.Files.TaskRecord,
            "result" => LedgerJson.Files.TaskResult,
            _ => LedgerJson.Events.LedgerLine,
        };
        var error = await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(JsonSerializer.Deserialize("""{"schemaVersion":3,"type":"run"}""", info)));
        await Assert.That(error!.Message).Contains("written by a newer dispatch; update the plugin");
    }

    [Test]
    [Arguments("always")]
    [Arguments("acceptForSession")]
    public async Task Decision_InvalidAnswer_Refused(string answer)
    {
        await Assert.That(() => JsonSerializer.Deserialize($$"""{"schemaVersion":2,"askId":"inspect-1","decision":"{{answer}}","time":"2030-01-01T00:00:00Z","by":"operator"}""", ControlJson.Files.AskDecision)).Throws<JsonException>();
    }

    [Test]
    [Arguments("{\"schemaVersion\":2,\"task\":\"inspect\",\"time\":\"now\"}")]
    [Arguments("{\"schemaVersion\":2,\"run\":\"example\",\"task\":\"inspect\",\"time\":\"now\",\"by\":\"operator\"}")]
    public async Task Cancel_OnlyTheFourRequiredMembers_Accepted(string text)
    {
        await Assert.That(() => JsonSerializer.Deserialize(text, ControlJson.Files.CancelRequest)).Throws<JsonException>();
    }

    [Test]
    public async Task Event_OnlyTheRunLineHasVersion_AndTimeIsUtcZ()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 23, 30, 0, TimeSpan.Zero));
        var text = JsonSerializer.Serialize(new LedgerLine { Ts = Json.Timestamp(time), Type = "permission", Data = new() { ["by"] = "operator" } }, LedgerJson.Events.LedgerLine);
        await Assert.That(text).DoesNotContain("schemaVersion");
        await Assert.That(text).DoesNotContain("\n");
        await Assert.That(text).Contains("2030-01-01T23:30:00.0000000Z");
        var decision = JsonSerializer.SerializeToNode(new AskDecision { Decision = "reject" }, ControlJson.Files.AskDecision)!;
        await Assert.That(decision["by"]!.GetValue<string>()).IsEqualTo("operator");
    }

    [Test]
    public async Task RunId_UsesUtcDate()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 23, 30, 0, TimeSpan.Zero));
        await Assert.That(Commands.CreateRunId("inspect", time)).IsEqualTo("2030-01-01-inspect");
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("test-zone", TimeSpan.FromHours(3), "test-zone", "test-zone");
    }

    [Test]
    [Arguments("run")]
    [Arguments("config")]
    [Arguments("task")]
    [Arguments("result")]
    [Arguments("event")]
    public async Task LedgerRead_UnknownField_Accepted(string kind)
    {
        var info = kind switch
        {
            "run" => (System.Text.Json.Serialization.Metadata.JsonTypeInfo)LedgerJson.Files.RunInfo,
            "config" => LedgerJson.Files.EffectiveConfig,
            "task" => LedgerJson.Files.TaskRecord,
            "result" => LedgerJson.Files.TaskResult,
            _ => LedgerJson.Events.LedgerLine,
        };
        var record = JsonSerializer.Deserialize("""{/* future extension */"schemaVersion":2,"type":"run","future":true,"spec":{"kind":"research","future":true},}""", info);
        var output = JsonSerializer.SerializeToNode(record, info)!;
        await Assert.That(output["schemaVersion"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(output["future"]).IsNull();
        if (kind == "task") await Assert.That(output["spec"]!["kind"]!.GetValue<string>()).IsEqualTo("research");
    }

    [Test]
    public async Task TaskFile_UnknownField_Refused()
    {
        await Assert.That(() => JsonSerializer.Deserialize("""{"schemaVersion":2,"verfiy":[]}""", InputJson.Read.TaskFile)).Throws<JsonException>();
    }

    [Test]
    public async Task Plan_MissingKind_Refused()
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "tasks.jsonc");
        File.WriteAllText(path, """{"schemaVersion":2,"tasks":[{"id":"inspect","prompt":"Inspect."}]}""");
        await Assert.That(async () => { await Planner.LoadAsync(path, tree.Root, new AllowList(), CancellationToken.None); }).Throws<HarnessException>();
    }

    [Test]
    public async Task TaskFile_V2Members_AndCallerItems_AreRead()
    {
        const string text = """
            {
              "schemaVersion":2, "$schema":"task.schema.json", "$comment":"annotation",
              "defaults":{"backend":"codex","defaultRoutes":{"codex":"model"},"timeoutMinutes":20},
              "policy":{"networkAllowed":false}, "contracts":["contract.md"],
              "tasks":[{"id":"translate","kind":"structured","backend":"codex","effort":"high",
                "profile":"clean","project":"off","access":"read-only","network":"on",
                "workspace":"worktree","rawPrompt":false,"timeoutMinutes":10,"prompt":"Translate.",
                "input":{"items":[{"id":"i1","arbitrary":{"text":"İyi günler 世界"}}],"idField":"id"},
                "result":{"schema":"result.schema.json","itemsAt":"/items","identity":"many","immutable":["key"]}}]
            }
            """;
        var file = JsonSerializer.Deserialize(text, InputJson.Read.TaskFile);
        var output = JsonSerializer.SerializeToNode(file, InputJson.Read.TaskFile)!;
        await Assert.That(output["tasks"]![0]!["input"]!["items"]![0]!["arbitrary"]!["text"]!.GetValue<string>()).IsEqualTo("İyi günler 世界");
        await Assert.That(output["defaults"]!["timeoutMinutes"]!.GetValue<int>()).IsEqualTo(20);
        await Assert.That(output["$schema"]).IsNull();
        await Assert.That(output["$comment"]).IsNull();
    }

    [Test]
    [Arguments("maxMinutes")]
    [Arguments("fallback")]
    public async Task TaskFile_RemovedMember_Refused(string member)
    {
        var value = member == "fallback" ? "[]" : "10";
        await Assert.That(() => JsonSerializer.Deserialize($$"""{"schemaVersion":2,"tasks":[{"kind":"code","{{member}}":{{value}}}]}""", InputJson.Read.TaskFile)).Throws<JsonException>();
    }

    [Test]
    [Arguments("run")]
    [Arguments("task")]
    [Arguments("result")]
    [Arguments("ask")]
    [Arguments("decision")]
    [Arguments("authored-config")]
    [Arguments("effective-config")]
    [Arguments("task-file")]
    [Arguments("cancel")]
    [Arguments("run-ref")]
    [Arguments("run-event")]
    public async Task ContractFiles_WriteVersion2(string kind)
    {
        var text = kind switch
        {
            "run" => JsonSerializer.Serialize(new RunInfo(), LedgerJson.Files.RunInfo),
            "task" => JsonSerializer.Serialize(new TaskRecord(), LedgerJson.Files.TaskRecord),
            "result" => JsonSerializer.Serialize(new TaskResult(), LedgerJson.Files.TaskResult),
            "ask" => JsonSerializer.Serialize(new PendingAsk(), ControlJson.Files.PendingAsk),
            "decision" => JsonSerializer.Serialize(new AskDecision(), ControlJson.Files.AskDecision),
            "authored-config" => JsonSerializer.Serialize(new ConfigFile(), InputJson.Read.ConfigFile),
            "effective-config" => JsonSerializer.Serialize(new EffectiveConfig(), LedgerJson.Files.EffectiveConfig),
            "task-file" => JsonSerializer.Serialize(new TaskFile(), InputJson.Read.TaskFile),
            "cancel" => JsonSerializer.Serialize(new CancelRequest(), ControlJson.Files.CancelRequest),
            "run-ref" => JsonSerializer.Serialize(new RunReference(), ControlJson.Files.RunReference),
            _ => JsonSerializer.Serialize(new LedgerLine { Type = "run" }, LedgerJson.Events.LedgerLine),
        };
        using var doc = JsonDocument.Parse(text);
        await Assert.That(doc.RootElement.TryGetProperty("schemaVersion", out var version)).IsTrue();
        await Assert.That(version.GetInt32()).IsEqualTo(2);
    }

    [Test]
    public async Task ContractWriter_UsesLfUtf8AndFinalNewline()
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "result.json");
        Json.WriteFile(path, new TaskResult { Error = "İyi günler 世界" }, LedgerJson.Files.TaskResult);
        var bytes = File.ReadAllBytes(path);
        await Assert.That(bytes).DoesNotContain((byte)13);
        await Assert.That(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 })).IsFalse();
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(text).Contains("İyi günler 世界");
        await Assert.That(text.EndsWith("\n", StringComparison.Ordinal) && !text.EndsWith("\n\n", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"schemaVersion\":1}")]
    [Arguments("{\"schemaVersion\":3}")]
    public async Task TaskFile_UnsupportedVersion_Refused(string text)
    {
        await Assert.That(() => JsonSerializer.Deserialize(text, InputJson.Read.TaskFile)).Throws<JsonException>();
    }
}
