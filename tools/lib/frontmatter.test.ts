import assert from "node:assert/strict";
import { test } from "node:test";
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

test("parseDoc reads an upstream value holding an unquoted ': ' as a string, and serializeDoc quotes it", () => {
  const text =
    "---\nname: factory\ndescription: Shared language for a factory: the tasks it runs.\ndisable-model-invocation: true\n---\n\nBody\n";
  const doc = parseDoc(text);
  assert.deepEqual(doc.frontmatter, {
    name: "factory",
    description: "Shared language for a factory: the tasks it runs.",
    "disable-model-invocation": true,
  });
  assert.deepEqual(parseDoc(serializeDoc(doc)).frontmatter, doc.frontmatter);
  assert.throws(() => parseDoc(text, { strict: true }), /Nested mappings/);
});

test("serializeDoc round-trips", () => {
  const out = serializeDoc({ frontmatter: { name: "x", description: "d" }, body: "B\n" });
  assert.equal(out, "---\nname: x\ndescription: d\n---\n\nB\n");
  assert.deepEqual(parseDoc(out).frontmatter, { name: "x", description: "d" });
});
