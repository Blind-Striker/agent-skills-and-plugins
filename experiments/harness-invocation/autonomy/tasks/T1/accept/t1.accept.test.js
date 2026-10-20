import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { formatDuration } from "../src/duration.js";
import * as api from "../src/index.js";

test("formatDuration renders non-zero units from largest to smallest", () => {
  assert.equal(formatDuration(3_723_000), "1h 2m 3s");
  assert.equal(formatDuration(3_600_000), "1h");
  assert.equal(formatDuration(61_000), "1m 1s");
  assert.equal(formatDuration(7_205_000), "2h 5s");
});

test("formatDuration renders zero and drops sub-second remainders", () => {
  assert.equal(formatDuration(0), "0s");
  assert.equal(formatDuration(999), "0s");
  assert.equal(formatDuration(1_500), "1s");
});

test("formatDuration is exported from the index", () => {
  assert.equal(api.formatDuration, formatDuration);
});

test("README lists formatDuration in its API section", () => {
  const readme = readFileSync(new URL("../README.md", import.meta.url), "utf8");
  const api = readme.split(/^## API$/m)[1]?.split(/^## /m)[0] ?? "";
  assert.match(api, /formatDuration/);
});

test("test/duration.test.js covers formatDuration", () => {
  const tests = readFileSync(new URL("../test/duration.test.js", import.meta.url), "utf8");
  assert.match(tests, /formatDuration/);
});
