using System.Text;
using System.Text.Json.Nodes;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class DeliverableTests
{
    static readonly JsonArray Input = JsonNode.Parse("""[{"id":"i1","key":"title"},{"id":"i2","key":"action"}]""")!.AsArray();

    [Test]
    public async Task Deliverable_Valid_LeavesUnicodeAndCallerDataUnchanged()
    {
        const string text = """{"items":[{"id":"i1","text":"İyi günler, ışık 世界 😀"},{"id":"i2","plural":{"one":"bir","other":"çok"}}]}""";
        var result = Deliverable.LoadSchema("{}").Validate(text, Input, new StructuredResult { ItemsAt = "/items" });

        await Assert.That(result.Validation.Ok).IsTrue();
        await Assert.That(result.Validation.Errors).IsEmpty();
        var roundTrip = result.Document!.ToJsonString(Json.Options(strict: false, indented: false));
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(roundTrip), JsonNode.Parse(text))).IsTrue();
        await Assert.That(roundTrip).Contains("İyi günler, ışık 世界");
        var value = JsonNode.Parse(roundTrip)!["items"]![0]!["text"]!.GetValue<string>();
        await Assert.That(Encoding.UTF8.GetBytes(value).SequenceEqual(Encoding.UTF8.GetBytes("İyi günler, ışık 世界 😀"))).IsTrue();
    }

    [Test]
    [Arguments("Here is the result:\n```json\n{\"items\":[]}\n```\nDone.", true)]
    [Arguments("Before\n````json\n{\"items\":[]}\n````", true)]
    [Arguments("not JSON", false)]
    [Arguments("{} {}", false)]
    [Arguments("```json\n{}\n```\n```json\n{}\n```", false)]
    [Arguments("```json\n{}\n", false)]
    [Arguments("{/* comment */\"items\":[],}", false)]
    [Arguments("{\"items\":[],\"items\":[]}", false)]
    public async Task Deliverable_ParsesOnlyOneDocumentOrOneJsonFence(string text, bool ok)
    {
        var result = Deliverable.LoadSchema("{}").Validate(text, [], new StructuredResult { ItemsAt = "/items" });
        await Assert.That(result.Validation.Ok).IsEqualTo(ok);
        if (!ok)
        {
            await Assert.That(result.Document).IsNull();
            await Assert.That(result.Validation.Errors.Count).IsEqualTo(1);
            await Assert.That(result.Validation.Errors[0].At).IsEqualTo("");
            await Assert.That(result.Validation.Errors[0].Message).Contains("parse");
        }
    }

    [Test]
    [Arguments("[{\"id\":\"i1\"}]", "/items", "missing", "i2")]
    [Arguments("[{\"id\":\"i1\"},{\"id\":\"i1\"},{\"id\":\"i2\"}]", "/items/1/id", "duplicate", "i1")]
    [Arguments("[{\"id\":\"i1\"},{\"id\":\"i2\"},{\"id\":\"other\"}]", "/items/2/id", "unknown", "other")]
    [Arguments("[{},{\"id\":\"i2\"}]", "/items/0/id", "string", "id")]
    [Arguments("[null,{\"id\":\"i2\"}]", "/items/0", "object", "answer")]
    public async Task Deliverable_Identity_RejectsMissingDuplicateUnknownOrMalformedIds(string items, string at, string message, string id)
    {
        var result = Deliverable.LoadSchema("{}").Validate("{\"items\":" + items + "}", Input, new StructuredResult { ItemsAt = "/items" });
        await Assert.That(result.Validation.Ok).IsFalse();
        await Assert.That(result.Document).IsNull();
        var error = result.Validation.Errors.Single(e => e.At == at);
        await Assert.That(error.Message).Contains(message);
        await Assert.That(error.Message).Contains(id);
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"items\":{}}")]
    [Arguments("null")]
    public async Task Deliverable_ItemsAt_MustResolveToAnArray(string text)
    {
        var result = Deliverable.LoadSchema("{}").Validate(text, Input, new StructuredResult { ItemsAt = "/items" });
        await Assert.That(result.Validation.Ok).IsFalse();
        await Assert.That(result.Validation.Errors[0].At).IsEqualTo("/items");
        await Assert.That(result.Validation.Errors[0].Message).Contains("array");
    }

    [Test]
    [Arguments("[]", true)]
    [Arguments("[{\"id\":\"i1\"},{\"id\":\"i1\"}]", true)]
    [Arguments("[{\"id\":\"unknown\"}]", false)]
    public async Task Deliverable_Many_AllowsRepeatedAndOmittedButNotUnknownIds(string items, bool ok)
    {
        var result = Deliverable.LoadSchema("{}").Validate("{\"items\":" + items + "}", Input, new StructuredResult { ItemsAt = "/items", Identity = "many" });
        await Assert.That(result.Validation.Ok).IsEqualTo(ok);
        if (!ok) await Assert.That(result.Validation.Errors.Single().Message).Contains("unknown");
    }

    [Test]
    [Arguments("[{\"id\":\"i1\",\"key\":\"changed\"},{\"id\":\"i2\",\"key\":\"action\"}]", "/items/0/key", "i1")]
    [Arguments("[{\"id\":\"i1\"},{\"id\":\"i2\",\"key\":\"action\"}]", "/items/0/key", "i1")]
    [Arguments("[{\"id\":\"i1\",\"key\":\"title\"},{\"id\":\"i1\",\"key\":null}]", "/items/1/key", "i1")]
    public async Task Deliverable_Immutable_ChecksEveryAnswerIncludingMissingFields(string items, string at, string id)
    {
        var result = Deliverable.LoadSchema("{}").Validate("{\"items\":" + items + "}", Input,
            new StructuredResult { ItemsAt = "/items", Identity = "many", Immutable = ["key"] });
        await Assert.That(result.Validation.Ok).IsFalse();
        await Assert.That(result.Document).IsNull();
        var error = result.Validation.Errors.Single();
        await Assert.That(error.At).IsEqualTo(at);
        await Assert.That(error.Message).Contains(id);
        await Assert.That(error.Message).Contains("key");
    }

    [Test]
    public async Task Deliverable_Immutable_UsesJsonEqualityAndEscapedPointersWithCustomIds()
    {
        var input = JsonNode.Parse("""[{"item/id":"a","key":{"a":[1,null],"b":true}}]""")!.AsArray();
        const string text = """{"a/b":{"~items":[{"item/id":"a","key":{"b":true,"a":[1.0,null]}}]}}""";
        var options = new StructuredResult { ItemsAt = "/a~1b/~0items", Immutable = ["key"] };
        var validator = Deliverable.LoadSchema("{}");
        await Assert.That(validator.Validate(text, input, options, "item/id").Validation.Ok).IsTrue();
        var changed = validator.Validate(text.Replace("[1.0,null]", "[null,1.0]", StringComparison.Ordinal), input, options, "item/id");
        await Assert.That(changed.Validation.Errors.Single().At).IsEqualTo("/a~1b/~0items/0/key");
    }

    [Test]
    public async Task Deliverable_InputMissingImmutableField_IsRefused()
    {
        await Assert.That(() => Deliverable.LoadSchema("{}").Validate("{\"items\":[]}", Input,
            new StructuredResult { ItemsAt = "/items", Immutable = ["missing"] })).Throws<HarnessException>();
    }
}
