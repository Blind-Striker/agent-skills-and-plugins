import assert from "node:assert/strict";
import { test } from "node:test";
import { cutResidue } from "./overlay.ts";

const chunk = (file: string, lines: string[]) =>
  [`diff --git a/${file} b/${file}`, `--- a/${file}`, `+++ b/${file}`, "@@ -1,2 +1,2 @@", ...lines, ""].join("\n");

test("cutResidue names an added marker line and an added .rej file", () => {
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+<<<<<<< ours"])), ["SKILL.md: conflict marker"]);
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+||||||| base"])), ["SKILL.md: conflict marker"]);
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+>>>>>>>"])), ["SKILL.md: conflict marker"]);
  const rej = [
    "diff --git a/SKILL.md.rej b/SKILL.md.rej",
    "new file mode 100644",
    "--- /dev/null",
    "+++ b/SKILL.md.rej",
    "@@ -0,0 +1 @@",
    "+x",
    "",
  ].join("\n");
  assert.deepEqual(cutResidue(rej), ["SKILL.md.rej: reject file"]);
});

test("cutResidue ignores markers in context, removed lines, and setext underlines", () => {
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" <<<<<<< HEAD", "->>>>>>> theirs", "+=======", "+Title"])), []);
});
