# Agent handovers

Date: 2026-08-25

This directory holds session handover prompts and agent playbooks. For handovers, start from
[`handover-prompts/session-pickup-template.md`](handover-prompts/session-pickup-template.md), consume
an active handover against live git, and delete it when the follow-up ships. The
[reference-audit playbook](reference-audit-playbook.md) is the repeatable sweep for reference
problems the deterministic gates cannot see; run it after a curation wave and before closing a
module.

The active cross-session pickup is the
[independent public-release review](handover-prompts/s1-public-release-independent-review.prompt.md).
It is a read-only-first onboarding and review brief covering public distribution, proof boundaries,
and lossless documentation preservation. Delete it when the review and approved follow-up close.

Shared current harness and product guidance lives in
[transformation and emission](../architecture/transformation-and-emission.md),
[references and linking](../architecture/references-and-linking.md), and
[distribution and installation](../architecture/distribution-and-installation.md). Dated measured
behavior remains in the [adapter research](../research/harness-adapters.md). Experiment method and
operator guidance live in the [protocol](../../experiments/harness-invocation/protocol.md), while
committed observations live in the [records index](../../experiments/harness-invocation/records/README.md).
