import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./lib/manifest.ts";
import { ledgerCandidateNames, renameDestination, type SyncIO, syncReport, treeExists } from "./sync.ts";

// Reading upstream at both pins is the only impure thing the report needs, so it arrives as a
// parameter: these tests hand it fixtures where the CLI hands it `git show` and `git ls-tree`.
const NO_IO: SyncIO = { readFile: () => null, exists: () => true, ledgerNames: [] };

/** One readable file at both pins; everything else unreadable, as an added or deleted path is. */
function oneFile(rel: string, atOld: string, atNew: string): SyncIO["readFile"] {
  return (rev, r) => {
    if (r !== rel) {
      return null;
    }
    return rev === "old" ? atOld : atNew;
  };
}

/**
 * An IO whose tree answers differ between the pins. `gone` is the delete/rename case — present at
 * the old pin, absent at the new one — and `neverThere` is absent at both, which is the shape a
 * manifest entry for another submodule's path has.
 */
function treeIO(opts: {
  gone?: string[];
  neverThere?: string[];
  moved?: Record<string, string>;
  readFile?: SyncIO["readFile"];
  ledgerNames?: string[];
}): SyncIO {
  const gone = new Set(opts.gone ?? []);
  const never = new Set(opts.neverThere ?? []);
  return {
    readFile: opts.readFile ?? (() => null),
    exists: (rev, rel) => {
      if (never.has(rel)) {
        return false;
      }
      return rev === "old" ? true : !gone.has(rel);
    },
    movedTo: (rel) => opts.moved?.[rel] ?? null,
    ledgerNames: opts.ledgerNames ?? [],
  };
}

const manifests: CurationManifest[] = [
  {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [
      { source: "sp/skills/alpha" },
      { source: "sp/skills/beta", body: "overlay" },
      { source: "sp/skills/gamma", body: "patch" },
      { source: "sp/skills/gone", exclude: true },
      { source: "other/skills/x" },
      // A merge source in a different submodule than the primary — the case a primary-source guard
      // hides, and the one the real manifests actually contain.
      { source: "sp/skills/host", body: "overlay", merged_from: [{ source: "mp/skills/beta" }] },
      { source: "sp/skills/overridden", frontmatter: { description: "ours" } },
      { source: "sp/skills/owned", body: "overlay", frontmatter: { description: "ours" } },
    ],
  },
];

// `git ls-tree -r` lists blobs, never the directories holding them, so a curated skill address only
// resolves by prefix. The boundary matters: `skills/alpha` must not be satisfied by `skills/alphabet`.
test("tree existence resolves a directory by prefix and respects the segment boundary", () => {
  const exists = treeExists(["skills/alpha/SKILL.md", "skills/alphabet/SKILL.md", "agents/solo.agent.md"]);
  assert.ok(exists("skills/alpha"));
  assert.ok(exists("skills/alphabet"));
  assert.ok(exists("agents/solo.agent.md"));
  assert.ok(!exists("skills/alph"));
  assert.ok(!exists("skills/missing"));
});

// Every bare command or agent source IS a file, so the renamed path equals the curated address and
// there is no directory tail to strip. Matching only by prefix reported those moves as an
// unexplained SOURCE GONE — the exact half of the delete/rename defect this machinery exists to fix.
test("a renamed file-shaped source resolves to its destination", () => {
  const dest = renameDestination([
    ["plugins/dotnet-test/agents/code-testing-builder.agent.md", "plugins/dotnet-test/agents/builder.agent.md"],
  ]);
  assert.equal(
    dest("plugins/dotnet-test/agents/code-testing-builder.agent.md"),
    "plugins/dotnet-test/agents/builder.agent.md",
  );
});

test("a renamed directory-shaped source collapses back to the directory", () => {
  const dest = renameDestination([
    ["skills/in-progress/wizard/SKILL.md", "skills/engineering/wizard/SKILL.md"],
    ["skills/in-progress/wizard/template.sh", "skills/engineering/wizard/template.sh"],
  ]);
  assert.equal(dest("skills/in-progress/wizard"), "skills/engineering/wizard");
});

test("a rename whose tail did not survive still names the destination file", () => {
  const dest = renameDestination([["skills/old/SKILL.md", "skills/new/README.md"]]);
  assert.equal(dest("skills/old"), "skills/new/README.md");
  assert.equal(dest("skills/untouched"), null);
});

test("reports only changed curated items with overlay flag", () => {
  const lines = syncReport(
    "sp",
    ["skills/alpha/SKILL.md", "skills/beta/SKILL.md", "skills/unrelated/SKILL.md"],
    manifests,
    NO_IO,
  );
  assert.equal(lines.length, 2);
  const [alpha, beta] = lines;
  assert.ok(alpha);
  assert.ok(beta);
  assert.match(alpha, /alpha/);
  assert.match(alpha, /auto-updated/);
  assert.match(beta, /beta/);
  assert.match(beta, /OVERLAY/);
});

// "auto-updated on next build" is a lie for a patch item: upstream moving into the patched region
// stops the build instead, so the default tag must not be what sync prints for one.
test("a patch item is reported as neither auto-updated nor an overlay", () => {
  const lines = syncReport("sp", ["skills/gamma/SKILL.md"], manifests, NO_IO);
  assert.equal(lines.length, 1);
  const only = lines[0];
  assert.ok(only);
  assert.match(only, /PATCH/);
  assert.doesNotMatch(only, /auto-updated/);
});

test("no changes yields empty report", () => {
  assert.deepEqual(syncReport("sp", [], manifests, NO_IO), []);
});

test("excluded item reports no action", () => {
  const lines = syncReport("sp", ["skills/gone/SKILL.md"], manifests, NO_IO);
  assert.equal(lines.length, 1);
  const only = lines[0];
  assert.ok(only);
  assert.match(only, /excluded — no action/);
});

// An item that states no invocation lets upstream's own frontmatter through, so a posture key
// flipping upstream silently changes how the shipped skill fires. That is the one drift a path
// list cannot show.
test("a passthrough item's invocation flip is reported as posture drift", () => {
  const lines = syncReport("sp", ["skills/alpha/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/alpha/SKILL.md",
      "---\nname: alpha\ndescription: a\n---\nB.\n",
      "---\nname: alpha\ndescription: a\ndisable-model-invocation: true\n---\nB.\n",
    ),
  });
  assert.ok(
    lines.some(
      (l) => l.includes("POSTURE") && l.includes("disable-model-invocation") && l.includes("undefined -> true"),
    ),
  );
});

test("a pin move touching a merge source tags the merged item even across submodules", () => {
  const lines = syncReport("mp", ["skills/beta/SKILL.md"], manifests, NO_IO);
  assert.ok(
    lines.some((l) => l.includes("MERGE SOURCE") && l.includes("sp/skills/host") && l.includes("mp/skills/beta")),
  );
});

test("candidate edges that appeared or vanished in a changed body are reported", () => {
  const lines = syncReport("sp", ["skills/alpha/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/alpha/SKILL.md",
      "---\nname: alpha\ndescription: a\n---\nRun /tdd here.\n",
      "---\nname: alpha\ndescription: a\n---\nSee writing-plans instead.\n",
    ),
    ledgerNames: ["tdd", "writing-plans"],
  });
  assert.ok(lines.some((l) => l.includes("CANDIDATE EDGES") && l.includes("+writing-plans") && l.includes("-tdd")));
});

// Ledger keys are `<plugin>/<kind>/<name>`. Reading segment 1 yields the KIND, which made the
// candidate universe the two words "skill" and "agent" — so the report could only ever say a body
// had gained or lost the word "skill", and never named a real output.
test("the candidate universe is output names, not artifact kinds", () => {
  const names = ledgerCandidateNames({
    "deniz-process/skill/writing-plans": {},
    "deniz-process/command/to-spec": {},
    "deniz-dotnet-akka/agent/akka-net-specialist": {},
    "deniz-dotnet-general/skill/run-tests": {},
  });
  assert.deepEqual(names, ["akka-net-specialist", "run-tests", "to-spec", "writing-plans"]);
  assert.ok(!names.includes("skill"));
  assert.ok(!names.includes("agent"));
});

test("the candidate universe de-duplicates a name two Modules both emit", () => {
  const names = ledgerCandidateNames({ "a/skill/shared": {}, "b/command/shared": {} });
  assert.deepEqual(names, ["shared"]);
});

// A deleted source IS in the changed-path list, so the old report matched it and tagged it
// "auto-updated on next build" — the single most misleading thing it could say about a path whose
// next build stops in `collectProblems` before anything is emitted.
test("a deleted source is reported as a build stop, never as auto-updated", () => {
  const lines = syncReport("sp", ["skills/alpha/SKILL.md"], manifests, treeIO({ gone: ["skills/alpha"] }));
  const alpha = lines.find((l) => l.includes("sp/skills/alpha"));
  assert.ok(alpha);
  assert.match(alpha, /SOURCE GONE/);
  assert.match(alpha, /source not found in external\//);
  assert.doesNotMatch(alpha, /auto-updated/);
});

// `git diff --name-only` prints only the DESTINATION of a rename it detected, so the curated old
// path is absent from the changed list entirely. Matching on that list alone made the item silent
// here and left the build to fail with no warning from this report.
test("a renamed source absent from the changed list is still reported, with its destination", () => {
  const lines = syncReport(
    "sp",
    ["skills/renamed-elsewhere/SKILL.md"],
    manifests,
    treeIO({ gone: ["skills/alpha"], moved: { "skills/alpha": "skills/renamed-elsewhere" } }),
  );
  const alpha = lines.find((l) => l.includes("sp/skills/alpha"));
  assert.ok(alpha);
  assert.match(alpha, /SOURCE GONE/);
  assert.match(alpha, /upstream moved it to skills\/renamed-elsewhere/);
});

// An excluded entry resolves to nothing, so its source vanishing is not a build stop — but it does
// leave the manifest recording an address upstream no longer has. Silence and "build stops" are
// both wrong for it.
test("a deleted source under an excluded item is a stale record, not a build stop", () => {
  const lines = syncReport("sp", [], manifests, treeIO({ gone: ["skills/gone"] }));
  const gone = lines.find((l) => l.includes("sp/skills/gone"));
  assert.ok(gone);
  assert.match(gone, /SOURCE GONE/);
  assert.match(gone, /excluded/);
  assert.doesNotMatch(gone, /source not found in external\//);
});

// `--bless` stamps a merge input by hashing the upstream file. When that file is gone there is
// nothing to hash, so handing over the re-bless command hands over one that cannot succeed.
test("a deleted merge source says it cannot be re-blessed", () => {
  const lines = syncReport("mp", ["skills/beta/SKILL.md"], manifests, treeIO({ gone: ["skills/beta"] }));
  const merge = lines.find((l) => l.includes("mp/skills/beta"));
  assert.ok(merge);
  assert.match(merge, /MERGE SOURCE GONE/);
  assert.doesNotMatch(merge, /re-bless: git/);
});

// A manifest entry for a path neither pin has is not news about this pin move.
test("a source absent at both pins is silent", () => {
  const lines = syncReport("sp", [], manifests, treeIO({ neverThere: ["skills/alpha"] }));
  assert.deepEqual(lines, []);
});

// A `frontmatter:` override merges in after body assembly and carries no upstream stamp, so an
// upstream rewrite leaves it describing a body that no longer exists — and no build check can see
// it. This report is the only place it can surface.
test("an upstream change under a frontmatter override is reported", () => {
  const lines = syncReport("sp", ["skills/overridden/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/overridden/SKILL.md",
      "---\nname: overridden\ndescription: narrow job\n---\nB.\n",
      "---\nname: overridden\ndescription: a much broader job now\n---\nB.\n",
    ),
  });
  const drift = lines.find((l) => l.includes("OVERRIDE"));
  assert.ok(drift);
  assert.match(drift, /description/);
  assert.match(drift, /upstream moved it too/);
});

// The case that motivated the check, and the one a key-comparison misses: upstream restructures the
// body end to end and leaves the description byte-identical. Every overridden key still compares
// equal, while the override now describes one branch of a body that grew three.
test("an override is reported when only the body moved beneath it", () => {
  const lines = syncReport("sp", ["skills/overridden/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/overridden/SKILL.md",
      "---\nname: overridden\ndescription: same\n---\nOne path.\n",
      "---\nname: overridden\ndescription: same\n---\nThree paths now: spike, bounded, architectural.\n",
    ),
  });
  const drift = lines.find((l) => l.includes("OVERRIDE"));
  assert.ok(drift);
  assert.match(drift, /the body moved underneath it instead/);
});

// An overlay shadows upstream until it is re-blessed, so the SHIPPED body did not move and the
// OVERLAY tag already asked for that review. Claiming the body moved under the override would be the
// same false statement the excluded-item guard removes.
test("an owned body is exempt from the override line", () => {
  const lines = syncReport("sp", ["skills/owned/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/owned/SKILL.md",
      "---\nname: owned\ndescription: same\n---\nupstream one.\n",
      "---\nname: owned\ndescription: same\n---\nupstream rewrote this.\n",
    ),
  });
  assert.ok(lines.some((l) => l.includes("OVERLAY")));
  assert.ok(!lines.some((l) => l.includes("OVERRIDE")));
});

// Parsing runs for every changed item now, not only passthrough ones. One upstream file with broken
// YAML used to throw out of the whole report — and it would do it after the pin had already moved.
test("frontmatter that no longer parses is reported, not thrown", () => {
  let lines: string[] = [];
  assert.doesNotThrow(() => {
    lines = syncReport("sp", ["skills/alpha/SKILL.md", "skills/beta/SKILL.md"], manifests, {
      ...NO_IO,
      readFile: oneFile(
        "skills/alpha/SKILL.md",
        "---\nname: alpha\ndescription: fine\n---\nB.\n",
        "---\nname: alpha\ndescription: broken: unquoted: colons\n---\nB.\n",
      ),
    });
  });
  assert.ok(lines.some((l) => l.includes("FRONTMATTER NO LONGER PARSES")));
  // The report survives: the item after the broken one is still reported.
  assert.ok(lines.some((l) => l.includes("sp/skills/beta")));
});

test("an item with no override gets no override line", () => {
  const lines = syncReport("sp", ["skills/alpha/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/alpha/SKILL.md",
      "---\nname: alpha\ndescription: same\n---\nold body\n",
      "---\nname: alpha\ndescription: same\n---\nnew body\n",
    ),
  });
  assert.ok(!lines.some((l) => l.includes("OVERRIDE")));
});

// Everything after the tag describes an emitted artifact: posture that "flows straight into
// output", edges in a shipped body, an override the model reads. An excluded item emits none of it.
test("an excluded item gets no posture, override or candidate lines", () => {
  const lines = syncReport("sp", ["skills/gone/SKILL.md"], manifests, {
    ...NO_IO,
    readFile: oneFile(
      "skills/gone/SKILL.md",
      "---\nname: gone\ndescription: a\n---\nRun tdd.\n",
      "---\nname: gone\ndescription: b\n---\nRun writing-plans.\n",
    ),
    ledgerNames: ["tdd", "writing-plans"],
  });
  assert.equal(lines.length, 1);
  assert.match(String(lines[0]), /excluded — no action/);
});
