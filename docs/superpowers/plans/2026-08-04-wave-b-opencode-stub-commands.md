# Wave B — OpenCode stub commands for bundled manual conversions Implementation Plan

Date: 2026-08-05

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `manual` skill-to-command conversion that carries bundled files stops pasting its whole body into the chat. The emitter parks the full body as `BODY.md` beside the bundle, emits a short command that tells the model how to find it in supported project-local and global installs, and repoints parked self-links from `SKILL.md` to `BODY.md`.

**Architecture:** `emitOpenCodeSkill` remains the single adapter point. Unit tests fix the output contract before its implementation changes; generated-tree and ledger checks prove the real build; an isolated OpenCode smoke proves both distribution mount forms before documentation narrows the Known Gap.

**Tech Stack:** Node's built-in test runner, TypeScript, the repository build/validate commands, PowerShell experiment runners, and the OpenCode CLI.

## Global Constraints

- Record `git rev-parse HEAD` as `<wave-b-start>` before Task 1. Use it for every scope and ledger comparison.
- Behavior changes only for `manual` conversions with at least one bundled file. Bundle-less `manual` output and all `both` output remain byte-for-byte equivalent to their pre-wave forms.
- The parked body is `skills/<name>/BODY.md`, never `SKILL.md`; a parked directory must remain absent from model discovery.
- The command names both supported distribution paths in prose: `.opencode/skills/<name>/BODY.md` for a project-local install and `~/.config/opencode/skills/<name>/BODY.md` for a global install. It contains no `@` file reference. `OPENCODE_CONFIG_DIR` remains an experiment mount, not a distribution promise.
- The stub passes `$ARGUMENTS` visibly. The runtime smoke must prove that the model reads the selected `BODY.md` and receives the argument; file existence alone is not evidence.
- No silent loss (ADR-0002): the report says that `BODY.md` was parked. `BODY.md` is not repeated in the parenthetical bundle-file list.
- Run `npm test` and `npm run typecheck` after each `tools/` edit. Run `npm run build && npm run validate` before committing generated output.

---

### Task 1: Pin the emitter contract with failing tests

**Files:**
- Modify: `tools/build.test.ts`

- [ ] **Step 1: Add a bundled-manual contract test** using `sp/skills/beta`, whose fixture carries `references/notes.md`.

The test must assert all of the following in one arrangement:

```ts
const bodyPath = join(root, "opencode", "skills", "beta", "BODY.md");
assert.ok(existsSync(bodyPath), "the full body is parked beside its bundle");
assert.ok(!existsSync(join(root, "opencode", "skills", "beta", "SKILL.md")), "manual stays undiscoverable");

const upstream = parseDoc(readFileSync(join(root, "external", "sp", "skills", "beta", "SKILL.md"), "utf8"));
assert.equal(readFileSync(bodyPath, "utf8"), upstream.body, "BODY.md is the complete parsed skill body");

const command = parseDoc(readFileSync(join(root, "opencode", "commands", "beta.md"), "utf8"));
const expectedStub = [
  "Read `skills/beta/BODY.md` from the active OpenCode configuration root before doing anything else.",
  "For a project-local install, use `.opencode/skills/beta/BODY.md`; for a global install, use `~/.config/opencode/skills/beta/BODY.md`.",
  "Follow that file as this command's full instructions.",
  "",
  "Arguments: $ARGUMENTS",
].join("\n");
assert.equal(command.body.trim(), expectedStub);
assert.doesNotMatch(command.body, /@(?:\.opencode|~\/\.config)/, "the path is prose, not a project-root-only @ reference");
assert.ok(!command.body.includes(upstream.body.trim()), "the command does not paste the full ceremony");
```

Also assert that the single beta parking report contains `body parked at skills/beta/BODY.md`, contains `references/notes.md` in its bundle list, does not use the `WARN` prefix or `not rewritten` wording, and does not list `BODY.md` inside the parenthesized bundle list.

- [ ] **Step 2: Add branch-preservation tests.** Use separate fixture roots.

For bundle-less `manual`, delete beta's `references/` directory before `buildAll`, derive the exact pre-wave command bytes with `serializeDoc({ frontmatter: { description: upstream.frontmatter.description }, body: upstream.body })`, and assert the emitted command equals them. Assert that `opencode/skills/beta/` does not exist and no parking report is emitted.

For `both`, curate bundled `delta`, derive the pre-wave skill and command documents from its upstream document, and assert both emitted files equal those forms byte for byte. Assert that no `BODY.md` exists and no parking report is emitted.

- [ ] **Step 3: Add a link-repoint test covering every promised spelling.** Append `[body-self](SKILL.md)` to beta's temporary source body, then add these bundled fixture files before building it as `manual`:

```text
README.md:                  [dot](./SKILL.md)
references/notes.md:       [parent](../SKILL.md)
references/nested/deep.md: [deep](../../SKILL.md)
```

After the build, assert `BODY.md`, `README.md`, `references/notes.md`, and `references/nested/deep.md` contain `BODY.md`, `./BODY.md`, `../BODY.md`, and `../../BODY.md` respectively and contain no `SKILL.md`. The paths are deliberately placed where each original link resolves to the source skill root; do not create a bare `SKILL.md` link in a nested file and mistake a still-dangling sibling path for success.

- [ ] **Step 4: Update the two existing expectations affected by the contract.**

- In `invocation sets the Claude flags and picks the OpenCode artifact`, assert that bundled manual beta has `BODY.md` while both delta does not.
- In `only a manual conversion reports parked files`, change beta's report assertion from `references/notes.md` as the main event to `body parked at skills/beta/BODY.md`; retain the delta no-report assertion.

- [ ] **Step 5: Run the red phase:** `npm test`.

Expected failures: the new bundled-manual contract test, the new link-repoint test, the new beta `BODY.md` assertion in the existing invocation test, and the updated existing parking-report assertion. The bundle-less-manual and `both` preservation tests must already pass; any other failure is unrelated and must be investigated before implementation.

---

### Task 2: Implement the bounded stub adapter

**Files:**
- Modify: `tools/build.ts` (`emitOpenCodeSkill` and its JSDoc)

- [ ] **Step 1: Rewrite the `emitOpenCodeSkill` JSDoc** so it states the current contract: bundled manual items park their parsed body as non-discoverable `BODY.md`, commands point at the supported project/global locations, and parked Markdown self-links are repointed. Remove the stale statements that the command body reaches the old bundle directly and references are not rewritten.

- [ ] **Step 2: Implement the parked body immediately after copy/husk handling.** Keep the current `cpSync`, `wantsSkill` branch, and empty-husk removal. Then add:

```ts
const bundledManual = !wantsSkill && existsSync(destSkill);
if (bundledManual) {
  writeFileSync(join(destSkill, "BODY.md"), doc.body);
  for (const file of listFiles(destSkill).filter((f) => f.endsWith(".md"))) {
    const path = join(destSkill, file);
    const body = readFileSync(path, "utf8").replaceAll(
      /(\]\((?:(?:\.\.\/)+|\.\/)?)SKILL\.md(?=[)#?\s])/g,
      "$1BODY.md",
    );
    writeFileSync(path, body);
  }
}
```

The regex accepts no prefix, one `./`, or one-or-more `../` segments and requires a Markdown-link boundary after `.md`. Writing `BODY.md` before the loop also repairs a root-relative self-link in the parked body itself.

- [ ] **Step 3: Select the command body by branch.** Keep inline `doc.body` for every non-bundled case. For `bundledManual`, use this exact stub:

```ts
const commandBody = bundledManual
  ? [
      `Read \`skills/${name}/BODY.md\` from the active OpenCode configuration root before doing anything else.`,
      `For a project-local install, use \`.opencode/skills/${name}/BODY.md\`; for a global install, use \`~/.config/opencode/skills/${name}/BODY.md\`.`,
      `Follow that file as this command's full instructions.`,
      "",
      `Arguments: $ARGUMENTS`,
    ].join("\n")
  : doc.body;
```

Pass `commandBody` to `serializeDoc`; do not alter command frontmatter.

- [ ] **Step 4: Replace the parked report.** Collect the original bundle only after writing/rewriting:

```ts
const parked = bundledManual ? listFiles(destSkill).filter((f) => f !== "BODY.md") : [];
if (bundledManual) {
  report.push(`opencode command ${name}: body parked at skills/${name}/BODY.md (bundle: ${parked.join(", ")})`);
}
```

Do not retain the old `WARN` or `not rewritten` clause.

- [ ] **Step 5: Verify the implementation:** `npm test && npm run typecheck`.

Expected: all tests pass and typecheck is clean. If a ledger test observes `BODY.md`, update only the expected parked-file list that now truthfully changed.

---

### Task 3: Rebuild and prove the real-tree delta

**Files:**
- Modify: `plugins/`, `opencode/`, `docs/ledger.json` (generated)
- Modify: `tools/validate.ts`, `tools/validate.test.ts` (L6 must scan a parked `BODY.md` as well as its command and report the actual source path)

- [ ] **Step 1:** Run `npm run build && npm run validate`.

Expected: `0 error(s)`. The `teach` and `writing-great-skills` dead self-link warning identities retire because those links now resolve to `BODY.md`; the standing `dotnet-devcert-trust`, `elements-of-style`, and `subagent-driven-development` findings remain. If either dead-link warning remains or any new identity appears, fix the emitter or test fixture; do not weaken validation.

- [ ] **Step 2: Review the generated command shape.** Confirm a retained bundled manual command such as `opencode/commands/dotnet-aot-compat.md` is only frontmatter plus the five-line stub, its corresponding `BODY.md` contains the former full body, `opencode/skills/writing-skills/BODY.md` exists, and a bundle-less manual command such as `grill-me` remains inline with no parked directory.

- [ ] **Step 3: Review the ledger semantically.** `git diff <wave-b-start> -- docs/ledger.json` must show `BODY.md` added to `opencode.parked` for every and only `manual` item that already had a non-empty parked list. Bundle-less `manual` and every `both` item remain unchanged; no Claude artifact, invocation, source, edge, or description field changes in this wave.

- [ ] **Step 4: Run all tool gates:** `npm test && npm run typecheck && npm run format:check && npm run lint && npm run build && npm run validate`.

Expected: green, `0 error(s)`, the two dead-self-link identities absent, and no new warning identity.

- [ ] **Step 5: Check scope.** `git diff <wave-b-start> --name-only` may contain only `tools/build.ts`, its directly affected tests, the L6 `tools/validate.ts` and `tools/validate.test.ts` follow-through, generated `opencode/` files, and `docs/ledger.json`. The Claude `plugins/` tree should regenerate byte-identically and therefore have no diff.

- [ ] **Step 6: Commit**

```bash
git add tools opencode docs/ledger.json
git commit -m "feat: bundled manual commands load parked bodies"
```

---

### Task 4: Prove project-local and global runtime resolution

**Files:**
- Create: `experiments/harness-invocation/stub-command-smoke.ps1`
- Create after a passing run: `experiments/harness-invocation/records/2026-08-05-opencode-stub-command-mounts.md`
- Modify: `experiments/harness-invocation/runbook.md`

- [ ] **Step 1: Add a repeatable smoke runner following `protocol.md`.** It must:

- Dot-source `common.ps1`, require the external lab via `Get-LabRoot`, support `-DryRun`, accept a required `-Leg` key resolved from the shared `$script:LegTable`, and use the protocol's timeout/kill discipline. Reject an unknown leg or unavailable provider before staging either mount.
- Generate a fresh `manual` beta fixture through the real `buildAll` adapter, not by hand-copying the stub literal. Replace only that temporary source skill's body with an unmistakable instruction to read the body, avoid all other work, and reply `ZX-STUB-BODY-RAN ZX-STUB-ARG`; retain a bundled file so the stub branch is selected.
- For the project leg, install only the generated `commands/beta.md` and parked `skills/beta/` under the scratch repository's `.opencode/`, with the isolated global config free of beta.
- For the global leg, remove the project-local fixture and install those generated artifacts under the isolated global config root resolved from `XDG_CONFIG_HOME`; leave `OPENCODE_CONFIG_DIR` unset.
- Before each model call, prove `beta` appears in resolved commands and does not appear in `opencode debug skill`. Invoke it with `opencode run --command beta ... "ZX-STUB-ARG"`.
- Parse the JSON event stream and require a read-tool input ending in the leg's expected `BODY.md` path plus final text containing both markers. Keep raw output only in the external lab. A response that guesses the marker without the observed read is a failure.
- Clean both fixture mounts after recording results. Refuse to run when isolation or mount exclusivity is not proven.

- [ ] **Step 2:** Add the runner command and its dry-run-first requirement to `runbook.md`; update that document's `Date:`.

- [ ] **Step 3:** Run the deterministic preflight and dry run:

```powershell
pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab
pwsh -NoProfile -File experiments/harness-invocation/stub-command-smoke.ps1 -DryRun -Leg grok
```

- [ ] **Step 4:** Run `pwsh -NoProfile -File experiments/harness-invocation/stub-command-smoke.ps1 -Leg grok`. Both project-local and global must pass. If either fails, fix the stub contract and repeat Tasks 2-3; do not narrow the Known Gap yet.

- [ ] **Step 5: Persist tier-2 evidence.** The record follows `records/README.md`, names the OpenCode version, model, emitter commit, runner revision, isolation checks, exact mount form per leg, observed read-path suffix, markers, and pass/fail. Include sanitized event excerpts sufficient to verify the read and response; include no credentials, tokens, or machine-specific paths. Link the raw external-lab location symbolically, not as a machine path.

- [ ] **Step 6: Commit** the runner, runbook, and passing record.

---

### Task 5: Documentation follow-through

**Files:**
- Modify: `docs/ROADMAP.md`
- Modify: `docs/research/skill-invocation-across-harnesses.md`

- [ ] **Step 1:** Update both documents' `Date:` values to `2026-08-05`.

- [ ] **Step 2: Replace the complete ROADMAP Known Gap beginning `Long-body ` `` `manual` `` ` conversions` with:**

```markdown
- **Some command copies still paste their whole body into the OpenCode chat.** A command is a
  template, and the TUI renders its body as the user's message. Bundled `manual` conversions emit a
  short stub whose project-local and global paths are runtime-probed; the remaining full-paste cases
  are `both` items and bundle-less `manual` items. Correctness is unaffected. Revisit only if those
  remaining command bodies become noisy in practice.
```

Keep the separate out-of-project folder-access Known Gap: the model still reads parked files outside the project under a global/config-root install, so the first permission prompt is unchanged.

- [ ] **Step 3: Replace the complete research bullet beginning `A typed command pastes its whole body` with:**

```markdown
- **A typed command pastes its command body into the chat as the message.** A command is a template,
  so inline `both` and bundle-less `manual` commands still behave this way. Bundled `manual`
  conversions now emit a short stub: on the OpenCode version named in the linked record, isolated
  project-local and global runs both read the parked `BODY.md` and followed it, while the parked
  directory remained absent from skill discovery. The measured stub result is recorded in
  [the OpenCode stub-command mount record](../../experiments/harness-invocation/records/2026-08-05-opencode-stub-command-mounts.md).
```

This is a full-paragraph replacement; do not leave the old `long-body manual conversions are noisy` clause before it. If the recorded version is not `1.18.7`, also revise the enclosing heading/opening sentence so the old real-tree observations remain attributed to `1.18.7` and the new stub observation is attributed only to the version named in the record.

- [ ] **Step 4:** Run `npm run build && npm run validate`; require `0 error(s)`, the expected standing warning identities, and no new warning identity, then commit the documentation.

---

### Task 6: Wave close-out

- [ ] **Step 1:** Run `npm test && npm run typecheck && npm run format:check && npm run lint && npm run build && npm run validate`; expect all checks green, `0 error(s)`, the dead-self-link identities absent, and no new warning identity.
- [ ] **Step 2:** Run `pwsh -NoProfile -File experiments/harness-invocation/selftest.ps1 -SkipLab`; expect all deterministic experiment checks green.
- [ ] **Step 3:** Confirm `emitOpenCodeSkill` JSDoc, implementation, tests, generated commands, ledger, ROADMAP, research, runbook, and runtime record all describe the same two supported mount forms and the same untouched branches.
- [ ] **Step 4:** `git diff <wave-b-start>..HEAD --name-only` must contain only the planned tools/tests, generated OpenCode/ledger output, experiment runner/evidence/runbook, and two updated documents. Stop and report any unrelated path.
