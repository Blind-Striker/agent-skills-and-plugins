# Session Pickup Prompt: Independent Public Release Review

Date: 2026-08-25

## Objective

Independently review the repository's public-release work from onboarding through distribution. The
review must establish what the live repository, generated artifacts, GitHub settings, and published
Package actually prove. It must also perform a lossless documentation audit, with special attention
to useful unique information removed from the former roadmap and public-release handover.

This is a review task, not an instruction to confirm that the release is correct. Do not begin by
editing. Do not treat this prompt, memory, prior agent verdicts, green commands, or the current roadmap
as evidence. Resolve every claim against live sources and report disagreements instead of repairing
them silently.

This user-requested review brief intentionally contains more onboarding and review dimensions than
the minimal pickup template. It does not own the mechanics it names: every checklist item must resolve
to the linked canonical source, and stale duplication in this handoff is itself a finding.

## Review posture

- Start read-only with respect to the target repository, Release, GitHub settings, and real harness
  profiles. Disposable clones, generated output inside those clones, Package downloads, and isolated
  test installations are required evidence writes; create them only under new temporary roots.
- Look for both false positive claims and missing claims. A review that finds no issue is valid only
  when the named proof boundaries were actually inspected.
- Do not infer legal compliance. Verify technical notice/license transport and identify questions
  requiring human or legal judgment.
- Never print a discovered secret, credential, private key, cookie, or personal email value. Report
  only category, path, line or commit, and consequence with values redacted.
- Git author/committer identity is ordinary provenance for this review. The curator explicitly did
  not request history rewriting. Current public documents and generated metadata use a noreply
  contact; do not turn commit metadata into a finding merely because it differs.
- Distinguish `pass`, `blocked`, `recorded limitation`, `independently verified limitation`,
  `curator-accepted limitation`, and `not tested`. A roadmap label is only a recorded claim unless a
  separate acceptance decision is evidenced. Do not turn absence of a finding into stronger proof.
- Findings require an exact source (`file:line`, commit, GitHub URL, Release asset, or command), a
  concrete consequence, severity, and confidence. Preserve reviewer disagreement.

## Commands to establish live state

1. Resolve repository identity and confirm that the checkout is for
   `github.com/blind-striker/agent-skills-and-plugins`:

   ```powershell
   git remote -v
   git status --short --branch
   git log --oneline -10
   git submodule status
   ```

   If the working tree is dirty, record its scope separately and do not mix it into the committed
   review range:

   ```powershell
   git diff --stat
   git diff --name-status
   git diff --cached --stat
   git diff --cached --name-status
   git ls-files --others --exclude-standard
   ```

2. Search Memorizer by normalized remote. Load current documentation authority and public-release
   records only as leads after reading live Git. Relevant IDs may include:

   - repository bootstrap: `6367169a-e0f1-4f60-92a7-26f652865d56`;
   - documentation authority: `0cedb1af-2a6b-4696-98a3-5ff84f344dee`;
   - identity/history-scope correction: `d3ef8208-25e4-4223-b799-27dc1aa4925d`;
   - public-release closure lead: `5675ecb2-d619-4ceb-8633-9dae111989d7`;
   - public capability/cheatsheet lead: `7e994ffe-3cb0-4732-a51c-d43fbd4a6090`.

   Do not cite memory as release evidence. Correct or archive stale living memories after the review
   closes.

3. Read the bootstrap and current owners before evaluating the diff:

   - `AGENTS.md`, `CONTEXT.md`, root `README.md`;
   - `docs/ROADMAP.md` and `docs/cheatsheet.md`;
   - `curation/SCHEMA.md`, `curation/attribution.json`, and all `curation/*.yaml`;
   - `docs/architecture/transformation-and-emission.md`;
   - `docs/architecture/references-and-linking.md`;
   - `docs/architecture/distribution-and-installation.md`;
   - `docs/engineering/documentation.md`, `workflow.md`, and `quality-gates.md`;
   - `docs/adr/README.md` and ADR-0004 through ADR-0008;
   - `docs/research/README.md`, every retained research note,
     `experiments/harness-invocation/protocol.md`, and every retained experiment record;
   - `.github/workflows/validate.yml`, `package.json`, `.gitmodules`, `tools/build.ts`,
     `tools/validate.ts`, and the OpenCode installer sources.

4. Pin and inspect the public-release review range. The intended range is the last pre-release
   baseline through the current documentation follow-up:

   Define a redacting filter before printing historical diffs. It must hide email-shaped values
   without storing or reporting the original value:

   ```powershell
   function Protect-PrivateText {
       process {
           $_ -replace '(?i)[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}', '<redacted-email>'
       }
   }

   git rev-parse 181549c
   git rev-parse a31c0a2
   git rev-parse 8867fc4
   git rev-parse 53035ba
   git log --oneline 181549c..53035ba
   git diff --stat 181549c..53035ba
   git diff --name-status 181549c..53035ba
   ```

   Run all content-producing historical commands below only after the digest-pinned Gitleaks command
   in step 6 passes. If it reports a finding, stop and investigate through redacted scanner output
   instead of printing historical content.

   ```powershell
   git diff 181549c..53035ba -- . ':!plugins' ':!opencode' ':!dist' ':!.claude-plugin' |
     Protect-PrivateText
   ```

   Inspect all three segments as well as the net range. A claim added and removed inside the range is
   invisible in a net diff:

   ```powershell
   git diff 181549c..a31c0a2 -- README.md CONTEXT.md AGENTS.md docs curation experiments |
     Protect-PrivateText
   git diff a31c0a2..8867fc4 -- README.md CONTEXT.md AGENTS.md docs curation experiments |
     Protect-PrivateText
   git diff 8867fc4..53035ba -- README.md CONTEXT.md AGENTS.md docs curation experiments |
     Protect-PrivateText
   ```

   Inspect generated output separately rather than omitting it from the review:

   ```powershell
   git diff --stat 181549c..53035ba -- plugins opencode dist .claude-plugin docs/inventory.md docs/ledger.json
   git diff 181549c..53035ba -- plugins opencode dist .claude-plugin docs/inventory.md docs/ledger.json |
     Protect-PrivateText
   ```

5. Establish public GitHub state without changing it:

   ```powershell
   gh repo view Blind-Striker/agent-skills-and-plugins `
     --json visibility,isPrivate,url,defaultBranchRef,description
   gh release list --repo Blind-Striker/agent-skills-and-plugins
   gh release view installer-v0.2.0 --repo Blind-Striker/agent-skills-and-plugins `
     --json tagName,targetCommitish,isDraft,isPrerelease,publishedAt,assets,url
   gh api repos/Blind-Striker/agent-skills-and-plugins/private-vulnerability-reporting
   gh run view 32839114876 --repo Blind-Striker/agent-skills-and-plugins `
     --json conclusion,status,url,headSha,name,event,jobs
   ```

6. Run the repository gate from a fresh recursive checkout, not from an already generated working
   tree. Use a new temporary directory outside the repository:

   ```powershell
   $reviewBase = $env:AGENT_SKILLS_REVIEW_ROOT
   if ([string]::IsNullOrWhiteSpace($reviewBase) -or -not (Test-Path -LiteralPath $reviewBase -PathType Container)) {
       throw "set AGENT_SKILLS_REVIEW_ROOT to an existing external-lab directory"
   }

   function Test-PathWithin {
       param([string]$Candidate, [string]$Parent)
       $candidatePath = [IO.Path]::GetFullPath($Candidate).TrimEnd([IO.Path]::DirectorySeparatorChar)
       $parentPath = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar)
       return $candidatePath.Equals($parentPath, [StringComparison]::OrdinalIgnoreCase) -or
         $candidatePath.StartsWith($parentPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
   }

   $repoRoot = git rev-parse --show-toplevel
   $realProfile = [Environment]::GetFolderPath("UserProfile")
   if ((Test-PathWithin $reviewBase $repoRoot) -or (Test-PathWithin $reviewBase $realProfile)) {
       throw "AGENT_SKILLS_REVIEW_ROOT must be outside the repository and real user profile"
   }

   $reviewRoot = Join-Path $reviewBase ("checkout-" + [guid]::NewGuid())
   if (Test-Path -LiteralPath $reviewRoot) { throw "fresh review root already exists" }

   git clone --recurse-submodules https://github.com/Blind-Striker/agent-skills-and-plugins.git $reviewRoot
   git -C $reviewRoot checkout --detach 53035ba
   git -C $reviewRoot submodule update --init --recursive
   if ((git -C $reviewRoot rev-parse HEAD) -ne (git rev-parse 53035ba)) {
       throw "fresh checkout is not pinned to review commit 53035ba"
   }

   npm --prefix $reviewRoot ci
   npm --prefix $reviewRoot test
   npm --prefix $reviewRoot run typecheck
   npm --prefix $reviewRoot run lint
   npm --prefix $reviewRoot run format:check
   npm --prefix $reviewRoot run check:public-safety
   npm --prefix $reviewRoot run build
   npm --prefix $reviewRoot run inventory
   npm --prefix $reviewRoot run validate
   pwsh -NoProfile -File (Join-Path $reviewRoot "experiments/harness-invocation/selftest.ps1") -SkipLab
   git -C $reviewRoot diff --check
   git -C $reviewRoot status --short --branch

   npm --prefix $reviewRoot run build
   npm --prefix $reviewRoot run inventory
   git -C $reviewRoot diff --check
   git -C $reviewRoot status --short --branch
   ```

   The status after each generation round must remain clean. Review every warning instead of
   equating exit code zero with release correctness.

   Reproduce the full-history secret job locally with the same digest-pinned scanner. This scans the
   superproject history; submodule histories are separate upstream repositories:

   ```powershell
   docker run --rm -v "$($reviewRoot):/repo" `
     ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f `
     detect --source=/repo --no-banner --redact=100
   ```

   Define one environment-restoring helper for both installer transports. It prevents either test
   from reaching the real OpenCode profile:

   ```powershell
   function Invoke-IsolatedOpenCode {
       param([string]$Profile, [scriptblock]$Action)

       $names = @(
           "HOME", "USERPROFILE", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "TEMP", "TMP",
           "npm_config_cache", "npm_config_update_notifier", "npm_config_audit", "npm_config_fund",
           "OPENCODE_CONFIG_DIR", "OPENCODE_DISABLE_CLAUDE_CODE_SKILLS"
       )
       $saved = @{}
       foreach ($name in $names) {
           $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
       }

       try {
           New-Item -ItemType Directory -Path $Profile | Out-Null
           $env:HOME = $Profile
           $env:USERPROFILE = $Profile
           $env:XDG_CONFIG_HOME = Join-Path $Profile ".config"
           $env:XDG_DATA_HOME = Join-Path $Profile ".local/share"
           $env:TEMP = Join-Path $Profile ".tmp"
           $env:TMP = $env:TEMP
           $env:npm_config_cache = Join-Path $Profile ".npm-cache"
           $env:npm_config_update_notifier = "false"
           $env:npm_config_audit = "false"
           $env:npm_config_fund = "false"
           $env:OPENCODE_DISABLE_CLAUDE_CODE_SKILLS = "1"
           [Environment]::SetEnvironmentVariable("OPENCODE_CONFIG_DIR", $null, "Process")
           New-Item -ItemType Directory -Path $env:TEMP | Out-Null
           $isolatedWork = Join-Path $Profile "work"
           New-Item -ItemType Directory -Path $isolatedWork | Out-Null
           Push-Location $isolatedWork
           try { & $Action } finally { Pop-Location }
       } finally {
           foreach ($name in $names) {
               [Environment]::SetEnvironmentVariable($name, $saved[$name], "Process")
           }
       }
   }
   ```

   Exercise the clone transport independently:

   ```powershell
   $cloneProfile = Join-Path $reviewBase ("clone-profile-" + [guid]::NewGuid())
   Invoke-IsolatedOpenCode $cloneProfile {
       npm --prefix $reviewRoot run install:opencode -- install --all
       if (Test-Path -LiteralPath (Join-Path $env:XDG_CONFIG_HOME "opencode")) {
           throw "clone transport Plan wrote the Destination"
       }
       npm --prefix $reviewRoot run install:opencode -- install --all --yes
       npm --prefix $reviewRoot run install:opencode -- status
   }
   ```

7. Independently verify the Release asset and the packaged installer in an isolated HOME/XDG root.
   The repository currently records the following identity as a claim to verify, not an expected
   answer to copy:

   | Field | Claimed value |
   |---|---|
   | Tag | `installer-v0.2.0` |
   | Target commit | `8867fc4077494a4bedb32f9f75ea251acbf8b81a` |
   | Asset | `deniz-agent-skills-0.2.0.tgz` |
   | Size | `902158` bytes |
   | SHA-256 | `f266ab796a5cb858a0e9fbad9c98c64959f20733ce1c84fc6b789e23585efe24` |

   Download, verify, and inspect the exact Package:

   ```powershell
   $releaseRoot = Join-Path $reviewBase ("release-" + [guid]::NewGuid())
   if (Test-Path -LiteralPath $releaseRoot) { throw "fresh release root already exists" }
   New-Item -ItemType Directory -Path $releaseRoot | Out-Null

   gh release download installer-v0.2.0 --repo Blind-Striker/agent-skills-and-plugins `
     --pattern "deniz-agent-skills-0.2.0.tgz" --dir $releaseRoot
   $package = Join-Path $releaseRoot "deniz-agent-skills-0.2.0.tgz"
   $expected = "f266ab796a5cb858a0e9fbad9c98c64959f20733ce1c84fc6b789e23585efe24"
   $actual = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
   if ($actual -ne $expected) { throw "downloaded Package SHA-256 mismatch" }
   if ((Get-Item -LiteralPath $package).Length -ne 902158) { throw "downloaded Package size mismatch" }

   $release = gh release view installer-v0.2.0 --repo Blind-Striker/agent-skills-and-plugins `
     --json tagName,targetCommitish,assets,url | ConvertFrom-Json
   if ($release.targetCommitish -ne "8867fc4077494a4bedb32f9f75ea251acbf8b81a") {
       throw "Release target mismatch"
   }
   $asset = @($release.assets | Where-Object name -eq "deniz-agent-skills-0.2.0.tgz")
   if ($asset.Count -ne 1) { throw "Release must contain exactly one matching Package asset" }
   if ($asset[0].digest -ne "sha256:$expected") { throw "GitHub asset digest mismatch" }
   if ($asset[0].size -ne 902158) { throw "GitHub asset size mismatch" }

   $embeddedRoot = Join-Path $releaseRoot "embedded"
   $taggedRoot = Join-Path $releaseRoot "tagged"
   New-Item -ItemType Directory -Path $embeddedRoot, $taggedRoot | Out-Null
   tar -xf $package -C $embeddedRoot package/README.md
   git -C $reviewRoot archive --format=tar -o (Join-Path $releaseRoot "tagged-readme.tar") 8867fc4 README.md
   tar -xf (Join-Path $releaseRoot "tagged-readme.tar") -C $taggedRoot
   if ((Get-FileHash (Join-Path $embeddedRoot "package/README.md")).Hash -ne
       (Get-FileHash (Join-Path $taggedRoot "README.md")).Hash) {
       throw "Package README does not match the tagged source"
   }
   git -C $reviewRoot diff 8867fc4..53035ba -- README.md | Protect-PrivateText
   ```

   The Package README comes from the tagged source, while the current repository README contains the
   post-publication exact digest recipe. Inspect and classify that two-commit relationship rather than
   treating either README as implicitly authoritative for the other.

   Exercise the Package transport independently:

   ```powershell
   $packageProfile = Join-Path $reviewBase ("package-profile-" + [guid]::NewGuid())
   Invoke-IsolatedOpenCode $packageProfile {
       npm exec --yes --package $package -- deniz-skills install --all
       if (Test-Path -LiteralPath (Join-Path $env:XDG_CONFIG_HOME "opencode")) {
           throw "Package transport Plan wrote the Destination"
       }
       npm exec --yes --package $package -- deniz-skills install --all --yes
       npm exec --yes --package $package -- deniz-skills status
   }
   ```

   Never point either transport at the real OpenCode profile.

8. Treat Claude Code consumption separately. Inspect the public marketplace and generated Plugin
   metadata. Do not install or enable Plugins in a real profile merely to strengthen the report. If
   no isolated harness runtime is run, mark model discovery/invocation as `not tested` rather than
   inferring it from compiler output.

## Independent review dimensions

### 1. Scope and public surface

Use the root [README](../../../README.md) as the human onboarding/consumption owner and
[SECURITY.md](../../../SECURITY.md) as the reporting route. Check that the public README presents a personal, opinionated collection rather than a supported
general product. Verify the no-SLA boundary, repository description, public contact, security route,
clone instructions, both installation transports, source credits, and absence of stale private-only
instructions. Check that `installer-v0.1.0` is historical and not presented as current.

### 2. Source-to-distribution licensing

Use [curation attribution](../../../curation/attribution.json), the root
[third-party notices](../../../THIRD_PARTY_NOTICES.md),
[transformation and emission](../../architecture/transformation-and-emission.md), and
[distribution and installation](../../architecture/distribution-and-installation.md) as current
owners. Build a source-to-distribution table from live curation rather than reusing a prior table. For every
primary source, merge source, and original-skill estate, establish:

- source license and copyright holder;
- Modules that consume it;
- whether copied or transformed material enters each Claude Plugin, OpenCode Bundle, Package, and
  installed Native tree;
- where the repository license, source-specific notice, and exact upstream license bytes travel;
- whether excluded-only sources are absent from a Module's notice set;
- whether `licenseFile` confinement and ordinary-file preflight fail before output deletion;
- whether Package dependencies add another distributed license surface.

Compare generated `third_party/<source>/LICENSE` bytes with the pinned upstream license files. Verify
that Bundle-root distribution metadata is hashed and packaged but deliberately does not become
OpenCode Destination Ownership. Report technical evidence without declaring legal compliance.

### 3. Privacy, secrets, and GitHub hardening

Use [engineering quality gates](../../engineering/quality-gates.md) and the always-on safety rules in
[AGENTS.md](../../../AGENTS.md) as current owners. Review current authored files, generated output, Package contents, and superproject history with
redacted tools. Verify the scope of `npm run check:public-safety`, the sole synthetic machine-path
fixture exception, full-history Gitleaks behavior, pinned Action/container identities, least-privilege
permissions, and private vulnerability reporting. Separate vendored upstream fixtures from material
this repository copies into public distributions.

### 4. Curation compiler capabilities

Use [curation manifest authoring](../../../curation/SCHEMA.md) and
[transformation and emission](../../architecture/transformation-and-emission.md) as the current
owners. Check each public capability claim in README against those owners, implementation, and tests:

- scanner-visible inventory and explicit take/merge/transform/exclude answers;
- rename, omit, frontmatter, invocation, and supported shape conversion;
- native skill/command/agent emission and the direction of supported conversions;
- `auto`, `manual`, and `both` mapping in both harnesses;
- parked body behavior for bundled manual OpenCode commands;
- patch, overlay, merge-source stamping, drift, blessing, and fail-before-delete preflight;
- independent per-harness reference localization, audience reachability, path checks, dependency
  symmetry, and ledger projection;
- sync impact reporting;
- Bundle manifest, license, and installer handoff.

Look for overclaims, omitted material limits, and mechanics duplicated into the wrong documentation
owner.

### 5. Reference, ledger, and generated-estate review

Use [references and linking](../../architecture/references-and-linking.md) as the semantics owner. Run
the [reference-audit playbook](../reference-audit-playbook.md) four times, once for each Module: `deniz-process`,
`deniz-dotnet-general`, `deniz-dotnet-akka`, and `deniz-dotnet-aspire`. Each run audits that Module in
both emitted harness trees. Inspect the complete ledger. Verify that new license files do not affect
reference scans, artifact discovery, invocation posture, or command parking. Review the two currently
documented validation warnings and decide from live source whether each remains expected.

### 6. Installer and transport review

Use [distribution and installation](../../architecture/distribution-and-installation.md) as the
mechanics owner. Review checkout and Package transports as distinct supported paths. Verify Package files exactly,
compiled-installer independence from authoring dependencies, Bundle verification, distribution-only
metadata filtering, module digest advancement, zero-write Plan, explicit Apply, Ownership,
collisions, local modification, State drift, locking, rollback, and Recovery. Do not claim arbitrary
Module subset dependency closure; test or inspect the limitation named in distribution canon.

### 7. Lossless documentation and unique-information audit

This dimension is mandatory even when code and CI are green.

Compare the pre-release and current documentation trees, especially:

```powershell
git show 181549c:docs/ROADMAP.md | Protect-PrivateText
git show a31c0a2:docs/ROADMAP.md | Protect-PrivateText
git show a31c0a2:docs/agents/handover-prompts/s1-public-release-preparation.prompt.md |
  Protect-PrivateText
git show 53035ba:docs/ROADMAP.md | Protect-PrivateText
git diff 181549c..a31c0a2 -- README.md CONTEXT.md AGENTS.md docs curation experiments |
  Protect-PrivateText
git diff a31c0a2..8867fc4 -- README.md CONTEXT.md AGENTS.md docs curation experiments |
  Protect-PrivateText
git diff 8867fc4..53035ba -- README.md CONTEXT.md AGENTS.md docs curation experiments |
  Protect-PrivateText
```

The `a31c0a2` roadmap snapshot is mandatory: it contains intermediate operational claims that are
absent from both the baseline and the net range. Include claims added and removed within the range in
the table instead of auditing only start versus finish.

Produce a claim-loss table with one row for every substantive removed or shortened claim:

| Removed/changed claim | Former owner | Current destination | Classification | Evidence |
|---|---|---|---|---|
| exact claim, not a topic label | file and line/range | current file/symbol or none | preserved, intentionally obsolete, duplicated, or lost | why |

Apply these rules:

- Git history alone is not an acceptable destination for useful current canon, rationale, evidence,
  or unfinished operational work.
- Completed recuration chronology may be removed from the roadmap only when item intent remains in
  manifests/ledger and bounded findings remain in research.
- Volatile personal installation status may be intentionally removed, but the report must say why it
  has no durable public value.
- Preserve dated research and append-only experiment records even when current mechanics supersede
  them; verify they are clearly marked as evidence rather than policy.
- Verify that the restored `original_skills` declaration shape, validation obligations, and
  acceptance matrix remain accessible.
- Verify that the curation sanity panel packet, axes, evidence format, advisory-only boundary, and
  trigger remain accessible.
- Verify that the invocation ADR candidate retains the body-fact versus description distinction,
  tentative rule, evidence links, and admission trigger.
- Verify that the optional writing-style warning and deferred `expects` trigger remain visible.
- Check whether any unique public-release command, proof boundary, decision, or operational follow-up
  disappeared when the old handover was consumed.
- Check README and cheatsheet attribution: upstream methods must not be presented as original work,
  and this repository's transformations must not be hidden either.

Do not preserve prose merely because it existed. A claim can be ruled obsolete, but the ruling needs
a reason and evidence.

### 8. Curation selection accuracy

Use the Process [curation manifest](../../../curation/deniz-process.yaml), generated
[ledger](../../ledger.json), and [cheatsheet](../../cheatsheet.md) as the intent, projection, and
human-routing owners. Recalculate the current Process source composition from live manifests and output. Verify direct,
merge-only, and excluded Matt Pocock items; all Superpowers items; ASD-STE100; invocation counts; and
the owned `ask-deniz` router. Check that the cheatsheet routes and upstream credits match current
curation and do not promise files or behavior that delegated skills create only conditionally.

### 9. Proof boundaries

Explicitly test or classify the repository's claimed limits. Do not preserve or accept a limit merely
because it appears in this list. Resolve current wording through the proof-boundary/current-limit
sections in [transformation and emission](../../architecture/transformation-and-emission.md),
[references and linking](../../architecture/references-and-linking.md), and
[distribution and installation](../../architecture/distribution-and-installation.md):

- upstream-owned Aspire CLI, TypeScript, testing, and package examples;
- model selection and instruction following;
- Claude runtime discovery when no isolated invocation was run;
- arbitrary-subset Module dependency safety;
- global parked-body permission behavior;
- mutable GitHub Release assets despite digest verification;
- scanner and linker limits listed in current architecture and roadmap.

### 10. Roadmap ordering

Use the current [roadmap](../../ROADMAP.md) as the operational owner. Evaluate every current `Next Up` item against its stated trigger and current public impact. Do not
assume numeric order is correct. For each candidate, report whether its trigger is present now, what
public/user risk it addresses, dependencies, and smallest acceptable first slice. Recommend one next
initiative only after this comparison.

## Required output

Report in this order:

1. **Findings**, ordered by severity, with exact evidence and concrete consequence.
2. **Open questions and assumptions**, including anything that requires curator or legal judgment.
3. **Release-readiness table** with rows for scope, licensing/notices, current-tree privacy,
   full-history secrets, owner/contact, public docs/support, GitHub permissions/security reporting,
   compiler claims, reference/linker state, generated output, clone transport, Package transport,
   Release identity, OpenCode isolated consumption, Claude consumption, and runtime limits. For every
   limit, state separately whether it is recorded, independently verified, curator-accepted, or not
   tested; do not use one generic `accepted limitation` status.
4. **Documentation claim-loss table** using the schema above.
5. **Roadmap next-step comparison** and one evidence-backed recommendation.
6. **Verification appendix** listing commands actually run, skipped commands, warnings, and links to
   GitHub evidence. Do not paste secret-bearing or machine-specific raw output.

If no actionable finding remains, say so directly and list residual risks and untested behavior. A
clean review does not authorize silent edits, Release replacement, installation changes, or new
curation decisions.

## Closeout

After the curator reviews the report:

- make only approved corrections;
- update the canonical owner of each changed claim;
- update `docs/ROADMAP.md` only for real current state or open work;
- store bounded review evidence in an appropriate dated research/record only if it has durable value;
- correct or archive stale memories;
- remove only the unique disposable checkout, download, and isolated profile directories created for
  this review after their evidence is no longer needed; never recursively delete the shared external
  review parent;
- delete this handoff and remove its relay from `docs/agents/README.md` when the review and approved
  follow-up are complete.
