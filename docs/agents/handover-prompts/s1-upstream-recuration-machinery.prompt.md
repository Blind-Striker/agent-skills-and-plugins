# Session Pickup Prompt - Upstream Recuration And Machinery Proof

Date: 2026-08-20

---

## Commands to run

1. `git status --short`
2. `git log --oneline -10`
3. `git submodule status`
4. `npm run inventory`
5. `npm run validate`
6. `npm run install:opencode -- status`
7. `git -C external/superpowers fetch origin main`
8. `git -C external/superpowers diff --shortstat HEAD origin/main`
9. `git -C external/superpowers log --oneline HEAD..origin/main`
10. `git -C external/mattpocock-skills fetch origin main`
11. `git -C external/mattpocock-skills diff --shortstat HEAD origin/main`
12. `git -C external/mattpocock-skills log --oneline HEAD..origin/main`
13. `git -C external/dotnet-skills fetch origin master`
14. `git -C external/dotnet-skills diff --shortstat HEAD origin/master`
15. `git -C external/dotnet-skills log --oneline HEAD..origin/master`
16. `git -C external/dotnet-agent-skills fetch origin main`
17. `git -C external/dotnet-agent-skills diff --shortstat HEAD origin/main`
18. `git -C external/dotnet-agent-skills log --oneline HEAD..origin/main`
19. `git -C external/aspire-skills fetch origin main`
20. `git -C external/aspire-skills diff --shortstat HEAD origin/main`
21. `rg -n "source:|exclude:|invocation:|as:|body:|merged_from:|depends_on:" curation/*.yaml`
22. `node --test tools/sync.test.ts tools/build.test.ts tools/eject.test.ts tools/validate.test.ts`
23. Search Memorizer and load repository bootstrap `6367169a-e0f1-4f60-92a7-26f652865d56`,
    current state `f12c4642-1a1c-48bb-999e-08acb4b8cb7e`, Matt audit
    `7d9cf8db-5358-496e-b418-18a9fdf42e13`, Superpowers audit
    `65303ea6-4b92-4c68-ad25-ee619b9c24f2`, dotnet-agent-skills audit
    `927405a9-fc72-4823-b9e6-10c69b4224b3`, and dotnet-skills audit
    `65073a98-2f9e-4a62-87f3-e417211950b3`. Re-run the live comparisons; memory is an
    index, not pin authority.
24. Read `AGENTS.md`, `curation/SCHEMA.md`, `docs/engineering/workflow.md`,
    `docs/engineering/quality-gates.md`, `docs/architecture/transformation-and-emission.md`,
    `docs/architecture/references-and-linking.md`, `docs/agents/reference-audit-playbook.md`, and
    the current manifests and overlays before proposing a mutation.
25. Present the machinery-first scope to the user before editing. Recommend focused regression
    coverage and fixes for deleted/renamed source reporting and the CLI candidate-name universe;
    present frontmatter-staleness guarding as a separate include/defer choice rather than silently
    expanding the wave.
26. Dispatch read-only impact audits in parallel after live refs are resolved. Keep each audit
    isolated by submodule and require each agent to search Memorizer first. The current routing
    preference is Sol xhigh for the Matt-sized semantic audit, DeepSeek through OpenCode Go for
    dotnet-agent-skills and lower-cost source audits, and a different model lineage for final review;
    re-check the live model roster and user preference before dispatch.
27. Do not let parallel agents edit shared manifests, overlays, submodule pointers, generated trees,
    or documentation. Once advisory reports return, perform machinery and each accepted sync wave
    sequentially in the primary workspace.
28. Do not run an all-submodule sync. After the machinery checkpoint is approved and green, use one
    explicit command per accepted wave, beginning with `npm run sync -- dotnet-skills`, then
    `npm run sync -- superpowers`, `npm run sync -- mattpocock-skills`, and finally
    `npm run sync -- dotnet-agent-skills`. Re-resolve the target immediately before each command.
29. Before every take, exclusion, rename, invocation, shape, reference, merge, overlay, or patch
    decision, run fresh inventory and read the actual changed body, bundled files, dependency
    closure, and upstream old-to-new diff. Ask the user for every ambiguous semantic choice.
30. For every patch or overlay drift, use the `eject` review flow from
    `docs/engineering/workflow.md`; never bless from a hash report alone. Confirm fail-before-delete
    behavior and inspect the displayed upstream diff before `--bless --yes`.
31. Close each accepted submodule as its own reviewable wave: regenerate both harnesses, inspect
    Plugin and Bundle output plus Module manifests, marketplace, inventory, and ledger; run the
    applicable full quality matrix; perform the second build/inventory idempotence comparison; run
    the reference-audit playbook; update current documentation and memory; and request independent
    Standards/Spec review before the wave is called closed.
32. Do not mutate real Claude or OpenCode installations during machinery or recuration work. After
    all accepted waves close, present read-only plans first and require explicit approval before any
    Apply. Keep General, Akka, and Aspire selected together until installer dependency planning
    closes that requirement.

## Deltas vs `docs/ROADMAP.md`

- `Next Up` item 1, machinery checkpoint: implement only the user-approved sync guardrail scope
  before moving live pins. At minimum, make deleted/renamed sources report truthfully and derive
  candidate output names from ledger identities correctly, with regression fixtures that fail under
  the old behavior. Preserve existing fail-before-delete, patch-apply, overlay-stamp, and
  idempotence guarantees.
- `Next Up` item 1, dotnet-skills canary: treat this as the clean sync path. Review the flowing
  OpenTelemetry body and decide the new nullable-reference-types knowledge skill separately from
  the existing manual nullable migration ceremony. No Akka, Aspire, or overlay drift is expected;
  verify rather than assume it.
- `Next Up` item 1, Superpowers wave: re-cut the drifted `using-superpowers` patch, review and absorb
  or reject the `requesting-code-review` overlay addition, and make explicit decisions on the new
  brainstorming router and subagent-driven-development ruling behavior before accepting output.
- `Next Up` item 1, Matt wave: treat the source rename, deleted components, changed invocation
  posture, frontier-round grilling, shareable-HTML prototype, new cross-skill calls, merge-source
  drift, and the wait-what/questionnaire/wizard candidates as user-guided recuration. Replace or
  explicitly retire the old writing-great-skills identity; do not patch around a missing source.
- `Next Up` item 1, dotnet-agent-skills wave: recut the five drifted patches only after reviewing the
  changed source intent; decide the platform-detection posture, test-gap-analysis mutation boundary,
  testability-obstacle/scaffold candidates, and changed exclusion reasons independently. Keep the
  large eval/tooling-only diff outside shipped-content judgement.
- `Next Up` item 1, machinery closeout: remove the solved candidate-name and deleted/renamed-source
  clauses from the initiative when their tested fixes land; restore any explicitly deferred defect
  to `Known Gaps`. Keep frontmatter staleness, mode drift, symlink-boundary patches, scanner limits,
  and dependency-aware Module Selection unless this initiative implements and verifies them.
- `Current State`: after each accepted wave, record only the live pin, resulting Module posture,
  remaining warnings/proof limits, and verified real-installation state. Do not turn audit memories
  or this handover into current canon.
- `Next Up` item 1: remove the initiative only when all four source waves are closed or explicitly
  deferred with reasons. Delete this consumed handover at that point and leave only remaining work.
