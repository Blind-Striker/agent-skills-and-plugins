# Agent handovers

Date: 2026-10-08

This directory holds session handover prompts and agent playbooks. For handovers, start from
[`handover-prompts/session-pickup-template.md`](handover-prompts/session-pickup-template.md), consume
an active handover against live git, and delete it when the follow-up ships. The
[reference-audit playbook](reference-audit-playbook.md) is the repeatable sweep for reference
problems the deterministic gates cannot see; run it after a curation wave and before closing a
module.

Shared current harness and product guidance lives in
[transformation and emission](../architecture/transformation-and-emission.md),
[references and linking](../architecture/references-and-linking.md), and
[distribution and installation](../architecture/distribution-and-installation.md). Dated measured
behavior remains in research: the [adapter research](../research/harness-adapters.md) for Claude
Code and OpenCode 1, the [OpenCode 2 research](../research/opencode-2-target.md) for the
current OpenCode target, and the
[Codex surface research](../research/codex-native-plugin-and-skill-surfaces.md) with its
[generated-estate audit](../research/codex-generated-estate-audit.md) for Codex. Experiment method and
operator guidance live in the [protocol](../../experiments/harness-invocation/protocol.md), while
committed observations live in the [records index](../../experiments/harness-invocation/records/README.md).
