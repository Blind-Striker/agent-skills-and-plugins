# Reference-audit playbook

Date: 2026-08-19

A repeatable sweep over **emitted output** for reference problems the deterministic gates cannot
decide. [References and linking](../architecture/references-and-linking.md) owns the current
namespaced-fact, declared-edge, path, and candidate semantics; [ADR-0008](../adr/0008-references-are-symbols.md)
records their rationale and trade-off. This playbook surfaces bare-name candidates and
curation-intent contradictions for the curator. It never authorizes an agent to change curation on
its own ([ADR-0007](../adr/0007-control-beats-fidelity.md)).

## When to run

Run after a curation wave, before declaring a module closed, and after `npm run sync` moves an
upstream pin. Start with fresh generated state:

```powershell
npm run inventory
npm run build
npm run validate
```

Stop if any command fails. The inventory, ledger, and emitted trees must describe the same build.

## Derive names and scope

Names are output identities, not assumed source-directory basenames. The build resolves an item in
this order: item-level `name:`, scanner/frontmatter name, then source basename. The ledger already
contains that result for taken manifest items; own skills need the emitted frontmatter name added.

Set the module under review, then run the derivation from the repository root:

```powershell
$plugin = "deniz-dotnet-general"

$sets = @'
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { basename, join } from "node:path";
import { parse } from "yaml";
import { parseDoc } from "./tools/lib/frontmatter.ts";

const plugin = process.argv[2];
const ledger = JSON.parse(readFileSync("docs/ledger.json", "utf8"));
const rows = readFileSync("docs/inventory.md", "utf8")
  .split(/\r?\n/)
  .map((line) => line.match(/^\| (.*?) \| (?:skill|command|agent) \| (.*?) \|.*\| `([^`]+)` \| \d+ \| \d+ \|$/))
  .filter(Boolean)
  .map((match) => ({ name: match[1], curated: match[2], source: match[3] }));
const inventoryName = new Map(rows.map((row) => [row.source, row.name]));

const manifestFile = readdirSync("curation")
  .filter((file) => file.endsWith(".yaml"))
  .find((file) => parse(readFileSync(join("curation", file), "utf8")).plugin.name === plugin);
if (!manifestFile) throw new Error(`no manifest has plugin.name ${plugin}`);
const manifest = parse(readFileSync(join("curation", manifestFile), "utf8"));

const taken = new Set();
const paths = new Set([`plugins/${plugin}`]);
const opencodeRoot = `opencode/${plugin}`;
for (const [key, entry] of Object.entries(ledger)) {
  const [entryPlugin, , ...nameParts] = key.split("/");
  if (entryPlugin !== plugin) continue;
  const name = nameParts.join("/");
  taken.add(name);
  for (const artifact of entry.opencode.artifacts) {
    const path = artifact === "skill"
      ? `${opencodeRoot}/skills/${name}`
      : `${opencodeRoot}/${artifact}s/${name}.md`;
    if (existsSync(path)) paths.add(path);
  }
  const parkedPath = `${opencodeRoot}/skills/${name}`;
  if (entry.opencode.parked.length && existsSync(parkedPath)) {
    paths.add(parkedPath);
  }
}

const ownRoot = join("skills", plugin);
if (existsSync(ownRoot)) {
  for (const dir of readdirSync(ownRoot, { withFileTypes: true }).filter((entry) => entry.isDirectory())) {
    const emitted = join("plugins", plugin, "skills", dir.name, "SKILL.md");
    if (!existsSync(emitted)) continue;
    const name = String(parseDoc(readFileSync(emitted, "utf8")).frontmatter.name ?? dir.name);
    taken.add(name);
    const opencodePath = `${opencodeRoot}/skills/${name}`;
    if (existsSync(opencodePath)) paths.add(opencodePath);
  }
}

const excluded = new Set(
  manifest.items
    .filter((item) => item.exclude === true)
    .map((item) => item.name ?? inventoryName.get(item.source) ?? basename(item.source, ".md")),
);
const neverCurated = new Set(rows.filter((row) => row.curated === "—").map((row) => row.name));

// A live output identity wins. Another source with the same name may be excluded or absent from a
// manifest, but references to the live identity are not broken for that reason.
for (const name of taken) {
  excluded.delete(name);
  neverCurated.delete(name);
}
// An explicitly excluded identity is classified by that stronger fact, not also as never curated.
for (const name of excluded) neverCurated.delete(name);

console.log(JSON.stringify({
  taken: [...taken].sort(),
  excluded: [...excluded].sort(),
  neverCurated: [...neverCurated].sort(),
  paths: [...paths].sort(),
}, null, 2));
'@ | node --input-type=module - $plugin | ConvertFrom-Json
```

Inspect `$sets` before scanning. The sets must be disjoint, every path must exist, an explicitly
renamed item must appear under its output name, and an own skill must appear under its emitted
frontmatter name. Treat a failure here as a derivation defect, not permission to type a list by hand.

## Build a candidate pattern

`rg -w` is not valid for hyphenated artifact names. Escape every name and use the same token boundary
as `candidateHits` in `tools/lib/refs.ts`: a candidate cannot touch `[a-z0-9-:]` on either side.
The colon exclusion prevents a namespaced fact from being reported again as a bare candidate.

```powershell
function New-CandidatePattern([string[]] $Names) {
    if (-not $Names.Count) { return $null }
    $alternation = ($Names | ForEach-Object { [regex]::Escape($_) }) -join "|"
    return "(^|[^a-z0-9-:])(?:$alternation)(?=$|[^a-z0-9-:])"
}

function Find-Candidates([string[]] $Names, [string[]] $Paths) {
    $pattern = New-CandidatePattern $Names
    if (-not $pattern) { return @() }
    & rg -n --pcre2 --glob "**/*.md" -- $pattern @Paths
    if ($LASTEXITCODE -gt 1) { throw "rg failed with exit code $LASTEXITCODE" }
}
```

This is candidate generation only. `rg` returning exit code 1 means a clean empty scan.

## Scan 1 — Excluded, but still referenced

```powershell
Find-Candidates $sets.excluded $sets.paths
```

Kept output that treats an excluded name as an artifact may retain a redirect the curation wave cut.

| Hit shape | Severity | Recommendation ladder |
|---|---|---|
| Description redirect such as `DO NOT USE ... use X` | High: descriptions enter the system prompt | Rewire to a kept target or let the absorbing item claim the job |
| Body handoff to X | Medium: active after invocation | Patch when the body is already owned; otherwise report for a curator decision |
| Historical, illustrative, or table mention | Low or false positive | Usually retain and record why it is not a live handoff |

## Scan 2 — Referenced, but never curated

```powershell
Find-Candidates $sets.neverCurated $sets.paths
```

A true hit means upstream assumes an artifact this marketplace never classified. The curator may
curate it, redirect to a kept equivalent, or record a deliberate boundary. Namespaced forms already
reach `validate`; this scan is for bare, slash, and path-like forms.

## Scan 3 — Taken, but unguarded

```powershell
Find-Candidates $sets.taken $sets.paths
```

Ignore a source artifact matching its own name unless the text actually invokes itself. For each
remaining true hit, answer:

1. **Audience coherence.** A `/name` pointer to an `auto` target advertises a command that does not exist. Model-invocation prose pointing to a `manual` target asks the model to load an undiscoverable skill.
2. **Load bearing.** Would the source malfunction if the target were renamed or cut? A trigger whose body says only to run X is load-bearing; a see-also entry usually is not.
3. **Guard path.** Body ownership is separate from reference authority. A patch or overlay does not promote a bare candidate merely by contact; preserve the corpus's convention unless the curator deliberately authors a namespaced fact and matching `depends_on` in the same change. If the body is untouched, report it; do not patch solely to add a guard. The accumulation trigger for any future `expects` mechanism lives in `docs/ROADMAP.md`.

## Classify candidates

Count a hit as an artifact reference when the sentence treats the matched name as one. Strong
signals are a backticked name, slash or path spelling, adjacency to `skill`, `command`, or `agent`,
or a governing verb such as use, load, invoke, run, route, dispatch, or hand off.

Drop a candidate when context shows a product word, CLI subcommand, code/API identifier, prose that
only contains the name as part of a larger concept, or framework lookup data. In particular,
MSTest/TUnit trait rows under `test-analysis-extensions/extensions/*.md` are data, not references to
curated artifacts. Record the drop reason so a later audit does not repeatedly relitigate it.

The exact-address suppression list `NON_SYMBOL_REF_ADDRESSES` in `tools/validate.ts` is useful
precedent for namespaced-looking product syntax, but it is not a substitute for reading bare-name
context. When context is ambiguous, report the candidate as ambiguous instead of silently promoting
it to a curation finding.

## Report format

Use one table per scan:

```text
file:line | referenced output name | class | severity | disposition/recommended rung
```

End each scan with candidate count, retained finding count, dropped false-positive count, and an
explicit `clean` when no finding remains. Include a short false-positive appendix with each dropped
candidate and reason. Recommendations name the cheapest honest mechanism from
[curation/SCHEMA.md](../../curation/SCHEMA.md); the curator decides every curation change.
