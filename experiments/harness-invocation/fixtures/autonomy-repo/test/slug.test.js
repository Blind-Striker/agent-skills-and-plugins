import assert from "node:assert/strict";
import { test } from "node:test";
import { slugify } from "../src/slug.js";

test("slugify lowercases and dashes words", () => {
  assert.equal(slugify("Hello World"), "hello-world");
});

test("slugify trims leading and trailing separators", () => {
  assert.equal(slugify(" Hello! "), "hello");
});
