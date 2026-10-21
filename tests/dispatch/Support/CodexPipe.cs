using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace Dispatch.Tests.Support;

internal sealed class CodexPipe : IAsyncDisposable
{
    readonly Pipe input = new();
    readonly Pipe output = new();
    readonly StreamReader reader;
    readonly Stream writer;
    public CodexRpc Client { get; }

    public CodexPipe(Func<CodexServerRequest, CancellationToken, Task<CodexReply>>? handler = null,
        Action<string>? protocolError = null, Func<string>? exitDetail = null)
    {
        reader = new(input.Reader.AsStream(), new UTF8Encoding(false, true), false);
        writer = output.Writer.AsStream();
        Client = new(input.Writer.AsStream(), output.Reader.AsStream(), handler, protocolError, exitDetail);
    }

    public async Task<JsonElement> ReadAsync(CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await ReadLineAsync(token));
        return doc.RootElement.Clone();
    }

    public async Task<string> ReadLineAsync(CancellationToken token) =>
        await reader.ReadLineAsync(token) ?? throw new EndOfStreamException("client closed its input");

    public async Task SendAsync(string line, CancellationToken token) =>
        await writer.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), token);

    public Task EndAsync() => output.Writer.CompleteAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        reader.Dispose();
        await writer.DisposeAsync();
    }
}
