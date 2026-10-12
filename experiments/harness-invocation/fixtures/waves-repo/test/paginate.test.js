import assert from "node:assert/strict";
import { test } from "node:test";
import { pageCount, paginate } from "../src/paginate.js";

const items = [1, 2, 3, 4, 5];

test("paginate returns the requested page", () => {
  assert.deepEqual(paginate(items, 1, 2), [1, 2]);
  assert.deepEqual(paginate(items, 3, 2), [5]);
});

test("paginate returns an empty page past the last one", () => {
  assert.deepEqual(paginate(items, 4, 2), []);
});

test("pageCount rounds a partial page up", () => {
  assert.equal(pageCount(5, 2), 3);
  assert.equal(pageCount(4, 2), 2);
  assert.equal(pageCount(0, 2), 0);
});
