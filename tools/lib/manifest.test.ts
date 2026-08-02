import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
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
