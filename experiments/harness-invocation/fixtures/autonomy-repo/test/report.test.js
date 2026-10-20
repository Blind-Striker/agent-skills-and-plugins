import assert from "node:assert/strict";
import { test } from "node:test";
import { toTsv } from "../src/report.js";

test("toTsv joins fields with tabs and rows with newlines", () => {
  assert.equal(toTsv([["a", "b"], ["c", "d"]]), "a\tb\nc\td");
});

test("toTsv quotes fields that need it", () => {
  assert.equal(toTsv([["a\tb", 'say "hi"', "x"]]), '"a\tb"\t"say ""hi"""\tx');
});
