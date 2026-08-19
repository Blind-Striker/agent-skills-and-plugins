# Session Pickup Prompt — Akka Curation

Date: 2026-08-18

---

## Commands to run

1. `git status --short`
2. `git log --oneline -5`
3. `npm run inventory`
4. `rg -n -C 2 "akka" docs/inventory.md`
5. `rg --files external/dotnet-skills | rg "akka"`
6. `rg -n "source:|exclude:|invocation:|as:|body:|merged_from:|depends_on:" curation/deniz-dotnet-akka.yaml curation/deniz-dotnet-aspire.yaml`
7. `npm run validate`
8. `npm run install:opencode -- status`

## Deltas vs `docs/ROADMAP.md`

- `Next Up` item 1: replace the Akka curation entry with only the decisions still unresolved after
  the user-guided take, merge, and exclusion pass; remove it when the Module closes.
- `Current State`: replace the Akka starter wording with the resulting curated and installed posture
  after build, validation, and the approved installer Update complete.
- `Next Up` item 3: update the cross-Module placement entry only if the Akka/Aspire ownership and edge
  direction are explicitly decided with both manifests open; otherwise leave it unchanged.
