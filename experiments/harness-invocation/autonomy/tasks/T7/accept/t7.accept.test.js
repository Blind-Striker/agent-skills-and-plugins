import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { test } from "node:test";
import { findDuplicates } from "../src/dupes.js";

test("findDuplicates keeps its contract", () => {
  assert.deepEqual(findDuplicates([3, 1, 2, 1, 3, 3]), [1, 3]);
  assert.deepEqual(findDuplicates(["b", "a", "a", "b"]), ["a", "b"]);
  assert.deepEqual(findDuplicates([1, 2, 3]), []);
  assert.deepEqual(findDuplicates([]), []);
  assert.deepEqual(findDuplicates([2, 2, 1, 1, 2]), [2, 1]);
  const a = {};
  assert.deepEqual(findDuplicates([a, {}, a]), [a]);
});

test("findDuplicates handles 100000 values in under 300 ms", () => {
  const script = `
    const { findDuplicates } = await import(${JSON.stringify(new URL("../src/dupes.js", import.meta.url).href)});
    const values = Array.from({ length: 100000 }, (_, i) => i % 50000);
    const start = performance.now();
    const result = findDuplicates(values);
    const ms = performance.now() - start;
    console.log(JSON.stringify({ ms, length: result.length, first: result[0], last: result.at(-1) }));
  `;
  const run = spawnSync(process.execPath, ["--input-type=module", "-e", script], {
    encoding: "utf8",
    timeout: 5000,
  });
  assert.equal(run.signal, null, "timed out");
  const out = JSON.parse(run.stdout);
  assert.deepEqual([out.length, out.first, out.last], [50000, 0, 49999]);
  assert.ok(out.ms < 300, `${out.ms} ms`);
});
