import assert from "node:assert/strict";
import { test } from "node:test";
import { backoff } from "../src/retry.js";

test("backoff grows by base each attempt", () => {
  assert.equal(backoff(1, 100), 100);
  assert.equal(backoff(2, 100), 200);
  assert.equal(backoff(3, 100), 300);
});
