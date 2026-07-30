import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { loadLock, lockKey } from "./lib/overlay.ts";
import { makeRepo } from "./testutil.ts";

// eject is a script, not a library: it reads process.argv and exits. Driving it as a subprocess is
// the only way to cover the flag handling, and it exercises the real git plumbing along the way.
function eject(root: string, args: string[]): { ok: boolean; out: string } {
  try {
    const out = execFileSync(process.execPath, [join(import.meta.dirname, "eject.ts"), ...args], {
      cwd: root,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    });
    return { ok: true, out };
  } catch (e) {
    const err = e as { stdout?: string; stderr?: string };
    return { ok: false, out: `${err.stdout ?? ""}${err.stderr ?? ""}` };
  }
}

test("eject writes a full-file overlay and records what it was written against", () => {
  const root = makeRepo();
  const r = eject(root, ["deniz-process", "alpha"]);
  assert.ok(r.ok, r.out);
  assert.ok(existsSync(join(root, "overlays", "deniz-process", "alpha", "SKILL.md")));
  const entry = loadLock(root)[lockKey("deniz-process", "alpha")];
  assert.equal(entry?.source, "sp/skills/alpha");
  assert.match(entry?.files["SKILL.md"] ?? "", /^[0-9a-f]{40}$/);
});

// Known gap this closes: eject used to overwrite an existing overlay, discarding hand edits.
test("eject refuses to overwrite an existing overlay unless forced", () => {
  const root = makeRepo();
  eject(root, ["deniz-process", "alpha"]);
  const file = join(root, "overlays", "deniz-process", "alpha", "SKILL.md");
  writeFileSync(file, "---\nname: alpha\ndescription: hand edited\n---\n\nMine.\n");
  const refused = eject(root, ["deniz-process", "alpha"]);
  assert.equal(refused.ok, false);
  assert.match(refused.out, /--force/);
  assert.match(readFileSync(file, "utf8"), /hand edited/, "the hand edit must survive a refused eject");
  assert.ok(eject(root, ["deniz-process", "alpha", "--force"]).ok);
  assert.doesNotMatch(readFileSync(file, "utf8"), /hand edited/);
});

test("eject --patch lays down a working copy, then turns the edits into overlay.patch", () => {
  const root = makeRepo();
  const dir = join(root, "overlays", "deniz-process", "alpha");
  const phase1 = eject(root, ["deniz-process", "alpha", "--patch"]);
  assert.ok(phase1.ok, phase1.out);
  assert.match(phase1.out, /working copy/);

  const file = join(dir, "SKILL.md");
  writeFileSync(file, readFileSync(file, "utf8").replace("Use superpowers:beta next.", "Use our own thing."));
  const phase2 = eject(root, ["deniz-process", "alpha", "--patch"]);
  assert.ok(phase2.ok, phase2.out);
  assert.ok(existsSync(join(dir, "overlay.patch")), "phase 2 replaces the working copy with a patch");
  assert.ok(!existsSync(file), "the working copy is consumed, not shipped alongside the patch");

  const patch = readFileSync(join(dir, "overlay.patch"), "utf8");
  assert.match(patch, /^diff --git a\/SKILL\.md b\/SKILL\.md$/m, "paths must be item-relative for -p1");
  assert.match(patch, /^\+Use our own thing\.$/m);
  assert.ok(!loadLock(root)[lockKey("deniz-process", "alpha")], "a patch is self-checking, never hashed");
});

test("eject --patch refuses a conversion, which has no stable file to diff against", () => {
  const root = makeRepo();
  const r = eject(root, ["deniz-process", "deniz-beta", "--patch"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /skill output only/);
});

test("eject --bless re-stamps a drifted full-file overlay and unblocks the build", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "beta", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Beta body.", "Beta body, moved upstream."));
  assert.throws(() => buildAll(root), /upstream changed under the overlay/);

  const r = eject(root, ["deniz-process", "deniz-beta", "--bless"]);
  assert.ok(r.ok, r.out);
  assert.doesNotThrow(() => buildAll(root));
});

test("eject --bless refuses a patch overlay", () => {
  const root = makeRepo();
  const r = eject(root, ["deniz-process", "gamma", "--bless"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /never blessed/);
});
