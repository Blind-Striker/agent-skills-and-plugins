# badgekit

A small badge-issuing toolkit: a library, a console app, tests, and a benchmark project.

| Path | What it is |
|---|---|
| `src/BadgeKit/` | Labels, key validation, the badge serializers, and the issuer |
| `src/BadgeKit.Cli/` | Prints a badge for a key, owner, and label |
| `tests/BadgeKit.Tests/` | Unit tests, plus integration tests over `samples/` |
| `benchmarks/BadgeKit.Benchmarks/` | BenchmarkDotNet benchmarks, outside the solution |
| `samples/` | Badge request samples |
| `logs/app.jsonl` | A structured application log |

Build and test from the repository root with `dotnet build` and `dotnet test`. Package versions are
managed centrally in `Directory.Packages.props`. `BADGEKIT_PROFILE=current` switches labels to the
compact style; any other value, or none, keeps the legacy style.
