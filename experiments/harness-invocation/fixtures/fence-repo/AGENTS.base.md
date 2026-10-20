# Agent notes

badgekit is a small .NET 10 solution (`badgekit.slnx`): the library in `src/BadgeKit/`, a console app
in `src/BadgeKit.Cli/`, xUnit tests in `tests/BadgeKit.Tests/`, and BenchmarkDotNet benchmarks in
`benchmarks/BadgeKit.Benchmarks/`, which the solution does not include.

- The checks are `dotnet build` and `dotnet test` from the repository root.
- Package versions live in `Directory.Packages.props` (central package management).
- `BADGEKIT_PROFILE` selects the label style: `current`, or legacy for anything else.
