import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { test } from "node:test";

test("the baseline retry test file still exists", () => {
  assert.ok(existsSync(new URL("../test/retry.test.js", import.meta.url)));
});
