import assert from "node:assert/strict";
import { test } from "node:test";
import { parseDuration } from "../src/duration.js";

test("parseDuration reads each unit", () => {
  assert.equal(parseDuration("1h2m3s"), 3_723_000);
  assert.equal(parseDuration("5m"), 300_000);
  assert.equal(parseDuration("30s"), 30_000);
});

test("parseDuration rejects other text", () => {
  assert.throws(() => parseDuration(""), TypeError);
  assert.throws(() => parseDuration("3s1h"), TypeError);
});
