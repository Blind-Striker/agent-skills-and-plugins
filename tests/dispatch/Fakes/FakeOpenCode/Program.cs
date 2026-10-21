using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (Environment.GetEnvironmentVariable("FAKE_ENV_NAMES") is { } namesFile)
    await File.WriteAllTextAsync(namesFile, JsonSerializer.Serialize(Environment.GetEnvironmentVariables().Keys.Cast<string>().ToArray()));

if (args.FirstOrDefault() == "--pipe-parent")
{
    using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
    {
        UseShellExecute = false,
        ArgumentList = { "--pipe-child" },
    })!;
    await File.WriteAllTextAsync(args[1], child.Id.ToString());
    Console.Write("parent stdout");
    Console.Error.Write("parent stderr");
    return 0;
}

if (args.SequenceEqual(new[] { "--pipe-child" }))
{
    Console.Write("child stdout");
    Console.Error.Write("child stderr");
    // Keep inherited pipes open well beyond the runner's timeout and drain grace.
    await Task.Delay(TimeSpan.FromSeconds(12));
    return 0;
}

// The same native apphost also supplies the injected git process boundary.
if (Environment.GetEnvironmentVariable("FAKE_GIT_PROBE") == "warn")
{
    Console.Error.Write("warning: synthetic git diagnostic\n");
    Console.Write((args.LastOrDefault() == "HEAD" ? new string('b', 40) : new string('a', 40)) + "\n");
    return 0;
}

if (!args.SequenceEqual(new[] { "serve", "--stdio", "--port", "0" }))
{
    Console.Error.WriteLine("expected: serve --stdio --port 0");
    return 97;
}

var scenario = Environment.GetEnvironmentVariable("FAKE_OPENCODE_SCENARIO")
    ?? throw new InvalidOperationException("FAKE_OPENCODE_SCENARIO is required");
var record = Environment.GetEnvironmentVariable("FAKE_OPENCODE_RECORD")
    ?? throw new InvalidOperationException("FAKE_OPENCODE_RECORD is required");
var password = Environment.GetEnvironmentVariable("OPENCODE_PASSWORD")
    ?? throw new InvalidOperationException("the SDK lease credential is required");
var authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password));
var script = new Scenario(scenario, record);

// Never pass the launcher's argv to ASP.NET configuration or log before the readiness line.
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    Args = [],
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = "Production",
});
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
await using var app = builder.Build();
app.Run(async context =>
{
    var authenticated = context.Request.Headers.Authorization.ToString() == authorization;
    if (!authenticated)
    {
        await script.RecordAsync(context, null, authenticated, unexpected: false);
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { _tag = "UnauthorizedError", message = "invalid lease credential" }, context.RequestAborted);
        return;
    }
    await script.HandleAsync(context, app.Urls.Single());
});
await app.StartAsync();
Console.WriteLine(JsonSerializer.Serialize(new { url = app.Urls.Single() }));
await app.WaitForShutdownAsync();
return 0;
