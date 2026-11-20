---
name: test-anti-patterns
description: >
  Audits an existing test file or suite in any language for pragmatic
  anti-patterns and quality issues, producing a severity-ranked report. Use when
  asked to review tests, find false-confidence assertions, investigate flakiness
  or order dependence, identify duplication or magic values, or judge whether
  assertions repeatedly verify only one facet of behavior. DO NOT USE FOR:
  writing or running tests, coverage/CRAP metrics, behavioral gap analysis, full
  assertion-diversity metrics, an academic smell catalog, or fixing the tests it
  audits (report-only).
license: MIT
---

# Test Anti-Pattern Detection

Quick, pragmatic analysis of test code in any supported language for anti-patterns and quality issues that undermine test reliability, maintainability, and diagnostic value.

> **Language-specific guidance**: Load the `deniz-dotnet-general:test-analysis-extensions` skill and read its extension file matching the target codebase (e.g., `extensions/dotnet.md`, `extensions/python.md`, `extensions/typescript.md`, `extensions/go.md`). The extension file tells you which sleep / time / random / skip / setup-teardown / mystery-guest APIs to look for in that language. If its files are absent, use the built-in framework rules below.

## When to Use

- User asks to review test quality or find test smells
- User wants to know why tests are flaky or unreliable
- User asks "are my tests good?" or "what's wrong with my tests?"
- User requests a test audit or test code review
- User wants diagnostic findings before deciding what to improve

## When Not to Use

- User wants to write new tests from scratch (out of scope; the user can invoke `/deniz-dotnet-general:code-testing`)
- User wants direct implementation fixes rather than a diagnostic review (use the relevant write/edit skill)
- User wants to run or execute tests (use `run-tests` for .NET)
- User wants to migrate between test frameworks or versions (use migration skills)
- User wants coverage collection or coverage/CRAP metrics
- User asks whether tests would catch a bug or wants behavioral/pseudo-mutation gaps (use `test-gap-analysis`)
- User wants test-mix or trait/category distributions
- User wants a deep formal test-smell audit with an academic taxonomy

## Inputs

| Input | Required | Description |
|-------|----------|-------------|
| Test scope | No | Test files, classes, directory, or project to analyze. Discover from the current workspace when omitted. |
| Production code | No | The code under test, for context on what tests should verify |
| Specific concern | No | A focused area like "flakiness" or "naming" to narrow the review |

Read [the remaining instructions](SKILL-CONTINUATION.md) to continue.
