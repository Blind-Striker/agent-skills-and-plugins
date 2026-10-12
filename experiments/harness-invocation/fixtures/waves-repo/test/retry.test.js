import assert from "node:assert/strict";
import { test } from "node:test";
import { backoff, retry } from "../src/retry.js";

const noWait = async () => {};

test("backoff doubles after each failed attempt", () => {
  assert.equal(backoff(1, 100), 100);
  assert.equal(backoff(2, 100), 200);
  assert.equal(backoff(3, 100), 400);
});

test("retry returns the first result", async () => {
  let calls = 0;
  const result = await retry(
    async () => {
      calls += 1;
      if (calls < 2) {
        throw new Error("flaky");
      }
      return "ok";
    },
    { sleep: noWait },
  );
  assert.equal(result, "ok");
  assert.equal(calls, 2);
});

test("retry rejects with the last error after 3 attempts", async () => {
  let calls = 0;
  const waits = [];
  await assert.rejects(
    retry(
      async () => {
        calls += 1;
        throw new Error(`failure ${calls}`);
      },
      {
        baseMs: 10,
        sleep: async (ms) => {
          waits.push(ms);
        },
      },
    ),
    /failure 3/,
  );
  assert.equal(calls, 3);
  assert.deepEqual(waits, [10, 20]);
});
