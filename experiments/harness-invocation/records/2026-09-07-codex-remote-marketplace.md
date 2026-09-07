---
record_id: codex-remote-marketplace-2026-09-07
date: 2026-09-07
repo_head: add06735640da296ea6e24e7ec6cb4f0797180dc
kind: harness-structural
summary: Codex CLI added, installed from, upgraded, and removed the public GitHub marketplace.
isolation_ok: true
---

# Codex remote Git marketplace

This tier-1 record captures a credential-free Codex CLI 0.153.4 run against the public repository at
commit `add06735640da296ea6e24e7ec6cb4f0797180dc`. The run used a new `CODEX_HOME` below the external
lab, outside the repository and real user profile.

## Results

| Probe | Observed result | Status |
|---|---|---|
| Add public Git marketplace | resolved `deniz-skills` from the GitHub HTTPS URL | pass |
| Inspect cloned source | origin URL matched; HEAD was the `repo_head` above | pass |
| List available plugins | Akka 0.3.1, Aspire 0.3.3, General 0.9.1, Process 0.6.0 | pass |
| Install from Git marketplace | `deniz-process@deniz-skills` installed and enabled at 0.6.0 | pass |
| Upgrade marketplace | selected and upgraded `deniz-skills`; zero errors | pass |
| Remove plugin | `deniz-process@deniz-skills` removed | pass |
| Remove marketplace | marketplace clone and configuration removed | pass |

## Commands

```text
codex plugin marketplace add https://github.com/Blind-Striker/agent-skills-and-plugins --json
codex plugin list --available --marketplace deniz-skills --json
codex plugin add deniz-process@deniz-skills --json
codex plugin list --marketplace deniz-skills --json
codex plugin marketplace upgrade deniz-skills --json
codex plugin remove deniz-process@deniz-skills --json
codex plugin marketplace remove deniz-skills --json
```

The installed cache and cloned marketplace were both below the disposable profile. They were absent
after cleanup. This proves Codex CLI's remote Git transport for the committed marketplace; it does
not prove ChatGPT desktop installation-state synchronization or universal-directory publication.
