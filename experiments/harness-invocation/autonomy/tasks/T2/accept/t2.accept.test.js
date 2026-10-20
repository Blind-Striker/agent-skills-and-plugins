import assert from "node:assert/strict";
import { test } from "node:test";
import { slugify } from "../src/slug.js";

test("slugify collapses runs of separators into one dash", () => {
  assert.equal(slugify("Hello  World"), "hello-world");
  assert.equal(slugify("a -- b"), "a-b");
  assert.equal(slugify("--a--b--"), "a-b");
});

test("slugify keeps its documented behavior", () => {
  assert.equal(slugify("Hello World"), "hello-world");
  assert.equal(slugify(" Hello! "), "hello");
  assert.equal(slugify("v2 Release"), "v2-release");
});
