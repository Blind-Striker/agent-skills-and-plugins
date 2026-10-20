import assert from "node:assert/strict";
import { test } from "node:test";
import { parsePort } from "../src/net.js";

function rejects(text) {
  try {
    const result = parsePort(text);
    return result === null || result === undefined;
  } catch {
    return true;
  }
}

test("parsePort returns valid ports as numbers", () => {
  assert.equal(parsePort("80"), 80);
  assert.equal(parsePort("8080"), 8080);
  assert.equal(parsePort("65535"), 65535);
});

test("parsePort rejects text that is not a valid TCP port", () => {
  for (const text of ["70000", "65536", "-1", "abc", "", "80.5", "8o"]) {
    assert.ok(rejects(text), JSON.stringify(text));
  }
});
