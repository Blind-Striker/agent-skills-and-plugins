sealed record DeliverableCheck(JsonNode? Document, ValidationResult Validation);

sealed class Deliverable
{
    readonly JsonObject schema;
    readonly Dictionary<JsonNode, string> schemaPaths = [];
    readonly Dictionary<JsonNode, JsonNode> references = [];

    Deliverable(JsonObject schema) => this.schema = schema;

    public static Deliverable LoadSchema(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false });
        }
        catch (JsonException error) { throw new HarnessException("schema#: parse: " + error.Message); }
        if (root is not JsonObject obj) throw new HarnessException("schema#: expected an object root");
        var validator = new Deliverable(obj);
        validator.CheckSchema(obj, "");
        foreach (var (node, at) in validator.schemaPaths)
        {
            if (node is not JsonObject current || !current.TryGetPropertyValue("$ref", out var reference)) continue;
            var target = reference!.GetValue<string>();
            if (!target.StartsWith('#') || !Resolve(obj, Uri.UnescapeDataString(target[1..]), out var resolved) ||
                resolved is null || !validator.schemaPaths.ContainsKey(resolved))
                throw SchemaError(Child(at, "$ref"), "$ref", "expected a resolvable local schema pointer");
            validator.references.Add(node, resolved);
        }
        var states = new Dictionary<JsonNode, int>();
        foreach (var node in validator.schemaPaths.Keys) validator.CheckCycle(node, states);
        return validator;
    }

    void CheckSchema(JsonNode? node, string at)
    {
        if (node is null || (node is not JsonObject && node.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)))
            throw SchemaError(at, at.Split('/').LastOrDefault() ?? "schema", "expected an object or boolean schema; array-form items is not supported");
        schemaPaths.Add(node, at);
        if (node is not JsonObject obj) return;
        foreach (var (keyword, value) in obj)
        {
            var location = Child(at, keyword);
            switch (keyword)
            {
                case "$schema":
                case "$id":
                    if (at != "") throw SchemaError(location, keyword, "allowed at the root only");
                    break;
                case "$comment": case "title": case "description": case "default": case "examples":
                case "deprecated": case "readOnly": case "writeOnly": case "format":
                    break;
                case "properties": case "$defs": case "definitions":
                    if (value is not JsonObject map) throw SchemaError(location, keyword, "expected an object of schemas");
                    foreach (var (name, child) in map) CheckSchema(child, Child(location, name));
                    break;
                case "items": case "additionalProperties":
                    CheckSchema(value, location);
                    break;
                case "anyOf":
                    if (value is not JsonArray { Count: > 0 } branches) throw SchemaError(location, keyword, "expected a non-empty array of schemas");
                    for (var i = 0; i < branches.Count; i++) CheckSchema(branches[i], Child(location, i.ToString(CultureInfo.InvariantCulture)));
                    break;
                case "$ref":
                    if (!IsString(value)) throw SchemaError(location, keyword, "expected a local reference string");
                    break;
                case "type":
                    var types = value is JsonArray array ? array.ToArray() : [value];
                    if (types.Length == 0 || types.Any(t => !IsString(t) || t!.GetValue<string>() is not
                        ("object" or "array" or "string" or "number" or "integer" or "boolean" or "null")) ||
                        types.Select(t => t!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() != types.Length)
                        throw SchemaError(location, keyword, "expected a type name or a non-empty array of unique type names");
                    break;
                case "required":
                    if (value is not JsonArray names || names.Any(n => !IsString(n)) ||
                        names.Select(n => n!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() != names.Count)
                        throw SchemaError(location, keyword, "expected an array of unique member names");
                    break;
                case "enum":
                    if (value is not JsonArray) throw SchemaError(location, keyword, "expected an array");
                    break;
                case "const":
                    break;
                case "minLength": case "maxLength":
                    if (!IsInteger(value) || Compare(value!, JsonValue.Create(0)!) < 0)
                        throw SchemaError(location, keyword, "expected a non-negative integer");
                    break;
                case "minimum": case "maximum": case "exclusiveMinimum": case "exclusiveMaximum":
                    if (value?.GetValueKind() != JsonValueKind.Number) throw SchemaError(location, keyword, "expected a number");
                    break;
                default:
                    throw SchemaError(location, keyword, "unsupported keyword");
            }
        }
    }

    void CheckCycle(JsonNode node, Dictionary<JsonNode, int> states)
    {
        if (states.ContainsKey(node)) return;
        states[node] = 1;
        if (references.TryGetValue(node, out var target)) Visit(target, Child(schemaPaths[node], "$ref"), "$ref");
        if (node is JsonObject obj && obj["anyOf"] is JsonArray branches)
            for (var i = 0; i < branches.Count; i++) Visit(branches[i]!, Child(Child(schemaPaths[node], "anyOf"), i.ToString(CultureInfo.InvariantCulture)), "anyOf");
        states[node] = 2;

        void Visit(JsonNode next, string at, string keyword)
        {
            if (states.TryGetValue(next, out var state) && state == 1)
                throw SchemaError(at, keyword, "cycle without an instance step");
            CheckCycle(next, states);
        }
    }

    static HarnessException SchemaError(string at, string keyword, string detail) => new($"schema#{at}: {keyword}: {detail}");

    static bool IsString(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out _);

    static bool IsInteger(JsonNode? node)
    {
        if (node?.GetValueKind() != JsonValueKind.Number) return false;
        var text = node.ToJsonString().TrimStart('-');
        var e = text.IndexOfAny(['e', 'E']);
        var exponent = e < 0 ? System.Numerics.BigInteger.Zero : System.Numerics.BigInteger.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        var mantissa = e < 0 ? text : text[..e];
        var dot = mantissa.IndexOf('.');
        var fraction = dot < 0 ? 0 : mantissa.Length - dot - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        var significant = digits.TrimEnd('0');
        return significant.Length == 0 || exponent >= fraction - (digits.Length - significant.Length);
    }

    static int Compare(JsonNode left, JsonNode right)
    {
        var a = left.ToJsonString();
        var b = right.ToJsonString();
        if (decimal.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) &&
            decimal.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db)) return da.CompareTo(db);
        return double.Parse(a, NumberStyles.Float, CultureInfo.InvariantCulture).CompareTo(double.Parse(b, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    public DeliverableCheck Validate(string text, JsonArray input, StructuredResult result, string idField = "id")
    {
        var originals = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in input)
        {
            if (item is not JsonObject obj || obj[idField] is not JsonValue value || !value.TryGetValue<string>(out var id) || !originals.TryAdd(id, obj))
                throw new HarnessException($"input: each item must have a unique string {idField}");
            foreach (var field in result.Immutable ?? [])
                if (!obj.ContainsKey(field)) throw new HarnessException($"input: id \"{id}\" lacks immutable field \"{field}\"");
        }
        try
        {
            var document = Parse(text);
            var errors = new List<ValidationError>();
            Evaluate(schema, document, "", errors);
            CheckIdentity(document, originals, result, idField, errors);
            return new(errors.Count == 0 ? document : null, new ValidationResult { Ok = errors.Count == 0, Errors = errors });
        }
        catch (JsonException error)
        {
            return new(null, new ValidationResult { Errors = [new() { At = "", Message = "parse: " + error.Message }] });
        }
    }

    void Evaluate(JsonNode node, JsonNode? instance, string at, List<ValidationError> errors)
    {
        if (node is not JsonObject obj)
        {
            if (!node.GetValue<bool>()) Add(errors, at, "false: no value is allowed");
            return;
        }
        if (references.TryGetValue(node, out var target)) Evaluate(target, instance, at, errors);
        if (obj.TryGetPropertyValue("type", out var type))
        {
            var types = type is JsonArray array ? array : new JsonArray(type!.DeepClone());
            if (!types.Any(t => MatchesType(t!.GetValue<string>(), instance)))
                Add(errors, at, "type: expected " + type!.ToJsonString());
        }
        if (obj["enum"] is JsonArray choices && !choices.Any(c => JsonNode.DeepEquals(c, instance)))
            Add(errors, at, "enum: expected one of the listed values");
        if (obj.TryGetPropertyValue("const", out var constant) && !JsonNode.DeepEquals(constant, instance))
            Add(errors, at, "const: expected " + (constant?.ToJsonString() ?? "null"));
        if (obj["anyOf"] is JsonArray branches && !branches.Any(branch =>
        {
            var branchErrors = new List<ValidationError>();
            Evaluate(branch!, instance, at, branchErrors);
            return branchErrors.Count == 0;
        })) Add(errors, at, $"anyOf: expected at least one of {branches.Count} branches");

        if (instance is JsonObject value)
        {
            if (obj["required"] is JsonArray required)
                foreach (var name in required.Select(n => n!.GetValue<string>()))
                    if (!value.ContainsKey(name)) Add(errors, at, $"required: missing member \"{name}\"");
            var properties = obj["properties"] as JsonObject;
            foreach (var (name, member) in value)
            {
                var memberAt = Child(at, name);
                if (properties is not null && properties.TryGetPropertyValue(name, out var property))
                    Evaluate(property!, member, memberAt, errors);
                else if (obj.TryGetPropertyValue("additionalProperties", out var additional))
                {
                    if (additional!.GetValueKind() == JsonValueKind.False)
                        Add(errors, memberAt, "additionalProperties: unexpected member");
                    else Evaluate(additional, member, memberAt, errors);
                }
            }
        }
        if (instance is JsonArray values && obj.TryGetPropertyValue("items", out var items))
            for (var i = 0; i < values.Count; i++) Evaluate(items!, values[i], Child(at, i.ToString(CultureInfo.InvariantCulture)), errors);
        if (instance?.GetValueKind() == JsonValueKind.String)
        {
            var length = JsonValue.Create(instance.GetValue<string>().EnumerateRunes().Count())!;
            if (obj.TryGetPropertyValue("minLength", out var min) && Compare(length, min!) < 0)
                Add(errors, at, "minLength: expected at least " + min!.ToJsonString() + " Unicode code points");
            if (obj.TryGetPropertyValue("maxLength", out var max) && Compare(length, max!) > 0)
                Add(errors, at, "maxLength: expected at most " + max!.ToJsonString() + " Unicode code points");
        }
        if (instance?.GetValueKind() == JsonValueKind.Number)
        {
            foreach (var keyword in new[] { "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum" })
            {
                if (!obj.TryGetPropertyValue(keyword, out var bound)) continue;
                var comparison = Compare(instance, bound!);
                var invalid = keyword switch
                {
                    "minimum" => comparison < 0,
                    "maximum" => comparison > 0,
                    "exclusiveMinimum" => comparison <= 0,
                    _ => comparison >= 0,
                };
                if (invalid) Add(errors, at, keyword + ": expected " + (keyword.Contains("Minimum", StringComparison.Ordinal) || keyword == "minimum" ? "above " : "below ") + bound!.ToJsonString() +
                    (keyword.StartsWith("exclusive", StringComparison.Ordinal) ? " (exclusive)" : " (inclusive)"));
            }
        }
    }

    static bool MatchesType(string type, JsonNode? instance) => type switch
    {
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "string" => instance?.GetValueKind() == JsonValueKind.String,
        "number" => instance?.GetValueKind() == JsonValueKind.Number,
        "integer" => IsInteger(instance),
        "boolean" => instance?.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        "null" => instance is null,
        _ => false,
    };

    static void CheckIdentity(JsonNode? document, Dictionary<string, JsonObject> originals, StructuredResult result, string idField, List<ValidationError> errors)
    {
        var at = result.ItemsAt ?? throw new HarnessException("result.itemsAt is required");
        if (!Resolve(document, at, out var node) || node is not JsonArray answers)
        {
            Add(errors, at, "identity: expected an answer array at itemsAt");
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matched = new List<(JsonObject Answer, string Id, string At)>();
        for (var i = 0; i < answers.Count; i++)
        {
            var itemAt = Child(at, i.ToString(CultureInfo.InvariantCulture));
            if (answers[i] is not JsonObject answer)
            {
                Add(errors, itemAt, "identity: expected an answer object");
                continue;
            }
            if (answer[idField] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id))
            {
                Add(errors, Child(itemAt, idField), $"identity: expected a string {idField}");
                continue;
            }
            if (!originals.ContainsKey(id)) Add(errors, Child(itemAt, idField), $"identity: unknown id \"{id}\"");
            else
            {
                if (!seen.Add(id) && result.Identity != "many") Add(errors, Child(itemAt, idField), $"identity: duplicate id \"{id}\"");
                matched.Add((answer, id, itemAt));
            }
        }
        foreach (var id in originals.Keys.Where(id => result.Identity != "many" && !seen.Contains(id)))
            Add(errors, at, $"identity: missing id \"{id}\"");
        foreach (var (answer, id, itemAt) in matched)
            foreach (var field in result.Immutable ?? [])
                if (!answer.TryGetPropertyValue(field, out var value) || !JsonNode.DeepEquals(value, originals[id][field]))
                    Add(errors, Child(itemAt, field), $"immutable: id \"{id}\" field \"{field}\" changed or is missing");
    }

    static void Add(List<ValidationError> errors, string at, string message) => errors.Add(new() { At = at, Message = message });

    static string Child(string at, string name) => at + "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    static bool Resolve(JsonNode? root, string pointer, out JsonNode? value)
    {
        value = root;
        if (pointer == "") return true;
        if (!pointer.StartsWith('/')) return false;
        foreach (var token in pointer[1..].Split('/'))
        {
            if (Regex.IsMatch(token, "~(?![01])", RegexOptions.CultureInvariant)) return false;
            var name = token.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (value is JsonObject obj && obj.TryGetPropertyValue(name, out var property)) value = property;
            else if (value is JsonArray array && (name == "0" || (name.Length > 0 && name[0] is >= '1' and <= '9')) &&
                int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count) value = array[index];
            else return false;
        }
        return true;
    }

    static JsonNode? Parse(string text)
    {
        try { return JsonNode.Parse(text, documentOptions: new() { AllowDuplicateProperties = false }); }
        catch (JsonException)
        {
            var blocks = new List<string>();
            var content = new StringBuilder();
            char marker = '\0';
            var width = 0;
            var json = false;
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                var run = trimmed.TakeWhile(c => c == (marker == '\0' ? trimmed.FirstOrDefault() : marker)).Count();
                if (marker == '\0')
                {
                    if (trimmed.Length == 0 || trimmed[0] is not ('`' or '~') || run < 3) continue;
                    marker = trimmed[0];
                    width = run;
                    json = trimmed[run..].Trim().Equals("json", StringComparison.Ordinal);
                    content.Clear();
                }
                else if (run >= width && trimmed[run..].Length == 0)
                {
                    if (json) blocks.Add(content.ToString());
                    marker = '\0';
                }
                else if (json) content.AppendLine(line);
            }
            if (blocks.Count != 1 || (marker != '\0' && json))
                throw new JsonException("expected one JSON document or exactly one fenced json block");
            return JsonNode.Parse(blocks[0], documentOptions: new() { AllowDuplicateProperties = false });
        }
    }
}
