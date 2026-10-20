import assert from "node:assert/strict";
import { test } from "node:test";
import { disableUser } from "../src/admin.js";
import { formatUserLine } from "../src/report.js";
import { getUsr } from "../src/users.js";

const users = () => [
  { id: 1, name: "Ada" },
  { id: 2, name: "Linus" },
];

test("getUsr finds a user by id", () => {
  assert.equal(getUsr(users(), 2).name, "Linus");
  assert.equal(getUsr(users(), 3), undefined);
});

test("formatUserLine renders known and unknown users", () => {
  assert.equal(formatUserLine(users(), 1), "#1 Ada");
  assert.equal(formatUserLine(users(), 9), "#9 (unknown)");
});

test("disableUser marks the user", () => {
  const list = users();
  assert.equal(disableUser(list, 1), true);
  assert.equal(list[0].disabled, true);
  assert.equal(disableUser(list, 9), false);
});
