# CI/CD and Tooling

CI/CD integration, advanced resource waiters, and Aspire CLI with MCP for AI-assisted development.

## Contents

- [CI/CD Integration](#cicd-integration)
- [Advanced Custom Resource Waiters](#advanced-custom-resource-waiters)
- [Aspire CLI and MCP Integration](#aspire-cli-and-mcp-integration)

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Integration Tests

on:
  push:
    branches: [ main, dev ]
  pull_request:
    branches: [ main, dev ]

jobs:
  test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v3

    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 9.0.x

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore -c Release

    - name: Run integration tests
      run: |
        dotnet test tests/YourApp.IntegrationTests \
          --no-build \
          -c Release \
          --logger trx \
          --collect:"XPlat Code Coverage"

    - name: Publish test results
      uses: actions/upload-artifact@v3
      if: always()
      with:
        name: test-results
        path: "**/TestResults/*.trx"
```

## Advanced Custom Resource Waiters

```csharp
public static class ResourceWaiters
{
    public static async Task WaitForSqlServerAsync(
        this DistributedApplication app,
        string resourceName,
        CancellationToken ct = default)
    {
        var resource = app.GetResource(resourceName);
        var connectionString = await resource.GetConnectionStringAsync(ct);

        var retryCount = 0;
        const int maxRetries = 30;

        while (retryCount < maxRetries)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(ct);
                return; // Success!
            }
            catch (SqlException)
            {
                retryCount++;
                await Task.Delay(1000, ct);
            }
        }

        throw new TimeoutException(
            $"SQL Server resource '{resourceName}' did not become ready");
    }

    public static async Task WaitForRedisAsync(
        this DistributedApplication app,
        string resourceName,
        CancellationToken ct = default)
    {
        var resource = app.GetResource(resourceName);
        var connectionString = await resource.GetConnectionStringAsync(ct);

        var retryCount = 0;
        const int maxRetries = 30;

        while (retryCount < maxRetries)
        {
            try
            {
                var redis = await ConnectionMultiplexer.ConnectAsync(
                    connectionString);
                await redis.GetDatabase().PingAsync();
                return; // Success!
            }
            catch
            {
                retryCount++;
                await Task.Delay(1000, ct);
            }
        }

        throw new TimeoutException(
            $"Redis resource '{resourceName}' did not become ready");
    }
}

// Usage
public async Task InitializeAsync()
{
    _app = await appHost.BuildAsync();
    await _app.StartAsync();

    // Wait for dependencies to be ready
    await _app.WaitForSqlServerAsync("yourdb");
    await _app.WaitForRedisAsync("cache");
}
```

## Aspire CLI and MCP Integration

Aspire 13.1+ includes MCP (Model Context Protocol) integration for AI coding assistants like Claude Code. This allows AI tools to query application state, view logs, and inspect traces.

### Installing the Aspire CLI

```bash
# Install the Aspire CLI globally
dotnet tool install -g aspire.cli

# Or update existing installation
dotnet tool update -g aspire.cli
```

### Using the Aspire MCP server

When an Aspire MCP server is connected to the session, use it first to query the running
application; otherwise use the Aspire CLI against the running AppHost. Never run `aspire mcp init`,
and never add a project-level MCP server: a host that already registers an `aspire` server would
get a duplicate, and configuring MCP is an explicit user opt-in. For when and how to configure the
Aspire MCP server, follow the
[Skills vs. MCP guidance](../aspire/SKILL.md#improving-ai-agent-support-aspire-agent-init) of the
Aspire router.

### MCP Capabilities

When connected, AI assistants can:
- **Query resources** - Get resource states, endpoints, health status
- **Debug with logs** - Access real-time console output from all services
- **Investigate telemetry** - View structured logs and distributed traces
- **Execute commands** - Run resource-specific commands
- **Discover integrations** - List available Aspire hosting integrations (Redis, PostgreSQL, Azure services)

### Benefits for Development

- AI assistants can see your actual running application state
- Debugging assistance uses real telemetry data
- No need for manual log copying/pasting
- AI can help correlate distributed trace spans

For more details, see:
- [Aspire MCP Configuration](https://aspire.dev/get-started/configure-mcp/)
