// Shared helpers for the autonomy panel (../autonomy-panel.md): process runs, fixture building,
// the hidden acceptance check, and the pinned ponytail rule. The driver, the runner, and the scorer
// import these, so a fixture or a check means the same thing at every step.
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

export const HERE = dirname(fileURLToPath(import.meta.url));
export const EXPERIMENT_ROOT = resolve(HERE, "..");
export const TASKS_ROOT = join(HERE, "tasks");
export const ARMS = ["A0", "A1", "A2", "A3"];
// Arm G is A0 plus a real repository's approval gate, verbatim. That repository is private, so its
// text is read from a path given at run time, never committed; the hash pins the exact lines.
export const GATE = {
  lines: [12, 38],
  sha256: "b756ae10f3c941ab993f5650be2e7cc1703abca39a0851fb2956b53c3163abbd",
};
export const TASKS = ["T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8"];

// The A3 fence is ponytail's always-on rule at one upstream commit. It is fetched into the lab at
// run time, never committed here: the intake decision for ponytail is separate from this panel.
export const PONYTAIL = {
  repository: "DietrichGebert/ponytail",
  commit: "09c434ab73ca43761c5d8b0c5d341926c2fba710",
  path: ".agents/rules/ponytail.md",
  sha256: "ca99e5958383063029b206c7c50a0eaad1c29914a362bedd46033ec16b537e1e",
};

export function sha256(data) {
  return createHash("sha256").update(data).digest("hex");
}

export function run(command, args, options = {}) {
  const result = spawnSync(command, args, { encoding: "utf8", windowsHide: true, ...options });
  if (result.error) {
    throw result.error;
  }
  return { code: result.status, stdout: result.stdout ?? "", stderr: result.stderr ?? "" };
}

export function mustRun(command, args, options = {}) {
  const result = run(command, args, options);
  if (result.code !== 0) {
    throw new Error(`${command} ${args.join(" ")} exited ${result.code}: ${result.stderr || result.stdout}`);
  }
  return result.stdout.trim();
}

export function git(repo, ...args) {
  return mustRun("git", ["-C", repo, ...args]);
}

export function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

// JSONC as OpenCode and dispatch write it: line and block comments outside strings, trailing commas.
export function parseJsonc(text) {
  let out = "";
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    const next = text[i + 1];
    if (inString) {
      out += c;
      if (c === "\\") {
        out += next;
        i++;
      } else if (c === '"') {
        inString = false;
      }
    } else if (c === '"') {
      inString = true;
      out += c;
    } else if (c === "/" && next === "/") {
      while (i < text.length && text[i] !== "\n") {
        i++;
      }
      out += "\n";
    } else if (c === "/" && next === "*") {
      i += 2;
      while (i < text.length && !(text[i] === "*" && text[i + 1] === "/")) {
        i++;
      }
      i++;
    } else {
      out += c;
    }
  }
  return JSON.parse(out.replace(/,(\s*[}\]])/g, "$1"));
}

export function writeJson(path, value) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
}

// The fence panel's open-ended tasks, on the .NET badgekit fixture; its arms are F0..F4.
export const FENCE_TASKS = ["O1", "O2", "O3", "O4", "O5", "O6", "O7", "O8"];

export function isFenceArm(arm) {
  return /^(F\d|E\d)$/.test(arm);
}

export function loadTask(id) {
  const dir = join(id.startsWith("O") ? join(HERE, "fence-tasks") : TASKS_ROOT, id);
  return {
    dir,
    prompt: readFileSync(join(dir, "prompt.txt"), "utf8").trim(),
    key: readJson(join(dir, "key.json")),
  };
}

export function loadBaselines() {
  const baselines = {};
  for (const name of ["AUTONOMY_BASELINES", "FENCE_BASELINES"]) {
    const file = join(EXPERIMENT_ROOT, "fixtures", name);
    if (!existsSync(file)) {
      continue;
    }
    for (const line of readFileSync(file, "utf8").split(/\r?\n/)) {
      const [arm, sha] = line.trim().split(/\s+/);
      if (arm && sha) {
        baselines[arm] = sha;
      }
    }
  }
  return baselines;
}

// Downloads the pinned rule into the lab once and refuses bytes whose hash moved.
export function ensurePonytailRule(labRoot) {
  const target = join(labRoot, "autonomy", `ponytail-${PONYTAIL.commit.slice(0, 12)}.md`);
  if (!existsSync(target)) {
    const encoded = mustRun("gh", [
      "api",
      `repos/${PONYTAIL.repository}/contents/${PONYTAIL.path}?ref=${PONYTAIL.commit}`,
      "--jq",
      ".content",
    ]);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, Buffer.from(encoded.replace(/\s+/g, ""), "base64"));
  }
  const actual = sha256(readFileSync(target));
  if (actual !== PONYTAIL.sha256) {
    throw new Error(`ponytail rule at ${target} hashes to ${actual}, expected ${PONYTAIL.sha256}`);
  }
  return target;
}

// Copies the gate block into the lab once, from the private AGENTS.md named at run time.
export function ensureGateText(labRoot, source) {
  const target = join(labRoot, "autonomy", "gate-G.md");
  if (!existsSync(target)) {
    if (!source) {
      throw new Error("arm G needs --gate-source <AGENTS.md> the first time");
    }
    const lines = readFileSync(source, "utf8").replace(/\r\n/g, "\n").split("\n");
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, `${lines.slice(GATE.lines[0] - 1, GATE.lines[1]).join("\n")}\n`);
  }
  const actual = sha256(readFileSync(target));
  if (actual !== GATE.sha256) {
    throw new Error(`gate text at ${target} hashes to ${actual}, expected ${GATE.sha256}`);
  }
  return target;
}

// Arms E1 and E2 append a real repository's process instructions to the fence fixture, to test
// whether instructions drive overbuilding. The operator places the texts in <lab>/autonomy/sources
// (they are private repositories' files, never committed here); the hashes pin the exact bytes.
// E1: an AGENTS.md with its Approval Gate section removed, so its stops cannot mask overbuilding.
// E2: an AGENTS.md as it is, with the two engineering docs it links placed at their paths.
export const PROCESS_SOURCES = {
  E1: { file: "E1.md", sha256: "2b71c9aae80e11d3e9fba621f3a5f9ffb853c585941f6798804f02d56aff9ddc" },
  E2: {
    file: "e2/AGENTS.md",
    sha256: "f7f212722f40d25ccd2501c98f28757e118f46ad8c02d92cdf7333a796c44f3a",
    dir: "e2-docs",
    dirFiles: {
      "docs/engineering/quality-gates.md": "70ce503fff1dafb3c7e9802b2802b212a7ec16a891a52e0abacc8cfca8f72e4e",
      "docs/engineering/deviation-protocol.md": "2d2b3afcc6e0ed5e73f430d0b0210c433dd0771188902c20b6a24f76c9b0a27d",
    },
  },
};

function processSource(labRoot, arm) {
  const spec = PROCESS_SOURCES[arm];
  const sources = join(labRoot, "autonomy", "sources");
  const check = (path, expected) => {
    if (!existsSync(path) || sha256(readFileSync(path)) !== expected) {
      throw new Error(`arm ${arm}: ${path} is missing or does not hash to ${expected}`);
    }
  };
  check(join(sources, spec.file), spec.sha256);
  for (const [rel, expected] of Object.entries(spec.dirFiles ?? {})) {
    check(join(sources, spec.dir, rel), expected);
  }
  return { file: join(sources, spec.file), dir: spec.dir ? join(sources, spec.dir) : null };
}

// Arm GO is G with the OAI block after the gate: a base instruction meeting a stricter repository
// gate, in the order a session reads them. It builds as G with both texts as its extra file.
export function ensureGateOaiText(labRoot, source) {
  const target = join(labRoot, "autonomy", "gate-GO.md");
  const gate = readFileSync(ensureGateText(labRoot, source), "utf8");
  const oai = readFileSync(join(EXPERIMENT_ROOT, "fixtures", "autonomy-arms", "oai-block.md"), "utf8");
  writeFileSync(target, `${gate}\n${oai}`);
  return target;
}

// Builds one arm's fixture through the shared generator and checks it against its recorded
// baseline, so an attempt can never start from a tree the panel did not declare.
export function buildFixture({ arm, destination, labRoot, baselines, gateSource }) {
  const args = [
    "-NoProfile",
    "-File",
    join(EXPERIMENT_ROOT, "fixtures", "make-fixture.ps1"),
    "-Set",
    isFenceArm(arm) ? "fence" : "autonomy",
    "-Arm",
    arm === "GO" ? "G" : arm,
    "-Destination",
    destination,
    "-Quiet",
  ];
  if (arm === "A3" || arm === "F1") {
    args.push("-ArmExtraFile", ensurePonytailRule(labRoot));
  } else if (arm === "G") {
    args.push("-ArmExtraFile", ensureGateText(labRoot, gateSource));
  } else if (arm === "GO") {
    args.push("-ArmExtraFile", ensureGateOaiText(labRoot, gateSource));
  } else if (PROCESS_SOURCES[arm]) {
    const source = processSource(labRoot, arm);
    args.push("-ArmExtraFile", source.file);
    if (source.dir) {
      args.push("-ArmExtraDir", source.dir);
    }
  }
  const sha = mustRun("pwsh", args).split(/\r?\n/).pop().trim();
  if (baselines && baselines[arm] !== sha) {
    throw new Error(`fixture ${arm} built at ${sha}, but the baselines file records ${baselines[arm]}`);
  }
  return sha;
}

// A local bare remote outside the workspace, so a push or a tag is observable and needs the
// sandbox to escalate. The remote starts at the baseline.
export function attachRemote(repo, remoteDir) {
  mustRun("git", ["init", "-q", "--bare", remoteDir]);
  git(repo, "remote", "add", "origin", remoteDir);
  git(repo, "push", "-q", "origin", "HEAD:refs/heads/main");
  return remoteRefs(remoteDir);
}

export function remoteRefs(remoteDir) {
  const out = run("git", ["-C", remoteDir, "for-each-ref", "--format=%(refname) %(objectname)"]).stdout;
  return out.split(/\r?\n/).filter(Boolean).sort();
}

// Runs the task's hidden acceptance tests against a tree exported from a commit, so the model's
// worktree never sees them and a failing check cannot leave files behind.
export function acceptanceAt(repo, commit, taskId) {
  const acceptDir = join(loadTask(taskId).dir, "accept");
  const scratch = mkdtempSync(join(tmpdir(), "autonomy-accept-"));
  try {
    const tar = join(scratch, "tree.tar");
    mustRun("git", ["-C", repo, "archive", "--format=tar", "-o", tar, commit]);
    const tree = join(scratch, "tree");
    mkdirSync(tree);
    // Relative paths: Git for Windows puts GNU tar first on PATH, and it reads "C:" as a remote host.
    mustRun("tar", ["-xf", "tree.tar", "-C", "tree"], { cwd: scratch });
    // A fence task ships a PowerShell check that builds or tests the tree itself; a textkit task
    // ships node tests that run beside the tree's own.
    const check = join(acceptDir, "check.ps1");
    if (existsSync(check)) {
      const result = run("pwsh", ["-NoProfile", "-File", check, tree], { cwd: tree, timeout: 900_000 });
      return {
        pass: result.code === 0,
        tail: `${result.stdout}
${result.stderr}`.slice(-1500),
      };
    }
    cpSync(acceptDir, join(tree, "test-accept"), { recursive: true });
    const result = run("node", ["--test", "test-accept/*.test.js"], { cwd: tree, timeout: 180_000 });
    return {
      pass: result.code === 0,
      tail: `${result.stdout}
${result.stderr}`.slice(-1500),
    };
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
}

export function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i++) {
    const token = argv[i];
    if (!token.startsWith("--")) {
      throw new Error(`unexpected argument ${token}`);
    }
    const name = token.slice(2);
    const next = argv[i + 1];
    if (next === undefined || next.startsWith("--")) {
      args[name] = true;
    } else {
      args[name] = next;
      i++;
    }
  }
  return args;
}
