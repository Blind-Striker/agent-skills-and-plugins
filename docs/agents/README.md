# Agent handovers

Date: 2026-08-02

This directory holds session handover prompts and agent playbooks. For handovers, start from
[`handover-prompts/session-pickup-template.md`](handover-prompts/session-pickup-template.md), consume
an active handover against live git, and delete it when the follow-up ships. The
[reference-audit playbook](reference-audit-playbook.md) is the repeatable sweep for reference
problems the deterministic gates cannot see; run it after a curation wave and before closing a
module.

Shared harness guidance lives in the [adapter guide](../research/harness-adapters.md). Probe method
and operator guidance live under the
[harness invocation experiments](../../experiments/harness-invocation/protocol.md).
