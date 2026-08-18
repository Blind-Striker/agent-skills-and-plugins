import assert from "node:assert/strict";
import { test } from "node:test";
import { digestFileMap, hashBytes, type FileIdentity, type FileMode, type ModuleManifest } from "./opencode-bundle.ts";
import { EMPTY_INSTALL_STATE, type InstallState, type ObservedPath } from "./opencode-install-state.ts";
import { planReconcile, type InstallRequest } from "./opencode-install-plan.ts";

interface Case {
  name: string;
  current: InstallState;
  manifests: Record<string, ModuleManifest>;
  observed: Record<string, ObservedPath>;
  request: InstallRequest;
  operations?: string[];
  findingCodes?: string[];
  transfers?: string[];
  nextMode?: { path: string; mode: FileMode };
  remainingModules?: string[];
}

const F19 = "skills/alpha/f19.md";
const F20 = "skills/alpha/f20.md";
const SCRIPT = "skills/alpha/run.sh";
const SHARED = "commands/shared.md";

function identity(seed: string, mode: FileMode = "100644"): FileIdentity {
  return { sha256: hashBytes(seed), mode };
}

function numbered(count: number): Record<string, FileIdentity> {
  const files = Object.create(null) as Record<string, FileIdentity>;
  for (let index = 0; index < count; index++) {
    files[`skills/alpha/f${String(index).padStart(2, "0")}.md`] = identity(`f${index}`);
  }
  return files;
}

function omit(files: Record<string, FileIdentity>, path: string): Record<string, FileIdentity> {
  const next = { ...files };
  delete next[path];
  return next;
}

function manifest(module: string, files: Record<string, FileIdentity>, version = "1.0.0"): ModuleManifest {
  const sorted = Object.fromEntries(
    Object.entries(files).sort(([left], [right]) => left.localeCompare(right)),
  ) as Record<string, FileIdentity>;
  return {
    schemaVersion: 1,
    module,
    version,
    digest: digestFileMap(sorted),
    files: sorted,
  };
}

function installed(entries: Record<string, { version?: string; files: Record<string, FileIdentity> }>): InstallState {
  const modules = Object.create(null) as InstallState["modules"];
  const files = Object.create(null) as InstallState["files"];
  for (const name of Object.keys(entries).sort((left, right) => left.localeCompare(right))) {
    const entry = entries[name];
    if (!entry) {
      continue;
    }
    const moduleManifest = manifest(name, entry.files, entry.version ?? "1.0.0");
    modules[name] = { version: moduleManifest.version, digest: moduleManifest.digest };
    for (const path of Object.keys(moduleManifest.files).sort((left, right) => left.localeCompare(right))) {
      const file = moduleManifest.files[path];
      if (!file) {
        continue;
      }
      files[path] = { module: name, sha256: file.sha256, mode: file.mode };
    }
  }
  return { schemaVersion: 1, modules, files };
}

function matching(files: Record<string, FileIdentity>): Record<string, ObservedPath> {
  const observed = Object.create(null) as Record<string, ObservedPath>;
  for (const [path, file] of Object.entries(files)) {
    observed[path] = { kind: "file", identity: file };
  }
  return observed;
}

function request(kind: InstallRequest["kind"], extra: Partial<InstallRequest> = {}): InstallRequest {
  return {
    kind,
    modules: extra.modules ?? [],
    all: extra.all ?? false,
    platform: extra.platform ?? "posix",
  };
}

const TWENTY = numbered(20);
const NINETEEN = omit(TWENTY, F19);
const TWENTY_ONE = { ...TWENTY, [F20]: identity("f20") };
const COMMAND = { "commands/alpha.md": identity("alpha") };
const COMMAND_NEW = { "commands/alpha.md": identity("alpha-new") };
const SCRIPT_FILE = { [SCRIPT]: identity("script") };
const SCRIPT_EXEC = { [SCRIPT]: identity("script", "100755") };
const OLD_NAME = { "skills/alpha/old.md": identity("renamed") };
const NEW_NAME = { "skills/alpha/new.md": identity("renamed") };
const OTHER = { "skills/other/SKILL.md": identity("other") };

const cases: Case[] = [
  {
    name: "no-op update when bundle and destination match",
    current: installed({ "deniz-process": { files: TWENTY } }),
    manifests: { "deniz-process": manifest("deniz-process", TWENTY) },
    observed: matching(TWENTY),
    request: request("update"),
  },
  {
    name: "update 20-to-19 removes the stale owned path",
    current: installed({ "deniz-process": { files: TWENTY } }),
    manifests: { "deniz-process": manifest("deniz-process", NINETEEN, "1.1.0") },
    observed: matching(TWENTY),
    request: request("update"),
    operations: [`remove:${F19}`],
  },
  {
    name: "update 20-to-21 adds the new bundle path",
    current: installed({ "deniz-process": { files: TWENTY } }),
    manifests: { "deniz-process": manifest("deniz-process", TWENTY_ONE, "1.1.0") },
    observed: { ...matching(TWENTY), [F20]: { kind: "absent" } },
    request: request("update"),
    operations: [`add:${F20}`],
  },
  {
    name: "content change replaces only the modified path",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: { "deniz-process": manifest("deniz-process", COMMAND_NEW, "1.1.0") },
    observed: matching(COMMAND),
    request: request("update"),
    operations: ["replace:commands/alpha.md"],
  },
  {
    name: "POSIX mode-only change is a chmod",
    current: installed({ "deniz-process": { files: SCRIPT_FILE } }),
    manifests: { "deniz-process": manifest("deniz-process", SCRIPT_EXEC, "1.1.0") },
    observed: matching(SCRIPT_FILE),
    request: request("update"),
    operations: [`chmod:${SCRIPT}`],
    nextMode: { path: SCRIPT, mode: "100755" },
  },
  {
    name: "Windows intended mode advances without chmod",
    current: installed({ "deniz-process": { files: SCRIPT_FILE } }),
    manifests: { "deniz-process": manifest("deniz-process", SCRIPT_EXEC, "1.1.0") },
    observed: matching(SCRIPT_FILE),
    request: request("update", { platform: "windows" }),
    nextMode: { path: SCRIPT, mode: "100755" },
  },
  {
    name: "rename is a remove plus an add",
    current: installed({ "deniz-process": { files: OLD_NAME } }),
    manifests: { "deniz-process": manifest("deniz-process", NEW_NAME, "1.1.0") },
    observed: { ...matching(OLD_NAME), "skills/alpha/new.md": { kind: "absent" } },
    request: request("update"),
    operations: ["add:skills/alpha/new.md", "remove:skills/alpha/old.md"],
  },
  {
    name: "unowned destination file is a collision",
    current: EMPTY_INSTALL_STATE,
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: matching(COMMAND),
    request: request("install", { modules: ["deniz-process"] }),
    findingCodes: ["unowned_collision"],
  },
  {
    name: "local modification blocks update",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: matching(COMMAND_NEW),
    request: request("update"),
    findingCodes: ["local_modification"],
  },
  {
    name: "missing owned path is state drift on update",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: { "commands/alpha.md": { kind: "absent" } },
    request: request("update"),
    findingCodes: ["state_drift"],
  },
  {
    name: "remove drops a missing claim instead of treating it as drift",
    current: installed({ "deniz-process": { files: COMMAND } }),
    observed: { "commands/alpha.md": { kind: "absent" } },
    manifests: {},
    request: request("remove", { modules: ["deniz-process"] }),
    operations: ["drop-missing-claim:commands/alpha.md"],
  },
  {
    name: "local modification blocks remove",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: {},
    observed: matching(COMMAND_NEW),
    request: request("remove", { modules: ["deniz-process"] }),
    findingCodes: ["local_modification"],
  },
  {
    name: "remove ignores state drift in an unrelated module",
    current: installed({
      "deniz-dotnet-general": { files: OTHER },
      "deniz-process": { files: COMMAND },
    }),
    manifests: {},
    observed: {
      ...matching(COMMAND),
      "skills/other/SKILL.md": { kind: "absent" },
    },
    request: request("remove", { modules: ["deniz-process"] }),
    operations: ["remove:commands/alpha.md"],
    remainingModules: ["deniz-dotnet-general"],
  },
  {
    name: "update of a selected module absent from the package",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: {},
    observed: matching(COMMAND),
    request: request("update"),
    findingCodes: ["unknown_module"],
  },
  {
    name: "directory where a file belongs is a type mismatch",
    current: EMPTY_INSTALL_STATE,
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: { "commands/alpha.md": { kind: "directory" } },
    request: request("install", { modules: ["deniz-process"] }),
    findingCodes: ["type_mismatch"],
  },
  {
    name: "regular file in an intermediate directory position is a type mismatch",
    current: EMPTY_INSTALL_STATE,
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: { "commands/alpha.md": { kind: "blocked", path: "commands", actual: "file" } },
    request: request("install", { modules: ["deniz-process"] }),
    findingCodes: ["type_mismatch"],
  },
  {
    name: "install reconciles requested modules only",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: { "deniz-dotnet-general": manifest("deniz-dotnet-general", OTHER) },
    observed: { "skills/other/SKILL.md": { kind: "absent" } },
    request: request("install", { modules: ["deniz-dotnet-general"] }),
    operations: ["add:skills/other/SKILL.md"],
    remainingModules: ["deniz-dotnet-general", "deniz-process"],
  },
  {
    name: "ownership transfers when both owners are affected",
    current: installed({
      "deniz-dotnet-general": { files: OTHER },
      "deniz-process": { files: { [SHARED]: identity("shared"), ...COMMAND } },
    }),
    manifests: {
      "deniz-dotnet-general": manifest("deniz-dotnet-general", { ...OTHER, [SHARED]: identity("shared") }, "1.1.0"),
      "deniz-process": manifest("deniz-process", COMMAND, "1.1.0"),
    },
    observed: matching({ ...COMMAND, ...OTHER, [SHARED]: identity("shared") }),
    request: request("update"),
    transfers: [`${SHARED}:deniz-process->deniz-dotnet-general`],
  },
  {
    name: "ownership collision when only the new owner is affected",
    current: installed({ "deniz-process": { files: { [SHARED]: identity("shared") } } }),
    manifests: { "deniz-dotnet-general": manifest("deniz-dotnet-general", { [SHARED]: identity("shared") }) },
    observed: matching({ [SHARED]: identity("shared") }),
    request: request("install", { modules: ["deniz-dotnet-general"] }),
    findingCodes: ["ownership_collision"],
  },
  {
    name: "two final owners of one path collide",
    current: EMPTY_INSTALL_STATE,
    manifests: {
      "deniz-dotnet-general": manifest("deniz-dotnet-general", { [SHARED]: identity("shared") }),
      "deniz-process": manifest("deniz-process", { [SHARED]: identity("shared") }),
    },
    observed: { [SHARED]: { kind: "absent" } },
    request: request("install", { all: true }),
    findingCodes: ["ownership_collision"],
  },
  {
    name: "Windows content edit is still a local modification",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: matching(COMMAND_NEW),
    request: request("update", { platform: "windows" }),
    findingCodes: ["local_modification"],
  },
  {
    name: "symlink where a file belongs is a type mismatch",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: { "commands/alpha.md": { kind: "link" } },
    request: request("update"),
    findingCodes: ["type_mismatch"],
  },
  {
    name: "omitted observation of an install path is missing_observation",
    current: EMPTY_INSTALL_STATE,
    manifests: { "deniz-process": manifest("deniz-process", COMMAND) },
    observed: {},
    request: request("install", { modules: ["deniz-process"] }),
    findingCodes: ["missing_observation"],
  },
  {
    name: "omitted observation of a removed owned path is missing_observation",
    current: installed({ "deniz-process": { files: COMMAND } }),
    manifests: {},
    observed: {},
    request: request("remove", { modules: ["deniz-process"] }),
    findingCodes: ["missing_observation"],
  },
];

for (const c of cases) {
  test(c.name, () => {
    const plan = planReconcile(c.current, c.manifests, c.observed, c.request);
    assert.deepEqual(
      plan.operations.map((item) => `${item.kind}:${item.path}`),
      c.operations ?? [],
    );
    assert.deepEqual(
      plan.findings.map((item) => item.code),
      c.findingCodes ?? [],
    );
    if ((c.findingCodes?.length ?? 0) > 0) {
      assert.equal(plan.nextState, c.current);
    }
    if (c.transfers) {
      assert.deepEqual(
        plan.transfers.map((item) => `${item.path}:${item.fromModule}->${item.toModule}`),
        c.transfers,
      );
    }
    if (c.nextMode) {
      assert.equal(plan.nextState.files[c.nextMode.path]?.mode, c.nextMode.mode);
    }
    if (c.remainingModules) {
      assert.deepEqual(Object.keys(plan.nextState.modules), c.remainingModules);
    }
  });
}
