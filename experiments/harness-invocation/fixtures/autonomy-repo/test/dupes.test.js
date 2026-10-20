import assert from "node:assert/strict";
import { test } from "node:test";
import { findDuplicates } from "../src/dupes.js";

test("findDuplicates orders by second appearance", () => {
  assert.deepEqual(findDuplicates([3, 1, 2, 1, 3, 3]), [1, 3]);
  assert.deepEqual(findDuplicates(["b", "a", "a", "b"]), ["a", "b"]);
});

test("findDuplicates returns an empty list when nothing repeats", () => {
  assert.deepEqual(findDuplicates([1, 2, 3]), []);
});
