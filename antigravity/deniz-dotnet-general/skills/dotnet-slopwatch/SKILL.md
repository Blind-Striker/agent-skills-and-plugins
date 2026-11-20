---
name: dotnet-slopwatch
description: Use Slopwatch to detect LLM reward hacking in .NET code changes.
  Run after every code modification to catch disabled tests, suppressed
  warnings, empty catch blocks, and other shortcuts that mask real problems.
---

# Slopwatch: LLM Anti-Cheat for .NET

## When to Use This Skill

**Use this skill constantly.** Every time an LLM (including Claude) makes changes to:
- C# source files (.cs)
- Project files (.csproj)
- Props files (Directory.Build.props, Directory.Packages.props)
- Test files

Run slopwatch to validate the changes don't introduce "slop."

## What is Slop?

"Slop" refers to shortcuts LLMs take that make tests pass or builds succeed without actually solving the underlying problem. These are reward hacking behaviors - the LLM optimizes for apparent success rather than real fixes.

### Common Slop Patterns

| Pattern | Example | Why It's Bad |
|---------|---------|--------------|
| Disabled tests | `[Fact(Skip="flaky")]` | Hides failures instead of fixing them |
| Warning suppression | `#pragma warning disable CS8618` | Silences compiler without fixing issue |
| Empty catch blocks | `catch (Exception) { }` | Swallows errors, hides bugs |
| Arbitrary delays | `await Task.Delay(1000);` | Masks race conditions, makes tests slow |
| Project-level suppression | `<NoWarn>CS1591</NoWarn>` | Disables warnings project-wide |
| CPM bypass | `Version="1.0.0"` inline | Undermines central package management |

**Never accept these patterns.** If an LLM introduces slop, reject the change and require a proper fix.

---

## Installation

Ask the user before you install slopwatch, create, regenerate or update its baseline, add a
`[SlopwatchSuppress]` attribute or a config suppression, or commit the baseline or the tool manifest: each changes the
repository or the machine, and a suppression or a baseline entry you add yourself approves your own
slop. When the tool or the baseline is missing, say so and wait for the answer; running
`slopwatch analyze` against an existing setup needs no question.

**Which slopwatch runs.** When `.config/dotnet-tools.json` lists slopwatch, run every command through
the repository's pinned version: `dotnet tool run slopwatch analyze`, and the same for `init` and the
hook command. A plain `slopwatch` runs the global tool, whose version can differ from the pin. Use the
global tool only when the manifest does not list slopwatch. The commands below are written as
`slopwatch` for short.

### As a Local Tool (Recommended)

Add to `.config/dotnet-tools.json`:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "slopwatch.cmd": {
      "version": "0.5.0",
      "commands": ["slopwatch"],
      "rollForward": false
    }
  }
}
```

Then restore:
```bash
dotnet tool restore
```

### As a Global Tool

```bash
dotnet tool install --global Slopwatch.Cmd
```

---

## First-Time Setup: Establish a Baseline

Before using slopwatch on an existing project, create a baseline of current issues:

```bash
# Initialize baseline from existing code
slopwatch init

# This creates .slopwatch/baseline.json
git add .slopwatch/baseline.json
git commit -m "Add slopwatch baseline"
```

**Why baseline?** Legacy code may have existing issues. The baseline ensures slopwatch only catches **new** slop being introduced, not pre-existing technical debt.

**Baseline version 2 (slopwatch 0.5.0).** A baseline from an earlier version, or one with no `version`
property, makes `analyze` stop before it runs and ask for `slopwatch init --force` (with
`--baseline <path>` for a baseline elsewhere). That regenerates the whole baseline, so ask the user
first, and review the regenerated `.slopwatch/baseline.json` before it is committed.

---

## Usage During LLM Sessions

### After Every Code Change

Run slopwatch after any LLM-generated code modification:

```bash
# Analyze for new issues (uses baseline)
slopwatch analyze

# Use strict mode - fail on warnings too
slopwatch analyze --fail-on warning
```

### When Slopwatch Flags an Issue

**Do not ignore it.** Instead:

1. **Understand why** the LLM took the shortcut
2. **Request a proper fix** - be specific about what's wrong
3. **Verify the fix** doesn't introduce different slop

```
# Example: LLM disabled a test
❌ SW001 [Error]: Disabled test detected
   File: tests/MyApp.Tests/OrderTests.cs:45
   Pattern: [Fact(Skip="Test is flaky")]

# Correct response: Ask for actual fix
"This test was disabled instead of fixed. Please investigate why
it's flaky and fix the underlying timing/race condition issue."
```

### Updating the Baseline (Rare)

Only update the baseline when slop is **truly justified** and documented:

```bash
# Add current detections to baseline (use sparingly!)
slopwatch analyze --update-baseline
```

**Justification examples:**
- Third-party library forces a pattern (e.g., must suppress specific warning)
- Intentional delay for rate limiting (not test flakiness)
- Generated code that can't be modified

Document why in a code comment when updating baseline.

---

## Claude Code Hook Integration

Add slopwatch as a hook to automatically validate every edit. Create or update `.claude/settings.json`:

```json
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit|MultiEdit",
        "hooks": [
          {
            "type": "command",
            "command": "slopwatch analyze -d . --hook",
            "timeout": 60000
          }
        ]
      }
    ]
  }
}
```

The `--hook` flag:
- Only analyzes **git dirty files** (fast, even on large repos)
- Outputs errors to stderr in readable format
- Blocks the edit on warnings/errors (exit code 2)
- Claude sees the error and can fix it immediately

---

## CI/CD Integration

Add slopwatch to your CI pipeline as a quality gate:

### GitHub Actions

```yaml
jobs:
  slopwatch:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Install Slopwatch
        run: dotnet tool install --global Slopwatch.Cmd

      - name: Run Slopwatch
        run: slopwatch analyze -d . --fail-on warning
```

### Azure Pipelines

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Install Slopwatch'
  inputs:
    command: 'custom'
    custom: 'tool'
    arguments: 'install --global Slopwatch.Cmd'

- script: slopwatch analyze -d . --fail-on warning
  displayName: 'Slopwatch Analysis'
```

---

## Detection Rules

| Rule | Severity | What It Catches |
|------|----------|-----------------|
| SW001 | Error | Disabled tests (`Skip=`, `Ignore`, `#if false`) |
| SW002 | Warning | Warning suppression (`#pragma warning disable`, `SuppressMessage`) |
| SW003 | Error | Empty catch blocks that swallow exceptions |
| SW004 | Warning | Arbitrary delays in tests (`Task.Delay`, `Thread.Sleep`) |
| SW005 | Warning | Project file slop (`NoWarn`, `TreatWarningsAsErrors=false`) |
| SW006 | Error | CPM bypass (`VersionOverride`, inline `Version` attributes) |
| SW021 | Error | Any use of the `dynamic` keyword (from 0.5.0) |

No rule can be turned off. A finding goes away only when the code changes, or through
`[SlopwatchSuppress]`, a config suppression or the baseline, which are the user's call. For SW021, replace `dynamic` with a
static type; when you cannot, say why and ask.

From 0.5.0, generated code is skipped by default: files with an `<auto-generated>` header or a
generated name (`*.g.cs`, `*.Designer.cs`, `*.generated.cs`, `*.g.i.cs`). The `generatedCode.mode`
setting in `.slopwatch/config.json` (`skip`, `analyze`, `analyzeAndReport`) changes that.

---

## Configuration

Create `.slopwatch/config.json` to suppress a rule for specific paths, with a justification:

```json
{
  "suppressions": [
    {
      "ruleId": "SW002",
      "pattern": "**/Generated/**",
      "justification": "Generated code from tooling cannot be manually changed"
    }
  ],
  "globalSuppressions": []
}
```

Use `-c` or `--config` to point at a config file elsewhere, and `--exclude` to leave paths out of analysis entirely:

```bash
slopwatch analyze -d . --config path/to/config.json
slopwatch analyze -d . --exclude "**/Generated/**,**/obj/**,**/bin/**"
```

### Strict Mode (Recommended for LLM Sessions)

For maximum protection during LLM coding sessions, fail on warnings as well as errors:

```bash
slopwatch analyze -d . --fail-on warning
```

---

## The Philosophy: Zero Tolerance for New Slop

1. **Baseline captures legacy** - Existing issues are acknowledged but isolated
2. **New slop is blocked** - Any new shortcut fails the build/edit
3. **Exceptions require justification** - If you must update baseline, document why
4. **LLMs are not special** - The same rules apply to human and AI-generated code

The goal is to prevent the gradual accumulation of technical debt that occurs when LLMs optimize for "make the test pass" rather than "fix the actual problem."

---

## Quick Reference

```bash
# First time setup
slopwatch init
git add .slopwatch/baseline.json

# After every LLM code change
slopwatch analyze

# Strict mode (recommended)
slopwatch analyze --fail-on warning

# With stats (performance debugging)
slopwatch analyze --stats

# Update baseline (rare, document why)
slopwatch analyze --update-baseline

# JSON output for tooling
slopwatch analyze --output json
```

---

## When to Override (Almost Never)

The only valid reasons to update baseline or disable a rule:

| Scenario | Action | Required |
|----------|--------|----------|
| Third-party forces pattern | Update baseline | Code comment explaining why |
| Generated code (not editable) | Skipped by default from 0.5.0; otherwise add to exclude list | Document in config |
| Intentional rate limiting delay | Update baseline | Code comment, not in test |
| Legacy code cleanup | One-time baseline update | PR description |

**Invalid reasons:**
- "The test is flaky" → Fix the flakiness
- "The warning is annoying" → Fix the code
- "It works on my machine" → Fix the race condition
- "We'll fix it later" → Fix it now
