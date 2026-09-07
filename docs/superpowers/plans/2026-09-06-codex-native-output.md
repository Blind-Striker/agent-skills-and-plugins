# Codex-native output implementation plan

Date: 2026-09-07

## Goal

Implement the approved [Codex-native output design](../specs/2026-09-06-codex-native-output-design.md)
as a third compiler target. The result is four generated Codex Plugins plus one generated repository
marketplace, with Codex-native invocation policy, reference localization, ledger/validator coverage,
and isolated CLI runtime evidence.

This plan is transient. Re-read live Git, the approved design, the three edited ADRs, current
architecture, `curation/SCHEMA.md`, and `docs/engineering/quality-gates.md` before executing it. Delete
this plan and the design specification when the implementation closes, after moving every durable
claim to its canonical owner.

## Baseline and boundaries

- Baseline planning tree: `83c09fa` plus the uncommitted approved Codex design documents and ADR
  edits. Re-resolve the actual base before execution.
- Preserve the unrelated `external/dotnet-agent-skills` worktree change exactly.
- The dependency-aware Selection work is complete through `83c09fa`; do not reopen or redesign its
  schema-2 Package mechanics for Codex.
- `codex/` and `.agents/plugins/marketplace.json` are generated committed review surfaces. Never
  hand-edit their final content.
- Codex extends the existing compile-time pipeline. All three targets share source resolution,
  omission, overlay/patch application, body assembly, dependency closure, provenance, attribution,
  and neutral reference facts before target-specific emission.
- A finalized Claude or OpenCode tree is not Codex's source. If the current pre-localization Plugin
  staging is generalized, treat it as internal common pipeline state rather than Claude output.
- Codex Plugins are distributed through Codex's native marketplace. They are not added to the
  OpenCode npm Package and do not share its installer, Install state, Module digest, Plan, Apply, or
  Recovery semantics.
- The first milestone supports Codex CLI and Codex in the ChatGPT desktop app. The IDE extension and
  native `.codex/agents/*.toml` distribution remain follow-up designs.
- Do not publish a Release, submit to the universal Plugins Directory, install into a real profile,
  or mutate user configuration as part of this plan.

## Commit and review boundaries

Use six implementation commits plus a final evidence/docs closeout. Extend the existing compiler
pipeline with one explicit target-neutral assembly boundary and a third target-specific emitter.
The assembly refactor is a separate behavior-neutral checkpoint: existing Claude and OpenCode
generated paths, contents, and modes must remain unchanged.

### Task 1: Land the accepted decision and research baseline

Files:

- `docs/adr/0002-multi-harness-output.md`
- `docs/adr/0005-invocation-intent-in-the-manifest.md`
- `docs/adr/0006-output-is-a-transformation.md`
- `docs/research/codex-native-plugin-and-skill-surfaces.md`
- `docs/ROADMAP.md`
- `docs/superpowers/specs/2026-09-06-codex-native-output-design.md`
- `docs/superpowers/plans/2026-09-06-codex-native-output.md`

Steps:

1. Review the ADR diff as current decision text, not a chronology. Keep `Status: Accepted`, update
   the material-decision dates, and let Git retain the prior wording.
2. Confirm the capability contract is identical everywhere:
   - `auto`: implicit required, explicit unspecified;
   - `manual`: implicit forbidden, explicit required;
   - `both`: implicit and explicit required; and
   - absent: passthrough or target default.
3. Confirm the research note separates official/documented behavior, local CLI introspection, and
   unmeasured runtime behavior.
4. Confirm ROADMAP makes Codex the active next item, retains the deferred composition-selection
   evidence question, and no longer lists Codex output as deferred.

Verification:

```text
npm run check:public-safety
git diff --check
```

Suggested commit:

```text
docs: design Codex-native output
```

### Task 2: Make the common assembly boundary explicit

Purpose: Codex must pass through the same compiler pipeline as Claude Code and OpenCode. Extract the
current shared resolution and body-assembly work into an explicit target-neutral boundary before
adding Codex emission. This is not a new output format, and no generated target tree becomes the
source of another target.

Files:

- Create `tools/lib/assemble.ts` and `tools/lib/assemble.test.ts`, or use an equivalently focused
  name after inspecting the live symbols.
- Modify `tools/build.ts` so the current Claude and OpenCode emitters consume the explicit common
  assembly result.
- Modify `tools/build.test.ts` and `tools/testutil.ts` only where focused assembly fixtures or
  regression assertions require it.

Design:

1. Preserve the current global preflight: manifests and initialized upstreams are loaded before
   generated output is touched; identity, source, overlay-lock, merge-source, patch, omission, and
   conversion problems remain aggregated before deletion.
2. Move the common work now spread across `buildAll`, `emitItem`, and Plugin staging behind one
   assembly contract. It contains resolved identity and kind, the parsed primary document, selected
   item-relative dependency files and modes, provenance, attribution inputs, invocation intent, and
   neutral semantic reference facts.
3. Apply `omit` before overlay or patch materialization, merge curator frontmatter, force the
   resolved identity, filter symlinks, and retain original-skill provenance exactly once in this
   shared stage.
4. Keep target decisions out of the assembly contract. Claude frontmatter policy, OpenCode
   command/agent shaping, Codex skill policy, and target reference spellings remain emitter work.
5. Feed both existing emitters from the new boundary without changing their order or behavior.
   Whether assembly is in memory or an internal temporary tree is an implementation choice; it must
   not be a committed/generated distribution tree.
6. Do not add Codex output in this checkpoint. Regenerate and require zero Git change under the
   existing generated Claude/OpenCode surfaces before proceeding.

Focused tests:

- each authored item is resolved and assembled once, then made available to both existing emitters;
- omit is applied before overlay/patch materialization;
- overlay and patch sources produce the same assembled document and dependency files as before;
- source skill converted to command/agent retains its current dependency closure;
- file-shaped commands and agents do not acquire unrelated sibling files;
- original skills resolve with own-source provenance;
- repeated source use creates distinct assembled items; and
- an assembly failure leaves all generated trees untouched.

Verification:

```text
node --test tools/lib/assemble.test.ts tools/build.test.ts
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
npm run build
git diff --exit-code -- plugins opencode .claude-plugin/marketplace.json dist docs/ledger.json docs/inventory.md
git status --short -- plugins opencode .claude-plugin/marketplace.json dist docs/ledger.json docs/inventory.md
```

Both Git commands must report no existing generated-target change before Task 3 begins. The scoped
`git diff` proves tracked path/content/mode identity; the scoped status also catches untracked paths.

Suggested commit:

```text
refactor: expose common artifact assembly
```

### Task 3: Add Codex plugin structures and deterministic emission

Files:

- Create `tools/lib/codex-plugin.ts`.
- Create `tools/lib/codex-plugin.test.ts`.
- Modify `tools/build.ts`.
- Modify `tools/build.test.ts`.
- Modify `tools/testutil.ts` to add `codexPluginPath(...)` only if repeated path construction warrants
  it.
- Generate `codex/<plugin>/...` and `.agents/plugins/marketplace.json` through `npm run build`.

Design:

1. Put Codex manifest and marketplace types, canonical serialization, path validation, and shared
   constants in `tools/lib/codex-plugin.ts`; keep `build.ts` orchestration-focused.
2. Preflight the flattened Codex skill namespace before deleting generated output. Detect
   same-plugin duplicates, case-insensitive aliases, Windows-hostile names, path escape, and any
   documented plugin/skill identity limit. List every collision in one build error.
3. After all preflights pass, remove only the exact `codex/` tree and generated
   `.agents/plugins/marketplace.json` parent as appropriate. Do not remove a broad `.agents/` tree if
   authored content later shares it.
4. Emit one `codex/<plugin>/.codex-plugin/plugin.json` per curation manifest. Derive stable name,
   version, description, repository, license, publisher, and interface metadata from owned manifest
   and root metadata. Use `"skills": "./skills/"`; do not declare empty MCP, hook, app, or asset
   components.
5. Emit one repository marketplace entry per plugin, sorted deterministically, with
   `source.path: "./codex/<plugin>"`, required install/authentication policy, category, and owned
   display metadata.
6. Emit repository `LICENSE` and exact source-specific notices into each Codex Plugin using the
   existing attribution owner.
7. Emit every common assembled item into Codex `skills/<name>/`. At this boundary it is enough to
   produce the structural skill; Task 4 owns final Codex frontmatter, invocation policy, and
   reference spelling.
8. Do not add Codex output to `package.json.files` or `tools/verify-package.ts`.

Focused tests:

- minimal and metadata-rich plugin manifest serialization;
- path rules and repository containment;
- deterministic marketplace ordering and source paths;
- empty curation manifest still emits a valid notice-bearing Codex Plugin;
- all three resolved source kinds plus an original skill emit under `skills/`;
- command/agent-to-skill conversion retains the dependency files selected from authored sources;
- collision preflight runs before deletion; and
- license, attribution, symlink filtering, and copied executable-mode behavior match the existing
  distribution guarantees.

Verification:

```text
node --test tools/lib/codex-plugin.test.ts tools/build.test.ts
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
```

Suggested commit:

```text
feat: emit native Codex plugins and marketplace
```

### Task 4: Add Codex invocation, shape adaptation, and reference rendering

Files:

- Modify `tools/lib/codex-plugin.ts` and its tests.
- Modify `tools/lib/rewrite.ts` and `tools/lib/rewrite.test.ts`.
- Modify `tools/lib/refs.ts` and `tools/lib/refs.test.ts` only if the shared semantic scanner needs a
  target-rendering boundary; do not teach authored-source scanning to infer bare prose.
- Modify `tools/build.ts` and `tools/build.test.ts`.
- Modify `tools/testutil.ts` only for focused fixtures.

Invocation:

1. Define a pure Codex policy resolver returning the resolved implicit and explicit capabilities
   plus whether `agents/openai.yaml` is required.
2. Emit `policy.allow_implicit_invocation: false` only for `manual`.
3. Emit no disabling policy for `auto` or `both`.
4. For absent invocation, filter unsupported source frontmatter but otherwise use Codex's target
   default. Do not translate upstream Claude flags into hidden Codex policy.
5. Keep explicit invocation available for every Codex skill and report the resolved capability in
   later ledger work. Never label an `auto` Codex skill model-only.

Shape and frontmatter:

1. Define the accepted Codex `SKILL.md` keys from current official documentation/specification and
   filter everything else.
2. Preserve the reusable body and dependency closure for source/resolved commands and agents.
3. Remove target-specific model, permission, mode, command-argument, and Claude/OpenCode invocation
   fields that Codex does not understand; report every dropped key.
4. Preserve persona text only when it changes how the procedure must be executed. Do not synthesize
   a custom-agent TOML or claim subagent identity.

References:

1. Add `codex` as a target style without treating the final Claude or OpenCode spelling as source.
2. Render a resolved skill identity as `$<plugin>:<skill>` when Codex needs an explicit pointer.
3. Because the current scanner encodes user pointers with a leading `/`, make target rendering aware
   of `Ref.kind`; do not produce `/$<plugin>:<skill>` and do not lose the model/pointer distinction in
   the ledger merely because both Codex spellings converge.
4. Keep detection on neutral authored facts. Validate final `$` spellings against the semantic
   resolution map instead of reparsing them as authored dependency declarations.
5. Add tests for token boundaries, renames, original skills, cross-plugin targets, model edges, user
   pointers, dangling targets, and a literal dollar amount that is not a reference.

Focused tests:

- `auto`, `manual`, `both`, and absent policy matrix;
- `manual` emits exactly one `agents/openai.yaml` with implicit invocation disabled;
- `auto` and `both` remain implicitly eligible and explicitly addressable;
- existing Claude/OpenCode invocation mappings remain unchanged unless a separate reviewed decision
  changes them;
- command and agent bodies become valid Codex skills without dead target metadata;
- Codex model and pointer references render valid `$plugin:skill` names; and
- reported drops are deterministic and complete.

Verification:

```text
node --test tools/lib/codex-plugin.test.ts tools/lib/rewrite.test.ts tools/lib/refs.test.ts tools/build.test.ts
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
```

Suggested commit:

```text
feat: adapt invocation and references for Codex
```

### Task 5: Extend ledger, validation, generated discipline, and current canon

Files:

- Modify `tools/lib/ledger.ts` and `tools/lib/ledger.test.ts`.
- Modify `tools/validate.ts` and `tools/validate.test.ts`.
- Modify `tools/repository-docs.test.ts`.
- Modify `.github/workflows/validate.yml`.
- Modify `AGENTS.md`.
- Modify `README.md`.
- Modify `CONTEXT.md`.
- Modify `curation/SCHEMA.md`.
- Modify `docs/architecture/transformation-and-emission.md`.
- Modify `docs/architecture/references-and-linking.md`.
- Modify `docs/architecture/distribution-and-installation.md`.
- Modify `docs/engineering/quality-gates.md`.
- Modify `docs/ROADMAP.md` only for state that changes at this boundary.
- Modify `package.json` and its lockfile only if repository-level descriptive metadata is updated;
  keep the OpenCode Package payload unchanged.
- Regenerate `docs/ledger.json`, `plugins/`, `opencode/`, `codex/`, `.claude-plugin/`,
  `.agents/plugins/marketplace.json`, and any mechanically affected `dist/` files.

Ledger:

1. Add a `codex` target projection without changing existing Claude/OpenCode meaning.
2. Record emitted artifact `skill`, namespaced identity, source/resolved kind, material kind
   transformation, implicit capability, explicit capability, policy files, semantic model/pointer
   edges, dropped metadata, and any target-specific body transformation.
3. Derive semantic edge kinds before Codex rendering; do not infer them back from `$` text.
4. Keep output ordering deterministic and add a second-build byte-identity assertion.

Validation:

1. Validate every `.codex-plugin/plugin.json` and repository marketplace entry against the owned
   local schema and path rules.
2. Compare expected plugin names from curation with actual `codex/` directories and marketplace
   entries in both directions.
3. Validate the flattened skill namespace, case aliases, required `name`/`description`, allowed
   frontmatter, manual `openai.yaml`, invocation capability matrix, relative asset closure, symlinks,
   file modes, and namespaced reference reachability.
4. Check the whole generated `codex/` tree for portability and path length alongside the existing
   targets.
5. Keep probabilistic selection out of deterministic validation.

Generated discipline and docs:

1. Add `codex/` and `.agents/plugins/marketplace.json` to the never-edit rules, CI freshness staging,
   README build-output sentence, repository-doc guard, quality-gate review list, and any generated
   tree enumerations.
2. Update current architecture from two emitters to three and document the actual common assembly,
   target-specific invocation, shape, reference, and distribution mechanics now present.
3. Update `curation/SCHEMA.md` with the capability semantics and a Codex output column. Keep absent as
   passthrough/target-default rather than a fourth enum value.
4. Document native marketplace installation for Codex CLI and the desktop support boundary. State
   plainly that IDE plugin and native custom-agent distribution are not included.
5. Keep OpenCode Package recipes and verification semantics unchanged. Codex output is repo
   marketplace content, not Package content.

Focused tests:

- ledger target projection and determinism;
- missing/extra plugin and marketplace entries;
- invalid manifests and escaping source paths;
- duplicate/case-colliding flattened skills;
- invocation policy mismatches for all four declaration states;
- dangling and wrongly rendered Codex references;
- unsupported frontmatter and symlink findings;
- README/CI/generated-path guard coverage; and
- existing Claude/OpenCode validation findings remain present and correctly worded.

Verification:

```text
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
npm run build
npm run inventory
npm run validate
git diff --exit-code -- plugins opencode .claude-plugin/marketplace.json dist
```

Review every generated Codex plugin, both marketplace files, all changed existing target files,
ledger projections, notices, and modes. Preserve the first generated diff, run build and inventory a
second time, and prove the second run adds no diff.

Suggested commit:

```text
feat: validate and document Codex output
```

### Task 6: Add isolated Codex installation and discovery evidence

Files:

- Create `experiments/harness-invocation/codex-matrix.ps1` or an equivalently focused runner.
- Modify `experiments/harness-invocation/common.ps1`, `lab.ps1`, `selftest.ps1`, `runbook.md`, and
  `README.md` only where the Codex leg genuinely shares their contract.
- Add minimal tracked Codex fixtures under `experiments/harness-invocation/fixtures/`.
- Create a dated sanitized record under `experiments/harness-invocation/records/` after a real run.
- Modify `docs/research/codex-native-plugin-and-skill-surfaces.md` only to cite the committed record
  and distinguish measured findings from the earlier source-derived baseline.

Isolation and structural probe:

1. Create the lab outside the repository and real profile. Point the environment variable
   `CODEX_HOME` at an already-created lab directory; never use the real Codex home.
2. Print and record only sanitized values for environment variables that can affect discovery.
3. Use a local fixture marketplace with nonsense plugin/skill names and positive/negative controls.
4. Exercise the CLI's JSON surfaces:
   - `codex plugin marketplace add <lab-root> --json`;
   - `codex plugin marketplace list --json`;
   - `codex plugin list --available --json`;
   - `codex plugin add <plugin>@<marketplace> --json`;
   - `codex plugin list --json`; and
   - `codex plugin remove <plugin>@<marketplace> --json`.
5. Assert all cache/config/state changes remain below the isolated Codex home and the external lab.
6. Add a dry-run/selftest path that validates commands, fixture construction, output ownership, and
   timeout cleanup without credentials or model tokens.

Behavioural probe:

1. Reuse the fixture's nonsense triggers and literal replies. Run only after a one-token liveness
   preflight succeeds.
2. Invoke Codex non-interactively with `codex exec --json --ephemeral` in an isolated project and
   disposable `CODEX_HOME`. Do not pass `--ignore-user-config`; Codex CLI 0.153.4 also hides that
   profile's installed plugins when the flag is present. Pass prompts through stdin or literal-safe
   argument arrays so PowerShell never expands `$plugin:skill`.
3. Prove explicit invocation for one ordinary and one manual skill.
4. For manual posture, pair an implicit negative with explicit positive control. A single miss does
   not prove the policy; inspect the event stream and repeat according to the protocol.
5. Sample implicit selection for `auto` and `both` with repeated runs. Record propensity, not a
   deterministic guarantee.
6. Install all four generated plugins and record large-catalog warnings, advertised skills, explicit
   addressability, and any omitted initial-list entries. Do not repartition a plugin unless this run
   demonstrates a concrete failure.
7. Exercise marketplace upgrade against a disposable Git or local-fixture revision if the CLI
   supports a deterministic isolated transition. Do not push or publish solely for the experiment.
8. Kill timed-out processes, mark them timed out, and retain raw credentialed JSONL only in the
   external lab.

Verification:

```text
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
```

The full selftest is conditional on the prepared external lab. Report a missing lab or credentials
as an unmeasured boundary, not a deterministic failure. A positive runtime-support claim requires
the actual isolated run and a committed sanitized record.

Suggested commit:

```text
test: measure Codex plugin discovery and invocation
```

### Task 7: Full-estate audit and closeout

1. Run `npm run inventory` before any item-level modification decision. Use the generated inventory
   to enumerate Codex candidates, then read each affected upstream body and dependency closure.
2. Review every generated Codex item for Claude/OpenCode-only assumptions. Do not use global string
   replacement. For each material mismatch, choose and record one of:
   - no change because the product reference is genuine;
   - deterministic Codex renderer rewrite;
   - authored per-target body transformation with manifest-owned rationale; or
   - explicit Codex exclusion with a nearby reason.
3. Pay special attention to the known current corpus: generated Markdown containing `Claude`,
   Claude-style slash pointers, command argument placeholders, agent persona/model fields, and
   sibling-file paths after kind conversion.
4. If per-target authored body ownership is required, stop and design the smallest manifest grammar
   before editing bodies. Update ADR-0001/architecture only if the ownership decision itself changes;
   do not hide the new axis inside emitter code.
5. Run the complete generated-output gate:

```text
npm test
npm run typecheck
npm run lint
npm run format:check
npm run check:public-safety
npm run build
npm run inventory
npm run validate
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
git diff --exit-code -- plugins opencode .claude-plugin/marketplace.json dist
```

6. Preserve the resulting generated diff, run `npm run build` and `npm run inventory` again, and
   prove the second run adds no change.
7. Review:
   - all four `codex/` plugin trees;
   - `.agents/plugins/marketplace.json`;
   - zero diff under existing `plugins/`, `opencode/`, `.claude-plugin/`, and `dist/` outputs;
   - every OpenCode schema-2 manifest and Module digest;
   - `docs/inventory.md` and all three ledger projections;
   - report lines for drops, conversions, warnings, and target-fit decisions; and
   - CI freshness paths and documentation claims.
8. Replace ROADMAP's active Codex item with only genuinely unfinished follow-ups. Keep IDE,
   custom-agent transport, catalog repartitioning, and public-directory submission deferred unless
   this milestone produced their trigger.
9. Move durable spec statements into their canonical owners, then delete this plan and the design
   specification. Retain the research note and experiment record.

Suggested closeout commit:

```text
docs: close Codex plugin verification
```

## Stop conditions

Stop and return to design rather than guessing if any of these occurs:

- Codex CLI behavior contradicts official invocation or marketplace documentation.
- `auto` requires disabling explicit invocation to satisfy a newly discovered curator need.
- A command or agent cannot become a skill without losing behavior, permissions, or dependency
  closure.
- A Codex target-fit edit requires body divergence but no authored ownership seam exists.
- The flattened Codex namespace collides and no manifest-owned rename is clearly correct.
- Large-catalog discovery omits required explicit skills or makes a whole plugin unusable.
- Native marketplace installation requires user-profile mutation beyond isolated `CODEX_HOME`.
- The common-pipeline refactor or Codex implementation changes any generated Claude or OpenCode
  path, content, or mode. Such a change is outside this initiative and requires a separate decision.
- A live change overlaps the same compiler, ledger, validator, roadmap, or experiment files.

## Expected effort

- Tasks 1–5: approximately 4.5–7 focused engineering days.
- Tasks 6–7: approximately 1.5–2.5 focused engineering days when the isolated lab and credentials
  are available.
- Total Plugin milestone: approximately 6–9.5 focused engineering days.
- A catalog repartition or new per-target body-ownership grammar adds approximately 2–4 days and
  requires its own design checkpoint.
