import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { findPublicContentFindings, findRepositoryCurrentFindings } from "./check-public-safety.ts";

test("machine-path scan reports user-profile paths without echoing their value", () => {
  const windowsPath = ["C:", "\\", "Users", "\\", "person", "\\", "repo"].join("");
  const unixPath = ["/home", "/person", "/repo"].join("");
  const macPath = ["/Users", "/person", "/repo"].join("");
  const legacyWindowsPath = ["C:", "\\", "Documents and Settings", "\\", "person", "\\", "repo"].join("");
  const workspacePath = ["E:", "\\", "repos", "\\", "my-projects", "\\", "repo"].join("");
  const harnessPath = ["E:", "\\", "harness-probe-lab"].join("");

  const findings = findPublicContentFindings([
    { path: "docs/windows.md", text: `before\n${windowsPath}\nafter\n` },
    { path: "docs/unix.md", text: `${unixPath}\n` },
    { path: "docs/mac.md", text: `${macPath}\n` },
    { path: "docs/windows-legacy.md", text: `${legacyWindowsPath}\n` },
    { path: "docs/workspace.md", text: `${workspacePath}\n` },
    { path: "docs/harness.md", text: `${harnessPath}\n` },
  ]);

  assert.deepEqual(findings, [
    { path: "docs/harness.md", line: 1, kind: "machine-path" },
    { path: "docs/mac.md", line: 1, kind: "machine-path" },
    { path: "docs/unix.md", line: 1, kind: "machine-path" },
    { path: "docs/windows-legacy.md", line: 1, kind: "machine-path" },
    { path: "docs/windows.md", line: 2, kind: "machine-path" },
    { path: "docs/workspace.md", line: 1, kind: "machine-path" },
  ]);
  assert.doesNotMatch(JSON.stringify(findings), /person|repo/);
});

test("machine-path scan allows only the named synthetic fixture", () => {
  const machinePath = ["C:", "\\", "Users", "\\", "fixture", "\\", "repo"].join("");
  const fixture = "experiments/harness-invocation/tests/fixtures/has-machine-path.txt";

  assert.deepEqual(findPublicContentFindings([{ path: fixture, text: machinePath }]), []);
  assert.deepEqual(findPublicContentFindings([{ path: "docs/copy.md", text: machinePath }]), [
    { path: "docs/copy.md", line: 1, kind: "machine-path" },
  ]);
});

test("repository scan includes untracked files before they are committed", () => {
  const root = mkdtempSync(join(tmpdir(), "public-safety-"));
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  writeFileSync(join(root, "tracked.txt"), "safe\n");
  execFileSync("git", ["add", "tracked.txt"], { cwd: root });
  const email = ["person", "@", "gmail", ".com"].join("");
  const machinePath = ["C:", "\\", "Users", "\\", "person", "\\", "repo"].join("");
  writeFileSync(join(root, "untracked.txt"), `${email}\n${machinePath}\n`);

  assert.deepEqual(findRepositoryCurrentFindings(root), [
    { path: "untracked.txt", line: 1, kind: "gmail-identity" },
    { path: "untracked.txt", line: 2, kind: "machine-path" },
  ]);
});

test("repository scan ignores tracked files deleted in the worktree", () => {
  const root = mkdtempSync(join(tmpdir(), "public-safety-"));
  execFileSync("git", ["init", "-q", "."], { cwd: root });
  writeFileSync(join(root, "removed.txt"), "safe\n");
  execFileSync("git", ["add", "removed.txt"], { cwd: root });
  rmSync(join(root, "removed.txt"));

  assert.deepEqual(findRepositoryCurrentFindings(root), []);
});
