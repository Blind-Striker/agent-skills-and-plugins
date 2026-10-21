using System.Text.Json.Nodes;

namespace Dispatch.Tests.Unit;

[Category("Unit")]
public sealed class ValidatorTests
{
    static DeliverableCheck Check(string schema, string instance) =>
        Deliverable.LoadSchema("{\"properties\":{\"value\":" + schema + "}}")
            .Validate("{\"items\":[],\"value\":" + instance + "}", [], new StructuredResult { ItemsAt = "/items" });

    [Test]
    [Arguments("{\"type\":\"object\"}", "{}", "[]", "/value", "type", 1)]
    [Arguments("{\"type\":\"array\"}", "[]", "{}", "/value", "type", 1)]
    [Arguments("{\"type\":\"string\"}", "\"a\"", "1", "/value", "type", 1)]
    [Arguments("{\"type\":\"number\"}", "1.5", "\"1\"", "/value", "type", 1)]
    [Arguments("{\"type\":\"integer\"}", "1.0", "1.5", "/value", "type", 1)]
    [Arguments("{\"type\":\"boolean\"}", "true", "1", "/value", "type", 1)]
    [Arguments("{\"type\":\"null\"}", "null", "false", "/value", "type", 1)]
    [Arguments("{\"type\":[\"null\",\"string\"]}", "null", "false", "/value", "type", 1)]
    [Arguments("{\"properties\":{\"a/b~\":false}}", "{}", "{\"a/b~\":null}", "/value/a~1b~0", "false", 1)]
    [Arguments("{\"required\":[\"x\"]}", "{\"x\":null}", "{}", "/value", "required", 1)]
    [Arguments("{\"properties\":{\"x\":true},\"additionalProperties\":false}", "{\"x\":null}", "{\"extra\":1}", "/value/extra", "additionalProperties", 1)]
    [Arguments("{\"additionalProperties\":{\"type\":\"integer\"}}", "{\"x\":2}", "{\"x\":null}", "/value/x", "type", 1)]
    [Arguments("{\"items\":{\"type\":\"string\"}}", "[\"a\",\"b\"]", "[1,2]", "/value/0", "type", 2)]
    [Arguments("{\"enum\":[1,\"x\",null]}", "1", "true", "/value", "enum", 1)]
    [Arguments("{\"const\":null}", "null", "0", "/value", "const", 1)]
    [Arguments("{\"anyOf\":[{\"type\":\"string\"},{\"type\":\"null\"}]}", "null", "1", "/value", "anyOf", 1)]
    [Arguments("{\"$defs\":{\"v\":{\"type\":\"string\"}},\"$ref\":\"#/properties/value/$defs/v\"}", "\"a\"", "1", "/value", "type", 1)]
    [Arguments("{\"definitions\":{\"v\":false},\"properties\":{\"x\":{\"$ref\":\"#/properties/value/definitions/v\"}}}", "{}", "{\"x\":0}", "/value/x", "false", 1)]
    [Arguments("{\"minLength\":2}", "\"ab\"", "\"a\"", "/value", "minLength", 1)]
    [Arguments("{\"maxLength\":2}", "\"ab\"", "\"abc\"", "/value", "maxLength", 1)]
    [Arguments("{\"minimum\":1}", "1", "0", "/value", "minimum", 1)]
    [Arguments("{\"maximum\":1}", "1", "2", "/value", "maximum", 1)]
    [Arguments("{\"exclusiveMinimum\":1}", "2", "1", "/value", "exclusiveMinimum", 1)]
    [Arguments("{\"exclusiveMaximum\":1}", "0", "1", "/value", "exclusiveMaximum", 1)]
    public async Task Validator_KeywordCoverage(string schema, string valid, string invalid, string at, string keyword, int count)
    {
        var accepted = Check(schema, valid);
        await Assert.That(accepted.Validation.Ok).IsTrue();
        var rejected = Check(schema, invalid);
        await Assert.That(rejected.Validation.Ok).IsFalse();
        await Assert.That(rejected.Document).IsNull();
        await Assert.That(rejected.Validation.Errors.Count).IsEqualTo(count);
        await Assert.That(rejected.Validation.Errors[0].At).IsEqualTo(at);
        await Assert.That(rejected.Validation.Errors[0].Message).Contains(keyword);
    }

    [Test]
    [Arguments("{\"oneOf\":[{}]}", "oneOf", "#/oneOf")]
    [Arguments("{\"$defs\":{\"unused\":{\"pattern\":\"x\"}}}", "pattern", "#/$defs/unused/pattern")]
    [Arguments("{\"$ref\":\"https://example.invalid/schema\"}", "$ref", "#/$ref")]
    [Arguments("{\"$ref\":\"#/$defs/missing\"}", "$ref", "#/$ref")]
    [Arguments("{\"items\":[{}]}", "items", "#/items")]
    [Arguments("{\"$ref\":\"#\"}", "$ref", "#/$ref")]
    [Arguments("{\"anyOf\":[{\"$ref\":\"#\"}]}", "$ref", "#/anyOf/0/$ref")]
    [Arguments("{\"properties\":{\"x\":{\"$id\":\"nested\"}}}", "$id", "#/properties/x/$id")]
    [Arguments("{\"type\":\"typo\"}", "type", "#/type")]
    [Arguments("{\"type\":[]}", "type", "#/type")]
    [Arguments("{\"required\":[1]}", "required", "#/required")]
    [Arguments("{\"minLength\":-1}", "minLength", "#/minLength")]
    [Arguments("{\"maxLength\":1.5}", "maxLength", "#/maxLength")]
    [Arguments("{\"exclusiveMinimum\":true}", "exclusiveMinimum", "#/exclusiveMinimum")]
    [Arguments("{\"additionalProperties\":null}", "additionalProperties", "#/additionalProperties")]
    [Arguments("{\"anyOf\":[]}", "anyOf", "#/anyOf")]
    [Arguments("{\"$defs\":{\"a\":{\"$ref\":\"#/$defs/b\"},\"b\":{\"$ref\":\"#/$defs/a\"}}}", "$ref", "#/$defs/b/$ref")]
    [Arguments("{\"$ref\":\"#/default\",\"default\":{}}", "$ref", "#/$ref")]
    public async Task Validator_PlanErrors(string schema, string keyword, string at)
    {
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(Deliverable.LoadSchema(schema)));
        await Assert.That(error!.Message).Contains(keyword);
        await Assert.That(error.Message).Contains(at);
    }

    [Test]
    [Arguments("true")]
    [Arguments("[]")]
    [Arguments("null")]
    public async Task Validator_SchemaRoot_MustBeAnObject(string schema)
    {
        await Assert.That(() => Deliverable.LoadSchema(schema)).Throws<HarnessException>();
    }

    [Test]
    [Arguments("{")]
    [Arguments("{\"type\":\"string\",\"type\":\"number\"}")]
    public async Task Validator_MalformedOrAmbiguousSchema_IsRefused(string schema)
    {
        var error = await Assert.ThrowsAsync<HarnessException>(() => Task.FromResult(Deliverable.LoadSchema(schema)));
        await Assert.That(error!.Message).Contains("schema#");
    }

    [Test]
    [Arguments("{\"enum\":[{\"a\":[1,null],\"b\":true}]}", "{\"b\":true,\"a\":[1.0,null]}", "{\"b\":true,\"a\":[null,1]}")]
    [Arguments("{\"const\":1}", "1.0", "1.00000000000000000000000000001")]
    [Arguments("{\"const\":\"İ\"}", "\"İ\"", "\"i\"")]
    [Arguments("{\"enum\":[1]}", "1e0", "true")]
    [Arguments("{\"type\":\"integer\"}", "1.0", "1.00000000000000000000000000001")]
    [Arguments("{\"type\":\"integer\"}", "10e-1", "10e-2")]
    [Arguments("{\"type\":\"integer\"}", "1e100", "1e-100")]
    public async Task Validator_Equality(string schema, string valid, string invalid)
    {
        await Assert.That(Check(schema, valid).Validation.Ok).IsTrue();
        await Assert.That(Check(schema, invalid).Validation.Ok).IsFalse();
    }

    [Test]
    public async Task Validator_CodePointLength()
    {
        await Assert.That(Check("{\"minLength\":1,\"maxLength\":1}", "\"😀\"").Validation.Ok).IsTrue();
        var tooShort = Check("{\"minLength\":2}", "\"😀\"");
        await Assert.That(tooShort.Validation.Errors.Single().Message).Contains("minLength");
        var tooLong = Check("{\"maxLength\":1}", "\"😀x\"");
        await Assert.That(tooLong.Validation.Errors.Single().Message).Contains("maxLength");
    }

    [Test]
    [Arguments("{\"minimum\":0.1000000000000000000000000001}", "0.1000000000000000000000000001", "0.1000000000000000000000000000")]
    [Arguments("{\"minimum\":1e100}", "2e100", "1e99")]
    [Arguments("{\"exclusiveMaximum\":-1e100}", "-2e100", "-1e100")]
    public async Task Validator_NumericBounds_UseDecimalThenDouble(string schema, string valid, string invalid)
    {
        await Assert.That(Check(schema, valid).Validation.Ok).IsTrue();
        await Assert.That(Check(schema, invalid).Validation.Ok).IsFalse();
    }

    [Test]
    public async Task Validator_NestedMapsNullsAndPluralObjects_PassThroughUnchanged()
    {
        const string schema = """
            {
              "$schema":"https://json-schema.org/draft/2020-12/schema", "$id":"urn:result",
              "$comment":"annotation", "title":"Result", "description":"Caller data",
              "default":{"notAKeyword":true}, "examples":[{"notAKeyword":1}],
              "deprecated":false, "readOnly":true, "writeOnly":false, "format":"not-checked",
              "$defs":{"unused":false},
              "type":"object", "required":["items"], "additionalProperties":false,
              "properties":{"items":{"type":"array","items":{
                "type":"object","required":["id","text","plural","notes"],"additionalProperties":false,
                "properties":{"id":{"type":"string"},"text":{"type":"string"},
                  "plural":{"type":"object","additionalProperties":{"type":"string"}},
                  "notes":{"type":["null","string"]}}
              }}}
            }
            """;
        const string text = """{"items":[{"id":"i1","text":"İyi günler","plural":{"one":"bir","other":"çok"},"notes":null}]}""";
        var result = Deliverable.LoadSchema(schema).Validate(text, JsonNode.Parse("""[{"id":"i1"}]""")!.AsArray(), new StructuredResult { ItemsAt = "/items" });
        await Assert.That(result.Validation.Ok).IsTrue();
        await Assert.That(JsonNode.DeepEquals(result.Document, JsonNode.Parse(text))).IsTrue();
        var invalid = Deliverable.LoadSchema(schema).Validate(text.Replace("\"other\":\"çok\"", "\"other\":3", StringComparison.Ordinal),
            JsonNode.Parse("""[{"id":"i1"}]""")!.AsArray(), new StructuredResult { ItemsAt = "/items" });
        await Assert.That(invalid.Validation.Errors.Single().At).IsEqualTo("/items/0/plural/other");
    }

    [Test]
    public async Task Validator_RecursiveRefs_WithInstanceStepsTerminate_AndRefSiblingsApply()
    {
        const string schema = """
            {"$defs":{"node":{"type":"object","required":["name"],"properties":{
              "name":{"type":"string"},"children":{"type":"array","items":{"$ref":"#/$defs/node"}}
            }}},"properties":{"value":{"$ref":"#/$defs/node","required":["children"]}}}
            """;
        var validator = Deliverable.LoadSchema(schema);
        var result = validator.Validate("""{"items":[],"value":{"name":"root","children":[{"name":"leaf"}]}}""", [], new StructuredResult { ItemsAt = "/items" });
        await Assert.That(result.Validation.Ok).IsTrue();
        var invalid = validator.Validate("""{"items":[],"value":{"name":"root"}}""", [], new StructuredResult { ItemsAt = "/items" });
        await Assert.That(invalid.Validation.Errors.Single().Message).Contains("children");
    }

    [Test]
    public async Task Validator_AllErrors_AreOrderedSchemaThenIdentityThenImmutable_AndSerializeAsValidation()
    {
        var validator = Deliverable.LoadSchema("""{"required":["absent"],"properties":{"items":{"items":{"properties":{"text":{"type":"string"}}}}}}""");
        var input = JsonNode.Parse("""[{"id":"i1","key":"one"},{"id":"i2","key":"two"}]""")!.AsArray();
        var result = validator.Validate("""{"items":[{"id":"i1","key":"changed","text":5},{"id":"other"}]}""", input,
            new StructuredResult { ItemsAt = "/items", Immutable = ["key"] });
        await Assert.That(result.Validation.Errors.Select(e => e.At).SequenceEqual(new[] { "", "/items/0/text", "/items/1/id", "/items", "/items/0/key" })).IsTrue();
        var output = System.Text.Json.JsonSerializer.SerializeToNode(new TaskResult { Validation = result.Validation }, LedgerJson.Files.TaskResult)!;
        await Assert.That(output["validation"]!["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(output["validation"]!["errors"]![0]!["message"]!.GetValue<string>()).Contains("required");
        await Assert.That(output["validation"]!["errors"]![0]!["at"]!.GetValue<string>()).IsEqualTo("");
    }

    [Test]
    public async Task Validator_AnyOf_ReportsOneErrorWithBranchCount()
    {
        var result = Check("""{"anyOf":[{"required":["a","b"]},{"type":"string","minLength":3}]}""", "{}");
        await Assert.That(result.Validation.Errors.Single().At).IsEqualTo("/value");
        await Assert.That(result.Validation.Errors[0].Message).Contains("2 branches");
    }
}
