---
record_id: 2026-10-25-dispatch-prepare-windows-elevated
date: 2026-10-25
repo_head: 12830a968a874e41d7222d5db4be6f00b17f137f
kind: runtime-smoke
summary: Outside restore enabled elevated Windows Codex sandbox builds and tests; inside restore failed with NU1301.
isolation_ok: true
---

# Outside restore, elevated Windows sandbox

## Provenance

Tier-1 observations supplied by the coordinator, who took these measurements on 2026-10-25,
between 07:15 and 14:27 local time, on a Windows 11 host with Codex 0.163.0 and
`[windows] sandbox = "elevated"`. Three dispatch runs were used: `diag-sandbox`, `d9-norestore`,
and `d10`. The harness ran from this repository at `58975fe` (0.1.3) for the first run and
between `bf95339` and `12830a9` (0.1.4) for the other two; harness source is identical across
those 0.1.4 commits. Each worker ran in a scratch repository outside this repository.

## Observed outcomes

| Probe | Status | Outcome of the run |
|---|---|---|
| Outside restore, inside build/run | pass | Restored outside the sandbox, then run inside it, in a scratch repository: an exe built with `--no-restore` and ran; a plain `dotnet build` also passed, because restore reported up to date and needed no network. |
| Project reference | pass | A test project with a `ProjectReference` to a library built with `--no-restore`. |
| TUnit host | pass | The TUnit host ran its one test through `dotnet run --no-build`, which passed. |
| Microsoft.Testing.Platform | pass | `dotnet test --no-build` (Microsoft.Testing.Platform, `global.json` `test.runner`) ran its one test, which passed. |
| Inside restore | fail | The identical restore inside the sandbox failed with NU1301 (SChannel). |

## Limits

One host, one Codex version (0.163.0), no raw transcripts kept. This is not a rate, panel or
fraction claim, nor a replayable transcript. Per-probe run mapping, exact commands beyond those
reported above, project/package pins, worker routes and billed cost were not supplied; none are
inferred. These observations do not establish unelevated-mode behaviour, arbitrary .NET workloads,
MSYS compatibility or another host's enforcement. They predate the `prepare` field and motivate its
outside-restore route, not a claim that this feature was exercised in these runs.
