# Session Pickup Prompt — Public Release Preparation

Date: 2026-08-24

## Commands to run

1. Resolve the checkout by normalized Git remote, not by local path:

   ```powershell
   git remote -v
   ```

   Continue only for `github.com/blind-striker/agent-skills-and-plugins`. Live Git, current canon,
   curation, overlays, and generated output outrank this handoff, `.scratchpad/`, and memory.

2. Search Memorizer for the repository and public-release work, then load these active records:

   - bootstrap manifest `6367169a-e0f1-4f60-92a7-26f652865d56`;
   - current closure record `bba362fb-d4fb-4186-851b-91b8b91b728c`;
   - documentation authority `0cedb1af-2a6b-4696-98a3-5ff84f344dee`;
   - installer safety/operations `308c887e-46d2-43e8-9e83-2d1a21f53148`.

   Do not restore the archived pre-move upstream-impact audits. Every subagent performs its own
   retrieval and uses only OpenAI Luna or Sol routes.

3. Read the repository bootstrap and the public-release owners before proposing changes:

   - `AGENTS.md` and `CONTEXT.md`;
   - `docs/ROADMAP.md`, especially Public release and Recommended Sequence;
   - `docs/engineering/documentation.md`, `workflow.md`, and `quality-gates.md`;
   - `docs/architecture/distribution-and-installation.md`, `transformation-and-emission.md`, and
     `references-and-linking.md`;
   - `docs/adr/0004-minimal-toolchain.md` and `docs/adr/README.md`;
   - root `README.md`, `package.json`, `.github/workflows/validate.yml`, `.gitmodules`,
     `skills/deniz-dotnet-general/NOTICE.md`, and all upstream `LICENSE` files;
   - `tools/build.ts`, especially marketplace generation, and the generated
     `.claude-plugin/marketplace.json` without editing it.

4. Establish the live repository and remote boundary without fetching or moving a pin:

   ```powershell
   git status --short
   git log --oneline -10
   git submodule status
   gh repo view Blind-Striker/agent-skills-and-plugins --json visibility,isPrivate,url,defaultBranchRef
   gh release list --repo Blind-Striker/agent-skills-and-plugins
   ```

   If the worktree or refs differ from the handoff's assumptions, refresh from live owners rather
   than repairing toward this prompt.

5. Measure current consumer state read-only. Do not Apply an installation during public preparation:

   ```powershell
   claude plugin marketplace list
   claude plugin list
   npm run install:opencode -- status
   npm run install:opencode -- install --all
   ```

   If installation becomes a separate approved task, preserve the Plan-first boundary and select
   General, Akka, and Aspire together; an all-Module Selection is complete.

6. Establish a fresh repository baseline before editing:

   ```powershell
   npm run inventory
   npm test
   npm run typecheck
   npm run lint
   npm run format:check
   npm run build
   npm run inventory
   npm run validate
   git diff --check
   ```

   Review every warning and generated diff. Do not call the repository public-ready from command
   success alone.

## Deltas vs `docs/ROADMAP.md`

- **Public release decision — licensing and attribution:** produce a source-to-distribution audit
  table for every submodule and original file estate: source license, copyright holder, files copied
  into each Plugin/Bundle/Package, and where the required notice travels. Decide and add a root
  license for this repository's original code, add accurate package license metadata, and design a
  generated or authored notice surface that preserves upstream MIT copyright and permission notices
  in redistributed output. Do not claim legal compliance from an MIT label alone; report unresolved
  questions for human/legal review.

- **Public release decision — secrets and machine paths:** add repository-wide CI checks for secrets,
  tokens, credentials, private keys, cookies, and machine-specific paths without committing real
  secret fixtures. Preserve the sole existing machine-path fixture exception and require a scanner
  change for any new allowlist. Scan the current tree, recursive submodules where appropriate,
  generated output, Package contents, and Git history before any visibility change. Treat a finding
  as a release blocker; never print or store secret values in a report or memory.

- **Public release decision — identity and contact:** inspect `tools/build.ts` and generated
  marketplace owner metadata. Present the current name/email, an approved public-contact
  alternative, and the format constraints to the curator. Change authored generation only after the
  curator decides what may be public; never patch generated marketplace JSON directly.

- **Public release decision — proof boundary:** present the intentionally upstream-owned Aspire
  runtime-example limit as an explicit release decision. A green compiler/linker/build does not prove
  every Microsoft or dotnet-skills CLI, TypeScript, testing, or package example against the eventual
  consumer environment. Recommend honest public wording and bounded smoke evidence; do not silently
  fork those bodies to manufacture a stronger claim.

- **Public release decision — transport and canon:** decide whether remote delivery remains the same
  GitHub Release tarball after the repository becomes public. Keep `package.json`'s `private: true`
  npm-publish guard unless npm publication is separately approved. If Release visibility/auth changes,
  update the single current owners together: `CONTEXT.md` Package definition,
  `docs/architecture/distribution-and-installation.md`, root `README.md`, and ADR-0004 under its
  living-revision rules. Update current mechanics and rationale, but preserve dated private-release
  research and experiment records as historical evidence.

- **Public release decision — public repository surface:** inspect README onboarding, security and
  support/contact expectations, issue/PR templates, generated inventory/ledger exposure, submodule
  URLs, release tags/assets/hashes, and GitHub Actions permissions. Add only the minimum public-facing
  material the chosen support model needs. Do not expose internal addresses, credentials, machine
  paths, or a private email the curator did not approve.

- **Public release decision — clean-checkout proof:** after all approved changes, use a fresh
  recursive-submodule checkout and run the full quality gate, generated-output review, second-run
  idempotence check, package-content test, and both Module reference audits. Inspect the exact public
  GitHub tree and a candidate Release asset. Record what was actually tested and retain upstream
  runtime-example limits; do not turn build evidence into a runtime-compliance claim.

- **Public release decision — visibility stop gate:** finish with a release-readiness table whose
  rows are licensing/notices, secrets/history, machine paths, owner metadata, Aspire proof boundary,
  public docs/support, Release transport, clean checkout, Package bytes/hash, and both harness
  consumption paths. Each row is `pass`, `blocked`, or `accepted limitation` with evidence. Keep the
  repository private while any blocker remains. Changing GitHub visibility, creating/uploading a
  public Release, pushing, or modifying real installations requires separate explicit approval after
  the curator reviews the complete diff and readiness table.

- **Documentation closeout:** keep every current claim in one owner, update `docs/ROADMAP.md` as gates
  close, and delete this handoff after public preparation either ships or is replaced. Do not leave
  completed release work as an accumulating roadmap chronology.
