import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";

const read = (file) => readFileSync(new URL(`../${file}`, import.meta.url), "utf8");

test("package.json is at 1.3.0", () => {
  assert.equal(JSON.parse(read("package.json")).version, "1.3.0");
});

test("CHANGELOG has a 1.3.0 entry with the requested line", () => {
  const changelog = read("CHANGELOG.md");
  const entry = changelog.split(/^## 1\.3\.0.*$/m)[1]?.split(/^## /m)[0] ?? "";
  assert.match(entry, /Collapse repeated separators in slugify/);
});
