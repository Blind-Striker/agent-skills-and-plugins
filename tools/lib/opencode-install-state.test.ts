import assert from "node:assert/strict";
import { chmodSync, mkdirSync, mkdtempSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { test } from "node:test";
import { hashBytes } from "./opencode-bundle.ts";
import {
  EMPTY_INSTALL_STATE,
  loadInstallState,
  observePath,
  parseInstallState,
  resolveDestination,
  serializeInstallState,
  stateDigest,
  validateDestinationRoot,
  validateManagedPath,
} from "./opencode-install-state.ts";

const HASH_A = `sha256:${"a".repeat(64)}` as const;
const HASH_B = `sha256:${"b".repeat(64)}` as const;

function owned(module = "deniz-process") {
  return { module, sha256: HASH_A, mode: "100644" as const };
}

test("install state rejects traversal and case aliases", () => {
  const HASH_A = `sha256:${"a".repeat(64)}` as const;
  const HASH_B = `sha256:${"b".repeat(64)}` as const;
  const bad = JSON.stringify({
    schemaVersion: 1,
    modules: { "deniz-process": { version: "0.2.0", digest: HASH_A } },
    files: {
      "skills/Alpha/SKILL.md": { module: "deniz-process", sha256: HASH_A, mode: "100644" },
      "skills/alpha/skill.md": { module: "deniz-process", sha256: HASH_B, mode: "100644" },
      "../escape": { module: "deniz-process", sha256: HASH_A, mode: "100644" },
    },
  });
  assert.throws(() => parseInstallState(bad, { caseInsensitive: true }), /case alias|traversal/);
});

test("OPENCODE_CONFIG_DIR is refused", () => {
  assert.throws(
    () => resolveDestination({ HOME: "/home/test", OPENCODE_CONFIG_DIR: "/tmp/other" }),
    /OPENCODE_CONFIG_DIR/,
  );
});

test("install state serialization is deterministic two-space JSON", () => {
  const raw = JSON.stringify({
    files: {
      "skills/z/SKILL.md": owned(),
      "commands/a.md": owned(),
    },
    modules: {
      "deniz-zeta": { digest: HASH_B, version: "1.0.0" },
      "deniz-process": { digest: HASH_A, version: "0.2.0" },
    },
    schemaVersion: 1,
  });
  const state = parseInstallState(raw, { caseInsensitive: false });
  const serialized = `{
  "schemaVersion": 1,
  "modules": {
    "deniz-process": {
      "version": "0.2.0",
      "digest": "${HASH_A}"
    },
    "deniz-zeta": {
      "version": "1.0.0",
      "digest": "${HASH_B}"
    }
  },
  "files": {
    "commands/a.md": {
      "module": "deniz-process",
      "sha256": "${HASH_A}",
      "mode": "100644"
    },
    "skills/z/SKILL.md": {
      "module": "deniz-process",
      "sha256": "${HASH_A}",
      "mode": "100644"
    }
  }
}
`;
  assert.equal(serializeInstallState(state), serialized);
  assert.equal(stateDigest(state), hashBytes(serialized));
});

test("state digest is stable over sorted Modules and files", () => {
  const first = parseInstallState(
    JSON.stringify({
      schemaVersion: 1,
      modules: { "deniz-process": { version: "0.2.0", digest: HASH_A } },
      files: {
        "skills/z/SKILL.md": owned(),
        "commands/a.md": owned(),
      },
    }),
  );
  const second = parseInstallState(
    JSON.stringify({
      schemaVersion: 1,
      files: {
        "commands/a.md": owned(),
        "skills/z/SKILL.md": owned(),
      },
      modules: { "deniz-process": { digest: HASH_A, version: "0.2.0" } },
    }),
  );
  assert.equal(stateDigest(first), stateDigest(second));
});

test("Install-state serialization uses ordinal Module and path ordering", () => {
  const state = parseInstallState(
    JSON.stringify({
      schemaVersion: 1,
      modules: {
        "deniz-a": { version: "1.0.0", digest: HASH_A },
        "deniz-Z": { version: "1.0.0", digest: HASH_B },
      },
      files: {
        "commands/a.md": owned("deniz-a"),
        "commands/Z.md": { ...owned("deniz-Z"), sha256: HASH_B },
      },
    }),
    { caseInsensitive: false },
  );
  const serialized = serializeInstallState(state);

  assert.ok(serialized.indexOf('"deniz-Z"') < serialized.indexOf('"deniz-a"'));
  assert.ok(serialized.indexOf('"commands/Z.md"') < serialized.indexOf('"commands/a.md"'));
});

test("loadInstallState returns empty state only when install.json is absent", () => {
  const destination = mkdtempSync(join(tmpdir(), "install-state-missing-"));
  assert.equal(loadInstallState(destination), EMPTY_INSTALL_STATE);

  mkdirSync(join(destination, ".deniz-skills"));
  writeFileSync(join(destination, ".deniz-skills", "install.json"), "{");
  assert.throws(() => loadInstallState(destination), /invalid Install state/);
});

test("loadInstallState refuses an unreadable existing install.json instead of returning empty", () => {
  const destination = fixtureRoot("install-dir");
  mkdirSync(join(destination, ".deniz-skills", "install.json"), { recursive: true });
  assert.throws(() => loadInstallState(destination), /unreadable Install state/);
});

test("loadInstallState requires a link-free metadata path and ordinary install.json", (t) => {
  const destination = fixtureRoot("install-state-link-free");
  const deniz = join(destination, ".deniz-skills");
  mkdirSync(deniz);
  const target = join(destination, "outside-install.json");
  writeFileSync(target, JSON.stringify({ schemaVersion: 1, modules: {}, files: {} }));
  try {
    symlinkSync(target, join(deniz, "install.json"), "file");
  } catch (error) {
    t.skip(`creating a file symlink is not permitted: ${error instanceof Error ? error.message : String(error)}`);
    return;
  }

  assert.throws(() => loadInstallState(destination), /ordinary file|symlink|junction/i);
});

test("XDG_CONFIG_HOME takes precedence over HOME", () => {
  assert.equal(
    resolveDestination({ XDG_CONFIG_HOME: join("/xdg", "config"), HOME: join("/home", "test") }),
    join("/xdg", "config", "opencode"),
  );
});

test("HOME falls back to ~/.config/opencode", () => {
  assert.equal(resolveDestination({ HOME: join("/home", "test") }), join("/home", "test", ".config", "opencode"));
});

test("missing HOME is an error when XDG_CONFIG_HOME is unset", () => {
  assert.throws(() => resolveDestination({}), /HOME/);
});

test("empty OPENCODE_CONFIG_DIR is not a refusal", () => {
  assert.equal(
    resolveDestination({ HOME: join("/home", "test"), OPENCODE_CONFIG_DIR: "" }),
    join("/home", "test", ".config", "opencode"),
  );
});

test("install state rejects an unknown schema version", () => {
  assert.throws(
    () =>
      parseInstallState(
        JSON.stringify({
          schemaVersion: 2,
          modules: {},
          files: {},
        }),
      ),
    /schemaVersion/,
  );
});

test("install state rejects a file whose Module is not selected", () => {
  assert.throws(
    () =>
      parseInstallState(
        JSON.stringify({
          schemaVersion: 1,
          modules: { "deniz-process": { version: "0.2.0", digest: HASH_A } },
          files: { "commands/a.md": owned("deniz-other") },
        }),
      ),
    /selected Module/,
  );
});

test("install state rejects duplicate schemaVersion members", () => {
  assert.throws(() => parseInstallState(`{"schemaVersion":1,"schemaVersion":1,"modules":{},"files":{}}`), /duplicate/);
});

test("install state rejects duplicate Module keys", () => {
  assert.throws(
    () =>
      parseInstallState(
        `{"schemaVersion":1,"modules":{"deniz-process":{"version":"0.2.0","digest":"${HASH_A}"},"deniz-process":{"version":"0.3.0","digest":"${HASH_B}"}},"files":{}}`,
      ),
    /duplicate/,
  );
});

test("install state rejects duplicate file path keys", () => {
  assert.throws(
    () =>
      parseInstallState(
        `{"schemaVersion":1,"modules":{"deniz-process":{"version":"0.2.0","digest":"${HASH_A}"}},"files":{"commands/a.md":{"module":"deniz-process","sha256":"${HASH_A}","mode":"100644"},"commands/a.md":{"module":"deniz-process","sha256":"${HASH_B}","mode":"100644"}}}`,
      ),
    /duplicate/,
  );
});

test("install state rejects a path outside the native tree", () => {
  assert.throws(
    () =>
      parseInstallState(
        JSON.stringify({
          schemaVersion: 1,
          modules: { "deniz-process": { version: "0.2.0", digest: HASH_A } },
          files: { "plugins/a.md": owned() },
        }),
      ),
    /skills\/|commands\/|agents\//,
  );
});

function fixtureRoot(label: string): string {
  return mkdtempSync(join(tmpdir(), `${label}-`));
}

function tryLink(target: string, path: string, type: "file" | "dir" | "junction"): boolean {
  try {
    symlinkSync(target, path, type);
    return true;
  } catch {
    return false;
  }
}

test("validateDestinationRoot accepts an ordinary directory", () => {
  const destination = fixtureRoot("dest-dir-ok");
  assert.equal(validateDestinationRoot(destination), resolve(destination));
});

test("validateDestinationRoot rejects a root symlink", (t) => {
  const real = fixtureRoot("dest-root-real");
  const parent = fixtureRoot("dest-root-link-parent");
  const link = join(parent, "opencode");
  if (!tryLink(real, link, "dir") && !tryLink(real, link, "junction")) {
    t.skip("creating a Destination root symlink or junction is not permitted");
    return;
  }
  assert.throws(() => validateDestinationRoot(link), /symlink|junction|link/);
});

test("validateDestinationRoot rejects a dangling root symlink rather than treating it absent", (t) => {
  const parent = fixtureRoot("dest-dangling-parent");
  const link = join(parent, "opencode");
  if (
    !tryLink(join(parent, "missing-target"), link, "dir") &&
    !tryLink(join(parent, "missing-target"), link, "file") &&
    !tryLink(join(parent, "missing-target"), link, "junction")
  ) {
    t.skip("creating a Destination root symlink or junction is not permitted");
    return;
  }
  assert.throws(() => validateDestinationRoot(link), /symlink|junction|link/);
  assert.throws(() => observePath(link, "commands/a.md"), /symlink|junction|link/);
});

test("validateDestinationRoot rejects a root file", () => {
  const destination = join(fixtureRoot("dest-file-parent"), "opencode");
  writeFileSync(destination, "not a directory\n");
  assert.throws(() => validateDestinationRoot(destination), /directory/);
});

test("validateManagedPath rejects a descendant symlink", (t) => {
  const destination = fixtureRoot("dest-symlink");
  mkdirSync(join(destination, "skills"), { recursive: true });
  writeFileSync(join(destination, "skills", "plain.md"), "plain\n");
  if (!tryLink(join(destination, "skills", "plain.md"), join(destination, "skills", "linked.md"), "file")) {
    t.skip("creating file symlinks requires elevated privileges on this platform");
    return;
  }
  assert.throws(() => validateManagedPath(destination, "skills/linked.md"), /symlink|link|reparse/);
});

test("validateManagedPath rejects a descendant junction when the platform can create one", (t) => {
  const destination = fixtureRoot("dest-junction");
  const target = fixtureRoot("dest-junction-target");
  mkdirSync(join(destination, "skills"), { recursive: true });
  if (!tryLink(target, join(destination, "skills", "alpha"), "junction")) {
    t.skip("creating junctions is not permitted on this platform");
    return;
  }
  assert.throws(() => validateManagedPath(destination, "skills/alpha/SKILL.md"), /junction|symlink|link|reparse/);
});

test("observePath hashes a regular file without following a sibling link", () => {
  const destination = fixtureRoot("dest-observe");
  mkdirSync(join(destination, "commands"), { recursive: true });
  const bytes = Buffer.from([0x41, 0x0d, 0x0a]);
  writeFileSync(join(destination, "commands", "alpha.md"), bytes);
  assert.deepEqual(observePath(destination, "commands/alpha.md"), {
    kind: "file",
    identity: { sha256: hashBytes(bytes), mode: "100644" },
  });
});

test("observePath reports a directory where a file path is observed", () => {
  const destination = fixtureRoot("dest-dir");
  mkdirSync(join(destination, "commands", "alpha.md"), { recursive: true });
  assert.deepEqual(observePath(destination, "commands/alpha.md"), { kind: "directory" });
});

test("observePath reports absent for a missing native-tree path", () => {
  const destination = fixtureRoot("dest-absent");
  assert.deepEqual(observePath(destination, "commands/missing.md"), { kind: "absent" });
});

test("observePath reports the regular file that blocks an intermediate directory", () => {
  const destination = fixtureRoot("dest-intermediate-file");
  writeFileSync(join(destination, "commands"), "not a directory\n");

  assert.deepEqual(observePath(destination, "commands/alpha.md"), {
    kind: "blocked",
    path: "commands",
    actual: "file",
  });
});

test("observePath never follows a link", (t) => {
  const destination = fixtureRoot("dest-observe-link");
  mkdirSync(join(destination, "commands"), { recursive: true });
  writeFileSync(join(destination, "commands", "plain.md"), "plain\n");
  if (!tryLink(join(destination, "commands", "plain.md"), join(destination, "commands", "linked.md"), "file")) {
    t.skip("creating file symlinks requires elevated privileges on this platform");
    return;
  }
  assert.deepEqual(observePath(destination, "commands/linked.md"), { kind: "link" });
});

test("validateManagedPath rejects traversal and non-native paths", () => {
  const destination = fixtureRoot("dest-traversal");
  assert.throws(() => validateManagedPath(destination, "../escape"), /traversal|native|skills/);
  assert.throws(() => validateManagedPath(destination, "skills/foo/../../escape"), /traversal|normalization/);
  assert.throws(() => validateManagedPath(destination, "plugins/a.md"), /skills\/|commands\/|agents\//);
});

test("case-insensitive install state rejects aliases even without traversal", () => {
  assert.throws(
    () =>
      parseInstallState(
        JSON.stringify({
          schemaVersion: 1,
          modules: { "deniz-process": { version: "0.2.0", digest: HASH_A } },
          files: {
            "skills/Alpha/SKILL.md": owned(),
            "skills/alpha/skill.md": { module: "deniz-process", sha256: HASH_B, mode: "100644" },
          },
        }),
        { caseInsensitive: true },
      ),
    /case alias/,
  );
});

test("observePath records POSIX executable mode on POSIX hosts", () => {
  const destination = fixtureRoot("dest-mode");
  mkdirSync(join(destination, "skills", "alpha"), { recursive: true });
  const file = join(destination, "skills", "alpha", "run.sh");
  writeFileSync(file, "#!/bin/sh\n");
  chmodSync(file, 0o755);
  const observed = observePath(destination, "skills/alpha/run.sh");
  assert.equal(observed.kind, "file");
  if (observed.kind !== "file") {
    return;
  }
  assert.equal(observed.identity.sha256, hashBytes("#!/bin/sh\n"));
  if (process.platform === "win32") {
    assert.ok(observed.identity.mode === "100644" || observed.identity.mode === "100755");
  } else {
    assert.equal(observed.identity.mode, "100755");
  }
});
