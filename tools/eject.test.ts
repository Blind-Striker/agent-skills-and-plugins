import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { loadLock, lockKey, saveLock, stampFiles, stampMergeFiles } from "./lib/overlay.ts";
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

// What a merged body looks like on disk: one overlay, a manifest declaring what it was merged from,
// and a lock stamping the primary plus every declared source.
function mergedAlpha(root: string): void {
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    body: overlay",
      "    merged_from: [sp/skills/beta]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\n\nMerged body.\n",
  );
  saveLock(root, {
    [lockKey("deniz-process", "alpha")]: {
      source: "sp/skills/alpha",
      files: stampFiles(join(root, "external", "sp", "skills", "alpha"), ["SKILL.md"]),
      mergeSources: {
        "sp/skills/beta": stampMergeFiles(join(root, "external", "sp", "skills", "beta"), ["SKILL.md"]),
      },
    },
  });
}

function manifestWithDeltaOverlay(root: string): string {
  const p = join(root, "curation", "deniz-process.yaml");
  return readFileSync(p, "utf8").replace(
    "  - source: sp/skills/delta",
    "  - source: sp/skills/delta\n    body: overlay",
  );
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
  const files = loadLock(root)[lockKey("deniz-process", "alpha")]?.files ?? {};
  assert.ok(files["SKILL.md"], "a patch is blessed against the upstream files it touches");
});

// Most upstream skills carry a references/ or scripts/ directory. A flat readdir put those
// directory names into the hash list, and `git hash-object` cannot hash a directory.
test("eject handles a skill that carries a subdirectory", () => {
  const root = makeRepo();
  const r = eject(root, ["deniz-process", "delta"]);
  assert.ok(r.ok, r.out);
  assert.ok(existsSync(join(root, "overlays", "deniz-process", "delta", "references", "notes.md")));
  const files = loadLock(root)[lockKey("deniz-process", "delta")]?.files ?? {};
  assert.ok(files["SKILL.md"], "top-level file must be stamped");
  assert.ok(files["references/notes.md"], "a nested file must be stamped too, keyed by its relative path");
  assert.ok(!("references" in files), "a directory is not a file and must never be hashed");
});

test("drift in a nested overlay file is detected", () => {
  const root = makeRepo();
  assert.ok(eject(root, ["deniz-process", "delta"]).ok);
  writeFileSync(join(root, "curation", "deniz-process.yaml"), manifestWithDeltaOverlay(root));
  assert.doesNotThrow(() => buildAll(root));
  const up = join(root, "external", "sp", "skills", "delta", "references", "notes.md");
  writeFileSync(up, "upstream rewrote the note\n");
  assert.throws(() => buildAll(root), /references\/notes\.md/);
});

// Phase 2 rebuilt the diff by copying over a committed baseline, so `git diff` could not see an
// added file (untracked) or a deleted one (the copy never removes) — and the working copy was
// deleted regardless, taking the addition with it.
test("eject --patch captures added and deleted files, not just modified ones", () => {
  const root = makeRepo();
  const dir = join(root, "overlays", "deniz-process", "delta");
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  const skill = join(dir, "SKILL.md");
  writeFileSync(skill, readFileSync(skill, "utf8").replace("Delta body.", "Our delta body."));
  writeFileSync(join(dir, "EXTRA.md"), "we added this\n");
  rmSync(join(dir, "references"), { recursive: true });

  const r = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(r.ok, r.out);
  const patch = readFileSync(join(dir, "overlay.patch"), "utf8");
  assert.match(patch, /Our delta body\./, "the modification");
  assert.match(patch, /EXTRA\.md/, "the addition must reach the patch");
  assert.match(patch, /references\/notes\.md/, "the deletion must reach the patch");
});

// cpSync absolutizes a symlink's target, so an ejected link would carry a machine path into a
// committed directory — the leak ADR-0001 says must never reach output.
test("eject never copies a symlink into overlays/", () => {
  const root = makeRepo();
  const shared = join(root, "external", "sp", "shared");
  mkdirSync(shared, { recursive: true });
  writeFileSync(join(shared, "f.md"), "shared\n");
  symlinkSync(join("..", "..", "shared"), join(root, "external", "sp", "skills", "delta", "linked"), "dir");

  assert.ok(eject(root, ["deniz-process", "delta"]).ok);
  const dir = join(root, "overlays", "deniz-process", "delta");
  assert.ok(!existsSync(join(dir, "linked")), "the link must not travel");
  assert.ok(existsSync(join(dir, "SKILL.md")), "the rest of the item still ejects");
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

  const unconfirmed = eject(root, ["deniz-process", "deniz-beta", "--bless"]);
  assert.equal(unconfirmed.ok, false, "what upstream did is shown for review before it is recorded");
  assert.match(unconfirmed.out, /sp\/skills\/beta: SKILL\.md/);

  const r = eject(root, ["deniz-process", "deniz-beta", "--bless", "--yes"]);
  assert.ok(r.ok, r.out);
  assert.doesNotThrow(() => buildAll(root));
});

test("eject --bless re-stamps a patch overlay against the files its patch touches", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "gamma", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Far region.", "Far region, moved upstream."));
  assert.throws(() => buildAll(root), /upstream changed under the overlay/);

  const r = eject(root, ["deniz-process", "gamma", "--bless", "--yes"]);
  assert.ok(r.ok, r.out);
  const files = loadLock(root)[lockKey("deniz-process", "gamma")]?.files ?? {};
  assert.deepEqual(Object.keys(files), ["SKILL.md"], "only the files the patch touches are stamped");
  assert.doesNotThrow(() => buildAll(root));
});

// ADR-0001: a re-bless that displays nothing invites rubber-stamping the very look it exists to
// force. Merge sources sharpen it — they are ingredients the primary stamp cannot see at all.
test("eject --bless names the merge-source drift and refuses to record it without --yes", () => {
  const root = makeRepo();
  mergedAlpha(root);
  const beta = join(root, "external", "sp", "skills", "beta", "SKILL.md");
  writeFileSync(beta, readFileSync(beta, "utf8").replace("Beta body.", "Beta body, moved upstream."));
  const lockFile = join(root, "overlays", "overlays.lock.json");
  const before = readFileSync(lockFile, "utf8");

  const unconfirmed = eject(root, ["deniz-process", "alpha", "--bless"]);
  assert.equal(unconfirmed.ok, false);
  assert.match(unconfirmed.out, /sp\/skills\/beta: SKILL\.md/, "the source that moved has to be named");
  assert.match(unconfirmed.out, /--yes/);
  assert.equal(readFileSync(lockFile, "utf8"), before, "a refused bless records nothing");

  const confirmed = eject(root, ["deniz-process", "alpha", "--bless", "--yes"]);
  assert.ok(confirmed.ok, confirmed.out);
  const stamp = loadLock(root)[lockKey("deniz-process", "alpha")]?.mergeSources?.["sp/skills/beta"]?.["SKILL.md"];
  assert.match(stamp ?? "", /^[0-9a-f]{40}$/, "every declared source is stamped in one act");
});

// The lock holds the previous blob SHA, and only the submodule that owns the address can resolve it
// back into content — without that lookup "review the diff" has no diff to review.
test("eject --bless shows the recorded content against the current one", () => {
  const root = makeRepo();
  mergedAlpha(root);
  const sp = join(root, "external", "sp");
  execFileSync("git", ["init", "-q", "."], { cwd: sp });
  // -w puts the blessed content in the submodule's object database, which is what a real checkout
  // has and the only thing that gives the diff a "before" side.
  execFileSync("git", ["hash-object", "-w", "--no-filters", "--", "skills/beta/SKILL.md"], { cwd: sp });
  const beta = join(sp, "skills", "beta", "SKILL.md");
  writeFileSync(beta, readFileSync(beta, "utf8").replace("Beta body.", "Beta body, moved upstream."));

  const r = eject(root, ["deniz-process", "alpha", "--bless"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /^-Beta body\.$/m, "the content the overlay was written against");
  assert.match(r.out, /^\+Beta body, moved upstream\.$/m, "what is there now");
});

// build sends you here when the lock has no entry at all. Nothing is recorded, so there is nothing
// to review and a confirmation prompt would be theatre.
test("eject --bless records a first blessing without asking for confirmation", () => {
  const root = makeRepo();
  assert.ok(eject(root, ["deniz-process", "delta"]).ok);
  saveLock(root, {});
  const r = eject(root, ["deniz-process", "delta", "--bless"]);
  assert.ok(r.ok, r.out);
  assert.ok(loadLock(root)[lockKey("deniz-process", "delta")]?.files["SKILL.md"], "the first bless still stamps");
});
