import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import {
  chmodSync,
  cpSync,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { buildAll } from "./build.ts";
import { blobSha, loadLock, lockKey, saveLock, stampFiles, stampMergeFiles } from "./lib/overlay.ts";
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

test("eject --patch --force replaces the old patch before recutting it", () => {
  const root = makeRepo();
  const dir = join(root, "overlays", "deniz-process", "alpha");
  const file = join(dir, "SKILL.md");
  const patchFile = join(dir, "overlay.patch");

  assert.ok(eject(root, ["deniz-process", "alpha", "--patch"]).ok);
  writeFileSync(file, readFileSync(file, "utf8").replace("Use superpowers:beta next.", "First edit."));
  assert.ok(eject(root, ["deniz-process", "alpha", "--patch"]).ok);
  assert.ok(existsSync(patchFile));
  writeFileSync(join(dir, "STRANDED.md"), "left beside the completed patch\n");

  const phase1 = eject(root, ["deniz-process", "alpha", "--patch", "--force"]);
  assert.ok(phase1.ok, phase1.out);
  assert.ok(existsSync(file), "force must lay down a fresh working copy");
  assert.ok(!existsSync(patchFile), "the old patch must not become part of the working copy");
  assert.ok(!existsSync(join(dir, "STRANDED.md")), "force must discard a stranded working copy");

  writeFileSync(file, readFileSync(file, "utf8").replace("Use superpowers:beta next.", "Second edit."));
  const phase2 = eject(root, ["deniz-process", "alpha", "--patch"]);
  assert.ok(phase2.ok, phase2.out);
  const patch = readFileSync(patchFile, "utf8");
  assert.match(patch, /^\+Second edit\.$/m);
  assert.doesNotMatch(patch, /diff --git b\/overlay\.patch b\/overlay\.patch/);
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

test("a cut never carries the old overlay.patch into the new one", () => {
  const root = makeRepo();
  const dir = join(root, "overlays", "deniz-process", "gamma");
  cpSync(join(root, "external", "sp", "skills", "gamma"), dir, { recursive: true });
  const file = join(dir, "SKILL.md");
  writeFileSync(file, readFileSync(file, "utf8").replace("Replace this line.", "Patched line."));
  const r = eject(root, ["deniz-process", "gamma", "--patch"]);
  assert.ok(r.ok, r.out);
  assert.doesNotMatch(readFileSync(join(dir, "overlay.patch"), "utf8"), /overlay\.patch/);
});

test("a cut refuses replay residue and leaves the old patch byte-identical", () => {
  for (const residue of ["marker", "rej"]) {
    const root = makeRepo();
    const dir = join(root, "overlays", "deniz-process", "gamma");
    const old = readFileSync(join(dir, "overlay.patch"), "utf8");
    cpSync(join(root, "external", "sp", "skills", "gamma"), dir, { recursive: true });
    const file = join(dir, "SKILL.md");
    if (residue === "marker") {
      writeFileSync(
        file,
        readFileSync(file, "utf8").replace("Replace this line.", "<<<<<<< ours\nA\n=======\nB\n>>>>>>> theirs"),
      );
    } else {
      writeFileSync(join(dir, "SKILL.md.rej"), "@@ -1 +1 @@\n");
    }
    const r = eject(root, ["deniz-process", "gamma", "--patch"]);
    assert.equal(r.ok, false, residue);
    assert.match(r.out, residue === "marker" ? /SKILL\.md: conflict marker/ : /SKILL\.md\.rej: reject file/);
    assert.equal(readFileSync(join(dir, "overlay.patch"), "utf8"), old);
    assert.ok(existsSync(file), "the working copy stays");
  }
});

// The refusal above exits before any write, so it cannot tell a check-then-write cut from a
// write-then-check one. A binary edit can: the cut carries no full index line, so its check fails.
test("a cut that does not apply back leaves the old patch and the working copy in place", () => {
  const root = makeRepo();
  const dir = join(root, "overlays", "deniz-process", "gamma");
  const old = readFileSync(join(dir, "overlay.patch"), "utf8");
  writeFileSync(join(root, "external", "sp", "skills", "gamma", "img.bin"), Buffer.from([0, 1, 2, 3, 0, 9]));
  cpSync(join(root, "external", "sp", "skills", "gamma"), dir, { recursive: true });
  writeFileSync(join(dir, "img.bin"), Buffer.from([0, 9, 8, 7, 0, 1]));
  const r = eject(root, ["deniz-process", "gamma", "--patch"]);
  assert.equal(r.ok, false, r.out);
  assert.match(r.out, /does not apply back to sp\/skills\/gamma/);
  assert.equal(readFileSync(join(dir, "overlay.patch"), "utf8"), old);
  assert.ok(existsSync(join(dir, "SKILL.md")), "the working copy stays");
  assert.ok(existsSync(join(dir, "img.bin")), "the edited file stays");
});

test("eject never copies an upstream root .git gitfile", () => {
  const root = makeRepo();
  const delta = join(root, "external", "sp", "skills", "delta");
  // A live gitfile, as a checked-out submodule root has: the cut's check runs git inside it.
  execFileSync("git", ["init", "-q", "--separate-git-dir", join(root, "delta.git"), delta]);
  assert.ok(existsSync(join(delta, ".git")));
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  const dir = join(root, "overlays", "deniz-process", "delta");
  assert.ok(!existsSync(join(dir, ".git")));
  // The cut's upstream side must drop the gitfile too, or every cut of a root item deletes .git.
  const file = join(dir, "SKILL.md");
  writeFileSync(file, readFileSync(file, "utf8").replace("Delta body.", "Patched body."));
  const r = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(r.ok, r.out);
  assert.doesNotMatch(readFileSync(join(dir, "overlay.patch"), "utf8"), /\.git\b/);
  rmSync(dir, { recursive: true });
  assert.ok(eject(root, ["deniz-process", "delta"]).ok);
  assert.ok(!(".git" in (loadLock(root)[lockKey("deniz-process", "delta")]?.files ?? {})));
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
  // beta also backs beta-agent, whose frontmatter override is stamped on its own.
  assert.throws(() => buildAll(root), /beta-agent: upstream changed SKILL\.md under the frontmatter override/);
  assert.ok(eject(root, ["deniz-process", "beta-agent", "--bless", "--yes"]).ok);
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

test("eject --bless refuses a patch that no longer applies, even with --yes", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "gamma", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Replace this line.", "Replaced upstream."));
  const lockFile = join(root, "overlays", "overlays.lock.json");
  const before = readFileSync(lockFile, "utf8");
  const r = eject(root, ["deniz-process", "gamma", "--bless", "--yes"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /overlay\.patch no longer applies to sp\/skills\/gamma/);
  assert.match(r.out, /npm run eject -- deniz-process gamma --patch --rebase/);
  assert.doesNotMatch(r.out, /DRIFTED/, "refused before any drift display");
  assert.equal(readFileSync(lockFile, "utf8"), before);
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

// The filename rule can only stamp names the overlay itself owns, so a merge that drew from a
// differently named file in its source was unguarded — and shrinking the merge to fit the guard
// would be letting the toolchain decide curation. The declared list is how the guard follows.
test("eject --bless stamps the files a merge source declares, not just the overlay's own names", () => {
  const root = makeRepo();
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
      "    merged_from:",
      "      - source: sp/skills/beta",
      "        files: [SKILL.md, references/notes.md]",
      "  - source: sp/skills/beta",
      "    exclude: true",
    ].join("\n")}\n`,
  );
  mkdirSync(join(root, "overlays", "deniz-process", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: merged\n---\n\nMerged body.\n",
  );
  saveLock(root, {});

  const r = eject(root, ["deniz-process", "alpha", "--bless"]);
  assert.ok(r.ok, r.out);
  const stamped = loadLock(root)[lockKey("deniz-process", "alpha")]?.mergeSources?.["sp/skills/beta"];
  assert.deepEqual(
    Object.keys(stamped ?? {}).sort(),
    ["SKILL.md", "references/notes.md"],
    "the declared list replaces the same-filename default",
  );
  assert.match(stamped?.["references/notes.md"] ?? "", /^[0-9a-f]{40}$/, "a name the overlay never carries");
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

test("eject --bless re-stamps a frontmatter override without a body, with no overlay directory", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "alpha", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Use superpowers:beta next.", "Three paths now."));
  assert.throws(() => buildAll(root), /under the frontmatter override/);

  const unconfirmed = eject(root, ["deniz-process", "alpha", "--bless"]);
  assert.equal(unconfirmed.ok, false, "what upstream did is shown for review before it is recorded");
  assert.match(unconfirmed.out, /DRIFTED sp\/skills\/alpha: SKILL\.md/);

  const r = eject(root, ["deniz-process", "alpha", "--bless", "--yes"]);
  assert.ok(r.ok, r.out);
  assert.match(
    r.out,
    /Blessed the frontmatter override of deniz-process\/alpha against current upstream \(sp\/skills\/alpha\)/,
  );
  assert.deepEqual(loadLock(root)[lockKey("deniz-process", "alpha")], {
    source: "sp/skills/alpha",
    files: { "SKILL.md": blobSha(up) },
    frontmatter: true,
  });
  assert.ok(!existsSync(join(root, "overlays", "deniz-process", "alpha")), "no overlay directory is made");
  assert.doesNotThrow(() => buildAll(root));
});

test("eject --bless records a first frontmatter stamp without asking for confirmation", () => {
  const root = makeRepo();
  const lock = loadLock(root);
  delete lock[lockKey("deniz-process", "alpha")];
  saveLock(root, lock);
  const r = eject(root, ["deniz-process", "alpha", "--bless"]);
  assert.ok(r.ok, r.out);
  assert.equal(loadLock(root)[lockKey("deniz-process", "alpha")]?.frontmatter, true);
});

test("eject --bless on an item with neither a body nor a frontmatter override has nothing to bless", () => {
  const root = makeRepo();
  const r = eject(root, ["deniz-process", "delta", "--bless"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /Nothing to bless/);
});

// --- eject --patch --rebase -------------------------------------------------------------------

/** Commits the fixture upstream, so a patch's index lines name blobs a 3-way merge can read. */
function commitUpstream(root: string): void {
  const sp = join(root, "external", "sp");
  for (const a of [
    ["init", "-q", "."],
    ["add", "-A"],
    ["commit", "-qm", "pin"],
  ]) {
    execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], {
      cwd: sp,
      stdio: "ignore",
    });
  }
}

/** Cuts a delta patch from the given edits and switches the item to `body: patch`; returns the patch. */
function patchDelta(root: string, edits: Record<string, [string, string]>): string {
  const dir = join(root, "overlays", "deniz-process", "delta");
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  for (const [f, [from, to]] of Object.entries(edits)) {
    writeFileSync(join(dir, f), readFileSync(join(dir, f), "utf8").replace(from, to));
  }
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace("  - source: sp/skills/delta", "  - source: sp/skills/delta\n    body: patch"),
  );
  return readFileSync(join(dir, "overlay.patch"), "utf8");
}

const changes = (p: string) =>
  p
    .split("\n")
    .filter((l) => /^[+-](?![+-]{2} )/.test(l))
    .sort();
const upstreamDelta = (root: string, f: string) => join(root, "external", "sp", "skills", "delta", f);
const deltaDir = (root: string) => join(root, "overlays", "deniz-process", "delta");
const lockText = (root: string) => readFileSync(join(root, "overlays", "overlays.lock.json"), "utf8");
const editUpstream = (root: string, f: string, from: string, to: string) => {
  const p = upstreamDelta(root, f);
  writeFileSync(p, readFileSync(p, "utf8").replace(from, to));
};
const twentyLines = `${Array.from({ length: 20 }, (_, i) => `line ${i + 1}`).join("\n")}\n`;

/** Every file under a directory with its content: what "left byte-identical" compares. */
function snapshot(dir: string): Record<string, string> {
  const out: Record<string, string> = {};
  if (!existsSync(dir)) {
    return out;
  }
  for (const e of readdirSync(dir, { withFileTypes: true, recursive: true })) {
    if (e.isFile()) {
      const p = join(e.parentPath, e.name);
      out[p.slice(dir.length)] = readFileSync(p, "utf8");
    }
  }
  return out;
}

test("R0 reopening a patch on unmoved upstream recuts it byte-identically", () => {
  const root = makeRepo();
  commitUpstream(root);
  const old = patchDelta(root, { "SKILL.md": ["Delta body.", "Our delta body."] });
  const lock = lockText(root);
  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.ok(r.ok, r.out);
  assert.match(r.out, /applied\s+SKILL\.md/);
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  assert.equal(readFileSync(join(deltaDir(root), "overlay.patch"), "utf8"), old);
  assert.equal(lockText(root), lock);
});

test("R1 context-only move rebases clean and recuts the same edit", () => {
  const root = makeRepo();
  commitUpstream(root);
  const old = patchDelta(root, { "SKILL.md": ["Delta body.", "Our delta body."] });
  editUpstream(root, "SKILL.md", "description: Delta upstream", "description: Delta moved");
  assert.throws(() => buildAll(root), /no longer applies[\s\S]*--patch --rebase/);

  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.ok(r.ok, r.out);
  assert.match(r.out, /merged\s+SKILL\.md/);
  assert.match(r.out, /Next: review the working copy, then: npm run eject -- deniz-process delta --patch/);
  const working = readFileSync(join(deltaDir(root), "SKILL.md"), "utf8");
  assert.match(working, /Delta moved/);
  assert.match(working, /Our delta body\./);
  assert.equal(readFileSync(join(deltaDir(root), "overlay.patch"), "utf8"), old, "the old patch stays until the cut");

  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  assert.deepEqual(changes(readFileSync(join(deltaDir(root), "overlay.patch"), "utf8")), changes(old));
  assert.equal(
    loadLock(root)[lockKey("deniz-process", "delta")]?.files["SKILL.md"],
    blobSha(upstreamDelta(root, "SKILL.md")),
  );
  assert.doesNotThrow(() => buildAll(root));
  const emitted = readFileSync(join(root, "plugins", "deniz-process", "skills", "delta", "SKILL.md"), "utf8");
  assert.match(emitted, /Delta moved/);
  assert.match(emitted, /Our delta body\./);
});

test("R2 a real conflict stops, keeps the old patch, and blocks the cut until resolved", () => {
  const root = makeRepo();
  commitUpstream(root);
  const old = patchDelta(root, { "SKILL.md": ["Delta body.", "Our delta body."] });
  editUpstream(root, "SKILL.md", "Delta body.", "Delta body, upstream.");
  const lock = lockText(root);

  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.equal(r.ok, false, r.out);
  assert.match(r.out, /CONFLICT\s+SKILL\.md \(1 region\)/);
  assert.match(r.out, /<<<<<<< ours = current upstream/);
  assert.match(r.out, /delete each \.rej, review, then: npm run eject -- deniz-process delta --patch/);
  assert.equal(lockText(root), lock);
  assert.equal(readFileSync(join(deltaDir(root), "overlay.patch"), "utf8"), old);

  const blocked = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.equal(blocked.ok, false);
  assert.match(blocked.out, /SKILL\.md: conflict marker/);

  const resolved = readFileSync(upstreamDelta(root, "SKILL.md"), "utf8").replace(
    "Delta body, upstream.",
    "Our delta body, upstream.",
  );
  writeFileSync(join(deltaDir(root), "SKILL.md"), resolved);
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  assert.doesNotThrow(() => buildAll(root));
});

test("R3 one file's conflict leaves the other file's edit in place", () => {
  const root = makeRepo();
  commitUpstream(root);
  patchDelta(root, {
    "SKILL.md": ["Delta body.", "Our delta body."],
    "references/notes.md": ["delta note", "our delta note"],
  });
  editUpstream(root, "SKILL.md", "Delta body.", "Delta body, upstream.");
  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.equal(r.ok, false, r.out);
  assert.match(r.out, /CONFLICT\s+SKILL\.md/);
  assert.match(r.out, /applied\s+references\/notes\.md/);
  assert.match(readFileSync(join(deltaDir(root), "references", "notes.md"), "utf8"), /our delta note/);
});

test("R4 a patch with no base blob falls back to .rej", () => {
  const root = makeRepo();
  const up = join(root, "external", "sp", "skills", "gamma", "SKILL.md");
  writeFileSync(up, readFileSync(up, "utf8").replace("Replace this line.", "Replace this line, upstream."));
  const r = eject(root, ["deniz-process", "gamma", "--patch", "--rebase"]);
  assert.equal(r.ok, false, r.out);
  assert.match(r.out, /REJECTED\s+SKILL\.md -> SKILL\.md\.rej \(1 of 1 hunks/);
  const cut = eject(root, ["deniz-process", "gamma", "--patch"]);
  assert.equal(cut.ok, false, cut.out);
  assert.match(cut.out, /SKILL\.md\.rej: reject file/);
});

test("R5 a target deleted upstream is GONE and drops out after resolution", () => {
  const root = makeRepo();
  commitUpstream(root);
  patchDelta(root, {
    "SKILL.md": ["Delta body.", "Our delta body."],
    "references/notes.md": ["delta note", "our delta note"],
  });
  rmSync(upstreamDelta(root, "references/notes.md"));
  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.equal(r.ok, false, r.out);
  assert.match(r.out, /GONE\s+references\/notes\.md -> references\/notes\.md\.rej/);
  rmSync(join(deltaDir(root), "references", "notes.md.rej"));
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  assert.deepEqual(Object.keys(loadLock(root)[lockKey("deniz-process", "delta")]?.files ?? {}), ["SKILL.md"]);
  assert.doesNotThrow(() => buildAll(root));
});

// A patch cut on Windows records 100644 for every file; on a POSIX checkout the upstream script is
// 100755, and neither the replay nor the recut may drop the bit or record a mode change.
test("R6 exec mode survives replay and cut", { skip: process.platform === "win32" }, () => {
  const root = makeRepo();
  const run = upstreamDelta(root, "scripts/run.sh");
  mkdirSync(join(run, ".."), { recursive: true });
  writeFileSync(run, twentyLines);
  chmodSync(run, 0o755);
  commitUpstream(root);
  patchDelta(root, { "scripts/run.sh": ["line 12\n", "line 12 ours\n"] });
  const patchFile = join(deltaDir(root), "overlay.patch");
  writeFileSync(
    patchFile,
    readFileSync(patchFile, "utf8").replace(/^(index [0-9a-f]+\.\.[0-9a-f]+) 100755$/gm, "$1 100644"),
  );
  editUpstream(root, "scripts/run.sh", "line 6\n", "line 6 upstream\n");
  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.ok(r.ok, r.out);
  assert.match(r.out, /merged\s+scripts\/run\.sh/, "the mode is kept across a real 3-way merge");
  assert.equal(statSync(join(deltaDir(root), "scripts", "run.sh")).mode & 0o777, 0o755);
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  const patch = readFileSync(patchFile, "utf8");
  assert.doesNotMatch(patch, /^old mode/m);
  assert.doesNotMatch(patch, /^new mode/m);
});

test("R7 a non-Markdown file rebases like Markdown", () => {
  const root = makeRepo();
  const ps1 = upstreamDelta(root, "scripts/check.ps1");
  mkdirSync(join(ps1, ".."), { recursive: true });
  writeFileSync(ps1, twentyLines);
  commitUpstream(root);
  const old = patchDelta(root, { "scripts/check.ps1": ["line 12\n", "line 12 ours\n"] });
  editUpstream(root, "scripts/check.ps1", "line 6\n", "line 6 upstream\n");
  assert.throws(() => buildAll(root), /no longer applies[\s\S]*--patch --rebase/);

  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.ok(r.ok, r.out);
  assert.match(r.out, /merged\s+scripts\/check\.ps1/);
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  assert.deepEqual(changes(readFileSync(join(deltaDir(root), "overlay.patch"), "utf8")), changes(old));
  assert.equal(
    loadLock(root)[lockKey("deniz-process", "delta")]?.files["scripts/check.ps1"],
    blobSha(upstreamDelta(root, "scripts/check.ps1")),
  );
  assert.doesNotThrow(() => buildAll(root));
  const emitted = readFileSync(
    join(root, "plugins", "deniz-process", "skills", "delta", "scripts", "check.ps1"),
    "utf8",
  );
  assert.match(emitted, /^line 6 upstream$[\s\S]*^line 12 ours$/m);
});

test("R8 merge-source drift is shown, and the cut stamps it", () => {
  const root = makeRepo();
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace(
      "  - source: sp/skills/delta",
      "  - source: sp/skills/delta\n    body: patch\n    merged_from: [sp/skills/beta]",
    ),
  );
  commitUpstream(root);
  const dir = deltaDir(root);
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  const skill = join(dir, "SKILL.md");
  writeFileSync(skill, readFileSync(skill, "utf8").replace("Delta body.", "Our delta body."));
  const first = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(first.ok, first.out);
  const beta = join(root, "external", "sp", "skills", "beta", "SKILL.md");
  writeFileSync(beta, readFileSync(beta, "utf8").replace("Beta body.", "Beta body, moved upstream."));

  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.ok(r.ok, r.out);
  assert.match(r.out, /DRIFTED sp\/skills\/beta: SKILL\.md/);
  assert.match(r.out, /^\+Beta body, moved upstream\.$/m);
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(cut.ok, cut.out);
  assert.equal(
    loadLock(root)[lockKey("deniz-process", "delta")]?.mergeSources?.["sp/skills/beta"]?.["SKILL.md"],
    blobSha(beta),
  );
  // beta also backs the deniz-beta full-file overlay and the beta-agent frontmatter override, whose
  // own reviews are their blesses.
  assert.ok(eject(root, ["deniz-process", "deniz-beta", "--bless", "--yes"]).ok);
  assert.ok(eject(root, ["deniz-process", "beta-agent", "--bless", "--yes"]).ok);
  assert.doesNotThrow(() => buildAll(root));
});

test("R9 every rebase refusal leaves the overlay directory and the lock byte-identical", () => {
  const cases: { args: string[]; setup?: (root: string) => void; message: RegExp }[] = [
    {
      args: ["gamma", "--rebase"],
      message: /--rebase reopens an overlay\.patch: npm run eject -- deniz-process gamma --patch --rebase/,
    },
    {
      args: ["gamma", "--patch", "--rebase", "--force"],
      message: /--rebase keeps the old edits and --force discards them — pass one/,
    },
    {
      args: ["delta", "--patch", "--rebase"],
      message:
        /Nothing to rebase: overlays\/deniz-process\/delta\/overlay\.patch does not exist — start a patch with: npm run eject -- deniz-process delta --patch$/m,
    },
    {
      args: ["gamma", "--patch", "--rebase"],
      setup: (root) =>
        cpSync(join(root, "external", "sp", "skills", "gamma"), join(root, "overlays", "deniz-process", "gamma"), {
          recursive: true,
        }),
      message:
        /already holds a working copy — cut it with: npm run eject -- deniz-process gamma --patch, or delete its files \(overlay\.patch stays\) to replay again/,
    },
    {
      args: ["delta", "--patch", "--rebase"],
      setup: (root) => {
        assert.ok(eject(root, ["deniz-process", "delta"]).ok);
        writeFileSync(join(root, "curation", "deniz-process.yaml"), manifestWithDeltaOverlay(root));
      },
      message:
        /overlays\/deniz-process\/delta\/ is a full-file overlay — review upstream with: npm run eject -- deniz-process delta --bless/,
    },
  ];
  for (const c of cases) {
    const root = makeRepo();
    c.setup?.(root);
    const label = c.args.join(" ");
    const dir = join(root, "overlays", "deniz-process", c.args[0] ?? "");
    const before = snapshot(dir);
    const lock = lockText(root);
    const r = eject(root, ["deniz-process", ...c.args]);
    assert.equal(r.ok, false, label);
    assert.match(r.out, c.message, label);
    assert.deepEqual(snapshot(dir), before, label);
    assert.equal(lockText(root), lock, label);
  }
});

test("R10 the phase-1 refusal names the rebase", () => {
  const root = makeRepo();
  const r = eject(root, ["deniz-process", "gamma", "--patch"]);
  assert.equal(r.ok, false);
  assert.match(r.out, /reopen it on current upstream: npm run eject -- deniz-process gamma --patch --rebase/);
  assert.match(r.out, /--force/);
});

test("R11 upstream adopting every edit ends the patch instead of looping", () => {
  const root = makeRepo();
  commitUpstream(root);
  const old = patchDelta(root, { "SKILL.md": ["Delta body.", "Our delta body."] });
  editUpstream(root, "SKILL.md", "Delta body.", "Our delta body.");
  const r = eject(root, ["deniz-process", "delta", "--patch", "--rebase"]);
  assert.ok(r.ok, r.out);
  assert.match(r.out, /(?:merged|applied)\s+SKILL\.md/);
  const lock = lockText(root);
  const cut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.equal(cut.ok, false, cut.out);
  assert.match(cut.out, /already carries every edit/);
  assert.match(cut.out, /overlays\/deniz-process\/delta\//);
  assert.match(cut.out, /deniz-process\/delta entry in overlays\/overlays\.lock\.json/);
  assert.match(cut.out, /body: patch/);
  assert.equal(readFileSync(join(deltaDir(root), "overlay.patch"), "utf8"), old);
  assert.equal(lockText(root), lock);
});

test("R12 the cut asks for body: patch only when the item does not have it yet", () => {
  const root = makeRepo();
  commitUpstream(root);
  assert.ok(eject(root, ["deniz-process", "delta", "--patch"]).ok);
  const file = join(deltaDir(root), "SKILL.md");
  writeFileSync(file, readFileSync(file, "utf8").replace("Delta body.", "Our delta body."));
  const first = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(first.ok, first.out);
  assert.match(first.out, /set 'body: patch' on that item in curation\/deniz-process\.yaml/);

  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace("  - source: sp/skills/delta", "  - source: sp/skills/delta\n    body: patch"),
  );
  assert.ok(eject(root, ["deniz-process", "delta", "--patch", "--rebase"]).ok);
  const recut = eject(root, ["deniz-process", "delta", "--patch"]);
  assert.ok(recut.ok, recut.out);
  assert.doesNotMatch(recut.out, /set 'body: patch'/);
  assert.match(recut.out, /Next: npm run build/);
});
