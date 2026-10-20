import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import * as api from "../src/index.js";
import { toCsv, toTsv } from "../src/report.js";

test("toCsv joins fields with commas and rows with newlines", () => {
  assert.equal(
    toCsv([
      ["a", "b"],
      ["c", "d"],
    ]),
    "a,b\nc,d",
  );
});

test("toCsv quotes fields with a comma, a quote, or a newline", () => {
  assert.equal(toCsv([["a,b", 'say "hi"', "x\ny", "plain"]]), '"a,b","say ""hi""","x\ny",plain');
});

test("toTsv keeps its own rules", () => {
  assert.equal(toTsv([["a\tb", 'say "hi"', "x"]]), '"a\tb"\t"say ""hi"""\tx');
  assert.equal(toTsv([["a,b"]]), "a,b");
});

test("toCsv is exported from the index and tested in test/report.test.js", () => {
  assert.equal(api.toCsv, toCsv);
  const tests = readFileSync(new URL("../test/report.test.js", import.meta.url), "utf8");
  assert.match(tests, /toCsv/);
});
