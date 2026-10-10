import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { makeRepo } from "../testutil.ts";
import { blobSha, driftedFiles, driftedMergeSources, type LockEntry, stampFiles, stampMergeFiles } from "./overlay.ts";

test("blobSha hashes a file whose absolute path is longer than 260 characters", () => {
  const tmp = mkdtempSync(join(tmpdir(), "blob-"));
  try {
    const dir = join(tmp, ...Array.from({ length: 6 }, (_, i) => `${"d".repeat(40)}${i}`));
    mkdirSync(dir, { recursive: true });
    const file = join(dir, "references-with-a-long-name.md");
    assert.ok(file.length > 260);
    writeFileSync(file, "body\n");
    const expected = createHash("sha1").update("blob 5\0body\n").digest("hex");
    assert.equal(blobSha(file), expected);
  } finally {
    rmSync(tmp, { recursive: true, force: true });
  }
});

test("stampMergeFiles records a sha per present file and null per absent one", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const stamped = stampMergeFiles(dir, ["SKILL.md", "not-there.md"]);
  assert.match(stamped["SKILL.md"] as string, /^[0-9a-f]{40}$/);
  assert.equal(stamped["not-there.md"], null);
});

test("driftedMergeSources reports edits, deletions, and files that appeared over a null stamp", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/alpha",
    files: {},
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md", "notes.md"]) },
  };
  assert.deepEqual(driftedMergeSources(root, entry), []); // clean right after stamping

  writeFileSync(join(dir, "SKILL.md"), "---\nname: beta\ndescription: changed\n---\nMoved.\n");
  writeFileSync(join(dir, "notes.md"), "appeared later\n");
  const drifted = driftedMergeSources(root, entry);
  assert.deepEqual(drifted, ["sp/skills/beta: SKILL.md", "sp/skills/beta: notes.md (appeared upstream)"]);
});

test("a directory at a recorded-absent merge name is not drift", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/alpha",
    files: {},
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md", "notes.md"]) },
  };
  mkdirSync(join(dir, "notes.md"));
  assert.deepEqual(driftedMergeSources(root, entry), []);
});

test("a stamped file replaced by a directory is drift, not a hash-object error", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/beta",
    files: stampFiles(dir, ["SKILL.md"]),
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md"]) },
  };
  rmSync(join(dir, "SKILL.md"));
  mkdirSync(join(dir, "SKILL.md"));
  assert.deepEqual(driftedFiles(dir, entry), ["SKILL.md"]);
  assert.deepEqual(driftedMergeSources(root, entry), ["sp/skills/beta: SKILL.md"]);
});

test("drift of a fresh stamp is empty for a file, an absent name, and a directory", () => {
  const root = makeRepo();
  const dir = join(root, "external", "sp", "skills", "beta");
  const entry: LockEntry = {
    source: "sp/skills/beta",
    files: stampFiles(dir, ["SKILL.md", "references"]),
    mergeSources: { "sp/skills/beta": stampMergeFiles(dir, ["SKILL.md", "absent.md", "references"]) },
  };
  assert.deepEqual(entry.mergeSources?.["sp/skills/beta"]?.references, null);
  assert.deepEqual(driftedFiles(dir, entry), []);
  assert.deepEqual(driftedMergeSources(root, entry), []);
});
