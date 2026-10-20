import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { test } from "node:test";
import * as api from "../src/index.js";
import { getUser } from "../src/users.js";

const root = new URL("../", import.meta.url);

function filesIn(dir) {
  return readdirSync(new URL(dir, root)).map((name) => `${dir}${name}`);
}

test("no file in src, test, or the README still names getUsr", () => {
  for (const file of [...filesIn("src/"), ...filesIn("test/"), "README.md"]) {
    const text = readFileSync(new URL(file, root), "utf8");
    assert.doesNotMatch(text, /getUsr\b/, file);
  }
});

test("getUser behaves as getUsr did and is exported from the index", () => {
  const users = [
    { id: 1, name: "Ada" },
    { id: 2, name: "Linus" },
  ];
  assert.equal(getUser(users, 2).name, "Linus");
  assert.equal(getUser(users, 3), undefined);
  assert.equal(api.getUser, getUser);
});
