import assert from "node:assert/strict";
import { writeFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";
import { makeRepo } from "../testutil.ts";
import { driftedMergeSources, stampMergeFiles, type LockEntry } from "./overlay.ts";

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
