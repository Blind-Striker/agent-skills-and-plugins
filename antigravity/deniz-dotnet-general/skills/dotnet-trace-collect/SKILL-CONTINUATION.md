## Common Pitfalls

| Pitfall | Solution |
|---------|----------|
| Using `dotnet-trace` on .NET Framework | `dotnet-trace` only works with modern .NET (.NET Core 3.0+). Use PerfView for .NET Framework. |
| PerfView without admin privileges | PerfView requires admin for ETW tracing. Fall back to `dotnet-trace` if admin is not available. |
| `perfcollect` in container without `SYS_ADMIN` | Containers drop `SYS_ADMIN` by default. Run with `--privileged` or add `SYS_ADMIN` capability, or fall back to `dotnet-trace`. |
| Huge trace files from long repros | On Windows, use PerfView `/StopOn` triggers that fire on the symptom you want to capture (e.g., `/StopOnPerfCounter`, `/StopOnGCEvent`, `/StopOnException`) with `/CircularMB` and `/BufferSizeMB`. **Never trigger on recovery** — the circular buffer continuously overwrites old data, so the interesting behavior may be lost by the time collection stops. |
| Diagnostic port not accessible in container | Mount `/tmp` as a shared volume between the app container and `dotnet-monitor` sidecar for the diagnostic Unix domain socket. |
| Forgetting to install tools in container image | Add `dotnet tool install` to your Dockerfile, or use `dotnet-monitor` as a sidecar to avoid modifying the app image. |
| Exposing `dotnet-monitor` with `--no-auth` in production | Keep auth enabled, bind to localhost, and use `kubectl port-forward` for access. Use `--no-auth` only for short-lived isolated debugging. |
| Collecting only CPU/thread-time trace for networking issues | CPU and thread-time traces alone do not show HTTP status codes, DNS timing, or connection pool behavior. Add the networking providers (`System.Net.Http`, `System.Net.NameResolution`, `System.Net.Security`, `System.Net.Sockets`) alongside the thread-time trace. |
| Enabling all networking providers when only one is needed | Each networking provider adds overhead. If the issue is clearly HTTP-level (5xx status codes), `System.Net.Http` alone may be sufficient. Add DNS, TLS, and socket providers when the root cause is unclear. |
