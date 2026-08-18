import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { indexModes } from "./git.ts";
import {
  createModuleManifest,
  digestFileMap,
  hashBytes,
  loadModuleBundles,
  loadModuleManifest,
  verifyModuleManifest,
  type FileIdentity,
} from "./opencode-bundle.ts";

test("module digest is stable over sorted path, hash, and mode", () => {
  const files: Record<string, FileIdentity> = {
    "skills/z/run.sh": { sha256: `sha256:${"b".repeat(64)}`, mode: "100755" },
    "commands/a.md": { sha256: `sha256:${"a".repeat(64)}`, mode: "100644" },
  };
  const reversed = Object.fromEntries(Object.entries(files).reverse());
  assert.equal(digestFileMap(files), digestFileMap(reversed));
});

test("module digest uses ordinal path ordering instead of the host locale", () => {
  const upper = { sha256: `sha256:${"a".repeat(64)}` as const, mode: "100644" as const };
  const lower = { sha256: `sha256:${"b".repeat(64)}` as const, mode: "100755" as const };
  const files = {
    "commands/a.md": lower,
    "commands/Z.md": upper,
  };
  const expectedPayload = [
    `commands/Z.md\0${upper.sha256}\0${upper.mode}\n`,
    `commands/a.md\0${lower.sha256}\0${lower.mode}\n`,
  ].join("");

  assert.equal(digestFileMap(files), hashBytes(expectedPayload));
});

test("manifest hashes raw bytes and verification reports tamper and extras", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-"));
  mkdirSync(join(root, "skills", "alpha"), { recursive: true });
  writeFileSync(join(root, "skills", "alpha", "SKILL.md"), Buffer.from([0x41, 0x0d, 0x0a]));
  const manifest = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");
  assert.deepEqual(verifyModuleManifest(root, manifest), []);
  writeFileSync(join(root, "skills", "alpha", "SKILL.md"), "changed\n");
  writeFileSync(join(root, "extra.txt"), "extra\n");
  assert.deepEqual(
    verifyModuleManifest(root, manifest)
      .map((x) => x.code)
      .sort(),
    ["extra_file", "hash_mismatch"],
  );
  assert.doesNotMatch(readFileSync(join(root, "skills", "alpha", "SKILL.md"), "utf8"), /\r/);
});

test("Git index modes are root-relative POSIX executable identities", () => {
  const root = mkdtempSync(join(tmpdir(), "index-modes-"));
  mkdirSync(join(root, "nested"), { recursive: true });
  writeFileSync(join(root, "plain.txt"), "plain\n");
  writeFileSync(join(root, "nested", "run.sh"), "#!/bin/sh\n");
  execFileSync("git", ["init", "--quiet", root]);
  execFileSync("git", ["-C", root, "add", "plain.txt", "nested/run.sh"]);
  execFileSync("git", ["-C", root, "update-index", "--chmod=+x", "nested/run.sh"]);

  assert.deepEqual(
    [...indexModes(root)],
    [
      ["nested/run.sh", "100755"],
      ["plain.txt", "100644"],
    ],
  );
  assert.deepEqual([...indexModes(mkdtempSync(join(tmpdir(), "not-a-repo-")))], []);
});

test("manifest verification reports a listed file that is missing", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-missing-"));
  const file = join(root, "commands", "alpha.md");
  mkdirSync(join(root, "commands"), { recursive: true });
  writeFileSync(file, "alpha\n");
  const manifest = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");
  rmSync(file);

  assert.deepEqual(
    verifyModuleManifest(root, manifest).map((finding) => finding.code),
    ["missing_file"],
  );
});

test("module digest changes when only the executable mode changes", () => {
  const sha256 = `sha256:${"a".repeat(64)}` as const;
  assert.notEqual(
    digestFileMap({ "skills/alpha/run.sh": { sha256, mode: "100644" } }),
    digestFileMap({ "skills/alpha/run.sh": { sha256, mode: "100755" } }),
  );
});

test("manifest creation excludes the root manifest.json from its file set", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-self-"));
  writeFileSync(join(root, "manifest.json"), "old manifest\n");
  mkdirSync(join(root, "commands"), { recursive: true });
  writeFileSync(join(root, "commands", "alpha.md"), "alpha\n");

  const manifest = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");

  assert.deepEqual(Object.keys(manifest.files), ["commands/alpha.md"]);
  assert.deepEqual(verifyModuleManifest(root, manifest), []);
});

test("manifest creation records prototype-named paths as ordinary files", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-prototype-path-"));
  writeFileSync(join(root, "__proto__"), "ordinary file\n");

  const manifest = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");

  assert.ok(Object.hasOwn(manifest.files, "__proto__"));
  assert.deepEqual(verifyModuleManifest(root, manifest), []);
});

test("manifest verification treats inherited object names as unlisted extras", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-inherited-path-"));
  writeFileSync(join(root, "toString"), "extra\n");
  const files: Record<string, FileIdentity> = {};
  const manifest = {
    schemaVersion: 1 as const,
    module: "deniz-process",
    version: "0.2.0",
    digest: digestFileMap(files),
    files,
  };

  assert.deepEqual(
    verifyModuleManifest(root, manifest).map((finding) => finding.code),
    ["extra_file"],
  );
});

test("loaded manifests serialize deterministically regardless of file-map order", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-serialization-"));
  mkdirSync(join(root, "commands"), { recursive: true });
  writeFileSync(join(root, "commands", "zeta.md"), "zeta\n");
  writeFileSync(join(root, "commands", "alpha.md"), "alpha\n");
  const manifest = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");
  const unordered = { ...manifest, files: Object.fromEntries(Object.entries(manifest.files).reverse()) };
  writeFileSync(join(root, "manifest.json"), JSON.stringify(unordered));

  assert.equal(JSON.stringify(loadModuleManifest(join(root, "manifest.json"))), JSON.stringify(manifest));
});

test("loader rejects malformed sha256 values", () => {
  const path = join(mkdtempSync(join(tmpdir(), "bundle-invalid-hash-")), "manifest.json");
  writeFileSync(
    path,
    JSON.stringify({
      schemaVersion: 1,
      module: "deniz-process",
      version: "0.2.0",
      digest: `sha256:${"a".repeat(64)}`,
      files: { "commands/alpha.md": { sha256: "sha256:not-a-hash", mode: "100644" } },
    }),
  );

  assert.throws(() => loadModuleManifest(path), /sha256/);
});

test("loader rejects absolute and traversal file paths", () => {
  const hash = `sha256:${"a".repeat(64)}`;
  const unsafePaths = [
    { name: "absolute", file: "/commands/alpha.md" },
    { name: "traversal", file: "../commands/alpha.md" },
  ] as const;
  for (const { name, file } of unsafePaths) {
    const path = join(mkdtempSync(join(tmpdir(), `bundle-${name}-`)), "manifest.json");
    writeFileSync(
      path,
      JSON.stringify({
        schemaVersion: 1,
        module: "deniz-process",
        version: "0.2.0",
        digest: hash,
        files: { [file]: { sha256: hash, mode: "100644" } },
      }),
    );
    assert.throws(() => loadModuleManifest(path), /root-relative|traversal/);
  }
});

test("case-insensitive verification reports a differently cased file as an alias", () => {
  const root = mkdtempSync(join(tmpdir(), "bundle-case-"));
  mkdirSync(join(root, "skills", "Alpha"), { recursive: true });
  writeFileSync(join(root, "skills", "Alpha", "SKILL.md"), "alpha\n");
  const actual = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");
  const identity = actual.files["skills/Alpha/SKILL.md"];
  assert.ok(identity);
  const files = { "skills/alpha/SKILL.md": identity };
  const manifest = { ...actual, files, digest: digestFileMap(files) };

  assert.deepEqual(
    verifyModuleManifest(root, manifest, { caseInsensitive: true }).map((finding) => finding.code),
    ["case_alias"],
  );
});

test("loader returns direct Module directories by their manifest name", () => {
  const opencodeRoot = mkdtempSync(join(tmpdir(), "module-bundles-"));
  const moduleRoot = join(opencodeRoot, "deniz-process");
  mkdirSync(join(moduleRoot, "commands"), { recursive: true });
  writeFileSync(join(moduleRoot, "commands", "alpha.md"), "alpha\n");
  const manifest = createModuleManifest(moduleRoot, "deniz-process", "0.2.0", () => "100644");
  writeFileSync(join(moduleRoot, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);

  const bundle = loadModuleBundles(opencodeRoot).get("deniz-process");
  assert.ok(bundle);
  assert.equal(bundle.root, moduleRoot);
  assert.deepEqual(bundle.manifest, manifest);
});

test("manifest creation refuses a bundle tree that contains a symlink", (t) => {
  const root = mkdtempSync(join(tmpdir(), "bundle-symlink-create-"));
  writeFileSync(join(root, "plain.txt"), "plain\n");
  try {
    symlinkSync(join(root, "plain.txt"), join(root, "linked.txt"), "file");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }

  assert.throws(() => createModuleManifest(root, "deniz-process", "0.2.0", () => "100644"), /symlink/);
});

test("manifest verification reports an unlisted symlink", (t) => {
  const root = mkdtempSync(join(tmpdir(), "bundle-symlink-verify-"));
  writeFileSync(join(root, "plain.txt"), "plain\n");
  const manifest = createModuleManifest(root, "deniz-process", "0.2.0", () => "100644");
  try {
    symlinkSync(join(root, "plain.txt"), join(root, "linked.txt"), "file");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }

  assert.deepEqual(
    verifyModuleManifest(root, manifest).map((finding) => ({ code: finding.code, path: finding.path })),
    [{ code: "symlink", path: "linked.txt" }],
  );
});

test("loader rejects a Module directory whose manifest has another name", () => {
  const opencodeRoot = mkdtempSync(join(tmpdir(), "module-mismatch-"));
  const moduleRoot = join(opencodeRoot, "deniz-process");
  mkdirSync(moduleRoot, { recursive: true });
  const manifest = createModuleManifest(moduleRoot, "deniz-other", "0.2.0", () => "100644");
  writeFileSync(join(moduleRoot, "manifest.json"), JSON.stringify(manifest));

  assert.throws(() => loadModuleBundles(opencodeRoot), /directory name/);
});
