import assert from "node:assert/strict";
import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { tempDir } from "../testutil.ts";
import { loadManifest } from "./manifest.ts";

function write(content: string): string {
  const p = join(tempDir("manifest"), "x.yaml");
  writeFileSync(p, content);
  return p;
}

test("loads a valid manifest", () => {
  const m = loadManifest(
    write(
      "plugin:\n  name: deniz-process\n  description: Process skills\n  version: 0.1.0\n" +
        "items:\n  - source: superpowers/skills/brainstorming\n    name: brainstorm\n    as: command\n",
    ),
  );
  assert.equal(m.plugin.name, "deniz-process");
  const item = m.items[0];
  assert.ok(item);
  assert.equal(item.source, "superpowers/skills/brainstorming");
  assert.equal(item.as, "command");
});

test("loads an item with optional enum fields absent", () => {
  const m = loadManifest(
    write("plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n"),
  );
  const item = m.items[0];
  assert.ok(item);
  assert.equal(item.invocation, undefined);
  assert.equal(item.as, undefined);
  assert.equal(item.body, undefined);
});

test("rejects an invocation outside the enum at load", () => {
  const p = write(
    "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    invocation: manul\n",
  );
  assert.throws(
    () => loadManifest(p),
    (err: unknown) =>
      err instanceof Error &&
      err.message === `${p}: sp/skills/a: invocation must be one of auto|manual|both (got "manul")`,
  );
});

test("rejects a non-string invocation at load", () => {
  const p = write(
    "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    invocation: 1\n",
  );
  assert.throws(
    () => loadManifest(p),
    (err: unknown) =>
      err instanceof Error && err.message === `${p}: sp/skills/a: invocation must be one of auto|manual|both (got 1)`,
  );
});

test("rejects a null invocation (bare key) at load", () => {
  const p = write(
    "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    invocation:\n",
  );
  assert.throws(
    () => loadManifest(p),
    (err: unknown) =>
      err instanceof Error &&
      err.message === `${p}: sp/skills/a: invocation must be one of auto|manual|both (got null)`,
  );
});

test("rejects an as value outside the enum at load", () => {
  const p = write(
    "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    as: skil\n",
  );
  assert.throws(
    () => loadManifest(p),
    (err: unknown) =>
      err instanceof Error && err.message === `${p}: sp/skills/a: as must be one of skill|command|agent (got "skil")`,
  );
});

test("rejects a body value outside the enum at load", () => {
  const p = write(
    "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    body: overaly\n",
  );
  assert.throws(
    () => loadManifest(p),
    (err: unknown) =>
      err instanceof Error && err.message === `${p}: sp/skills/a: body must be one of overlay|patch (got "overaly")`,
  );
});

test("items defaults to empty array", () => {
  const m = loadManifest(write("plugin:\n  name: p\n  description: d\n  version: 0.1.0\n"));
  assert.deepEqual(m.items, []);
});

test("missing plugin.name throws with path", () => {
  const p = write("plugin:\n  description: d\n  version: 0.1.0\n");
  assert.throws(
    () => loadManifest(p),
    (err: unknown) => err instanceof Error && err.message.includes(p) && /plugin\.name/.test(err.message),
  );
});

test("item without source throws", () => {
  const p = write("plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - name: x\n");
  assert.throws(() => loadManifest(p), /source/);
});

const withHarnessFiles = (block: string): string =>
  write(
    `plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    harness_files:\n${block}`,
  );

test("harness_files maps one output path to a source per harness, null stating a gap", () => {
  const m = loadManifest(
    withHarnessFiles(
      "      references/harness-tools.md:\n        claude: references/claude.md\n        codex: references/codex.md\n        opencode: null\n",
    ),
  );
  assert.deepEqual(m.items[0]?.harness_files, {
    "references/harness-tools.md": { claude: "references/claude.md", codex: "references/codex.md", opencode: null },
  });
});

test("harness_files rejects a harness it does not name, and every malformed path", () => {
  const cases: [string, RegExp][] = [
    [
      "      out.md:\n        claude: a.md\n        codex: b.md\n",
      /harness_files out\.md states no source for opencode — name one, or null to ship no file there/,
    ],
    [
      "      out.md:\n        claude: a.md\n        codex: b.md\n        opencode: c.md\n        cursor: d.md\n",
      /harness_files out\.md names an unknown harness cursor/,
    ],
    ["      out.md: a.md\n", /harness_files out\.md must map claude, codex, and opencode to a source or null/],
    [
      "      ../out.md:\n        claude: a.md\n        codex: b.md\n        opencode: c.md\n",
      /not an item-relative path/,
    ],
    [
      "      out.md:\n        claude: /a.md\n        codex: b.md\n        opencode: c.md\n",
      /not an item-relative path/,
    ],
    ["      SKILL.md:\n        claude: a.md\n        codex: b.md\n        opencode: c.md\n", /SKILL\.md/],
    ["      out.md:\n        claude: SKILL.md\n        codex: b.md\n        opencode: c.md\n", /SKILL\.md/],
    ["      out.md:\n        claude: out.md\n        codex: b.md\n        opencode: c.md\n", /is also a mapped source/],
    ["      out.md:\n        claude: null\n        codex: null\n        opencode: null\n", /maps no harness/],
  ];
  for (const [block, message] of cases) {
    assert.throws(() => loadManifest(withHarnessFiles(block)), message, block);
  }
});

const withOriginals = (block: string): string =>
  write(`plugin:\n  name: p\n  description: d\n  version: 0.1.0\noriginal_skills:\n${block}items: []\n`);

test("original_skills maps a skills/<plugin>/ directory to its declaration", () => {
  const m = loadManifest(withOriginals("  my-own:\n    invocation: manual\n"));
  assert.deepEqual(m.original_skills, { "my-own": { invocation: "manual" } });
});

test("original_skills rejects every malformed declaration", () => {
  const cases: [string, RegExp][] = [
    ["  - my-own\n", /: original_skills must map a skill directory name to its declaration$/],
    ["  my-own: auto\n", /: original_skills\.my-own must be a mapping$/],
    ["  my-own: {}\n", /: original_skills\.my-own: invocation is required \(auto\|manual\|both\)$/],
    [
      "  my-own:\n    invocation: manul\n",
      /original_skills\.my-own: invocation must be one of auto\|manual\|both \(got "manul"\)/,
    ],
    [
      "  my-own:\n    invocation: auto\n    invocaton: manual\n",
      /original_skills\.my-own: unknown key invocaton — a declaration holds only invocation/,
    ],
    ["  my-own:\n    invocation: auto\n    source: sp/x\n", /original_skills\.my-own: unknown key source/],
    [
      "  my-own:\n    invocation: auto\n    frontmatter:\n      name: x\n",
      /original_skills\.my-own: unknown key frontmatter/,
    ],
    ["  my-own:\n    invocation: auto\n  my-own:\n    invocation: both\n", /Map keys must be unique/],
  ];
  for (const [block, message] of cases) {
    assert.throws(() => loadManifest(withOriginals(block)), message, block);
  }
});

test("original_skills takes harness_files under the curated grammar", () => {
  const m = loadManifest(
    withOriginals(
      "  my-own:\n    invocation: manual\n    harness_files:\n      references/tools.md:\n        claude: references/claude.md\n        codex: references/codex.md\n        opencode: null\n",
    ),
  );
  assert.deepEqual(m.original_skills, {
    "my-own": {
      invocation: "manual",
      harness_files: {
        "references/tools.md": { claude: "references/claude.md", codex: "references/codex.md", opencode: null },
      },
    },
  });
  assert.throws(
    () =>
      loadManifest(
        withOriginals(
          "  my-own:\n    invocation: manual\n    harness_files:\n      SKILL.md:\n        claude: a.md\n        codex: b.md\n        opencode: c.md\n",
        ),
      ),
    /original_skills\.my-own: harness_files cannot replace SKILL\.md/,
  );
});

test("original_skills takes depends_on", () => {
  const m = loadManifest(withOriginals("  my-own:\n    invocation: auto\n    depends_on: [a]\n"));
  assert.deepEqual(m.original_skills, { "my-own": { invocation: "auto", depends_on: ["a"] } });
});

test("loadManifest accepts optional_refs on curated and original skills", () => {
  const original = loadManifest(withOriginals("  my-own:\n    invocation: auto\n    optional_refs: [remote]\n"));
  assert.deepEqual(original.original_skills?.["my-own"]?.optional_refs, ["remote"]);
  const curated = loadManifest(
    write(
      "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    optional_refs: [remote]\n",
    ),
  );
  assert.deepEqual(curated.items[0]?.optional_refs, ["remote"]);
});

test("loadManifest rejects malformed optional_refs and dependency overlap for either declaration kind", () => {
  for (const value of ["null", "remote", "{}", "[]", "[null]", "[1]", "['']", "['remote.name']", "[remote, remote]"]) {
    for (const original of [false, true]) {
      const fields = `    optional_refs: ${value}\n`;
      const path = original
        ? withOriginals(`  my-own:\n    invocation: auto\n${fields}`)
        : write(`plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n${fields}`);
      assert.throws(() => loadManifest(path), /optional_refs/, `${original}: ${value}`);
    }
  }
  assert.throws(
    () =>
      loadManifest(
        withOriginals("  my-own:\n    invocation: auto\n    depends_on: [remote]\n    optional_refs: [remote]\n"),
      ),
    /optional_refs.*overlap/,
  );
  assert.throws(
    () =>
      loadManifest(
        write(
          "plugin:\n  name: p\n  description: d\n  version: 0.1.0\nitems:\n  - source: sp/skills/a\n    depends_on: [remote]\n    optional_refs: [remote]\n",
        ),
      ),
    /optional_refs.*overlap/,
  );
});
