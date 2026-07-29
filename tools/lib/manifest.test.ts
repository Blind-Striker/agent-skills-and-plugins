import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadManifest } from "./manifest.ts";

function write(content: string): string {
  const p = join(mkdtempSync(join(tmpdir(), "manifest-")), "x.yaml");
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
  assert.equal(m.items[0].source, "superpowers/skills/brainstorming");
  assert.equal(m.items[0].as, "command");
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
