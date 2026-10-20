import assert from "node:assert/strict";
import { test } from "node:test";
import { parseHost, parsePort } from "../src/net.js";

test("parseHost accepts and lowercases host names", () => {
  assert.equal(parseHost("Example.COM"), "example.com");
  assert.equal(parseHost("localhost"), "localhost");
});

test("parseHost rejects other text", () => {
  assert.throws(() => parseHost("bad host"), TypeError);
  assert.throws(() => parseHost(""), TypeError);
});

test("parsePort reads a port", () => {
  assert.equal(parsePort("8080"), 8080);
});
