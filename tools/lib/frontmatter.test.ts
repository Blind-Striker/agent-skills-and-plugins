import { test } from "node:test";
import assert from "node:assert/strict";
import { parseDoc, serializeDoc } from "./frontmatter.ts";

test("parseDoc splits frontmatter and body", () => {
  const doc = parseDoc("---\nname: foo\ndescription: Bar baz\n---\n\nBody text\n");
  assert.equal(doc.frontmatter.name, "foo");
  assert.equal(doc.frontmatter.description, "Bar baz");
  assert.equal(doc.body, "Body text\n");
});

test("parseDoc handles CRLF line endings", () => {
  const doc = parseDoc("---\r\nname: foo\r\n---\r\nBody");
  assert.equal(doc.frontmatter.name, "foo");
  assert.equal(doc.body, "Body");
});

test("parseDoc without frontmatter returns empty object", () => {
  const doc = parseDoc("Just body");
  assert.deepEqual(doc.frontmatter, {});
  assert.equal(doc.body, "Just body");
});

test("serializeDoc round-trips", () => {
  const out = serializeDoc({ frontmatter: { name: "x", description: "d" }, body: "B\n" });
  assert.equal(out, "---\nname: x\ndescription: d\n---\n\nB\n");
  assert.deepEqual(parseDoc(out).frontmatter, { name: "x", description: "d" });
});
