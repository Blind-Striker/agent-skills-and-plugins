import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./lib/manifest.ts";
import { type SyncIO, syncReport } from "./sync.ts";

// Reading upstream at both pins is the only impure thing the report needs, so it arrives as a
// parameter: these tests hand it fixtures where the CLI hands it `git show`.
const NO_IO: SyncIO = { readFile: () => null, ledgerNames: [] };

/** One readable file at both pins; everything else unreadable, as an added or deleted path is. */
function oneFile(rel: string, atOld: string, atNew: string): SyncIO["readFile"] {
  return (rev, r) => {
    if (r !== rel) {
      return null;
    }
    return rev === "old" ? atOld : atNew;
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
    ],
  },
];

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
    readFile: oneFile(
      "skills/alpha/SKILL.md",
      "---\nname: alpha\ndescription: a\n---\nB.\n",
      "---\nname: alpha\ndescription: a\ndisable-model-invocation: true\n---\nB.\n",
    ),
    ledgerNames: [],
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
    readFile: oneFile(
      "skills/alpha/SKILL.md",
      "---\nname: alpha\ndescription: a\n---\nRun /tdd here.\n",
      "---\nname: alpha\ndescription: a\n---\nSee writing-plans instead.\n",
    ),
    ledgerNames: ["tdd", "writing-plans"],
  });
  assert.ok(lines.some((l) => l.includes("CANDIDATE EDGES") && l.includes("+writing-plans") && l.includes("-tdd")));
});
