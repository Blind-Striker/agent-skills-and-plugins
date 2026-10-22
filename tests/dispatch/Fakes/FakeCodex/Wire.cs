using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

// raw is base64 of the entire frame, including its original LF or CRLF. message is a queryable view.
internal sealed class Wire
{
    readonly object gate = new();
    readonly string path;
    internal static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public Wire(string directory)
    {
        path = Path.Combine(directory, "wire.jsonl");
        File.WriteAllBytes(path, []);
    }

    public void Record(string direction, byte[] raw, JsonNode? message)
    {
        var line = new JsonObject
        {
            ["direction"] = direction,
            ["time"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["raw"] = Convert.ToBase64String(raw),
            ["message"] = message?.DeepClone(),
            ["unexpected"] = direction == "unexpected",
        };
        Append(line);
    }

    public void Unexpected(string detail) => Append(new JsonObject
    {
        ["direction"] = "unexpected", ["time"] = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        ["unexpected"] = true, ["detail"] = detail,
    });

    void Append(JsonObject entry)
    {
        lock (gate) File.AppendAllText(path, entry.ToJsonString(Json) + "\n", new UTF8Encoding(false));
    }

    public static async IAsyncEnumerable<byte[]> Lines(Stream stream)
    {
        var buffer = new byte[4096];
        using var line = new MemoryStream();
        int count;
        while ((count = await stream.ReadAsync(buffer)) != 0)
        {
            for (var index = 0; index < count; index++)
            {
                line.WriteByte(buffer[index]);
                if (buffer[index] != (byte)'\n') continue;
                yield return line.ToArray();
                line.SetLength(0);
            }
        }
        if (line.Length != 0) yield return line.ToArray();
    }
}
