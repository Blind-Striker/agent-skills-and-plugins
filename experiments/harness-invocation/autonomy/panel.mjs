// Runs the autonomy panel (../autonomy-panel.md) through the dispatch harness of
// opencode-sdk-dotnet-internal, with a raw-prompt policy and a lab-only OpenCode config root.
//
//   node panel.mjs prepare --lab <lab> --dispatch <path to dispatch.cs>
//   node panel.mjs dry     --lab <lab> --dispatch <path>
//   node panel.mjs run     --lab <lab> --dispatch <path> --leg A1:sol --n 3 --run p-a1-sol [--max 4]
//   node panel.mjs score   --lab <lab> [--out <file.md>]
//
// prepare and dry make no model call. run spends tokens: it runs only on the curator's word.
import { spawn } from "node:child_process";
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, statSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { basename, dirname, join, resolve } from "node:path";
import {
  ARMS,
  acceptanceAt,
  attachRemote,
  buildFixture,
  git,
  HERE,
  loadBaselines,
  loadTask,
  mustRun,
  parseArgs,
  parseJsonc,
  readJson,
  remoteRefs,
  run,
  sha256,
  TASKS,
  writeJson,
} from "./lib.mjs";
import { scoreAll } from "./score.mjs";

// Model routes per leg, as dispatch's allow-list spells them. Effort is the variant.
const MODELS = {
  sol: "openai/gpt-6.1-sol-1m#high",
  luna: "openai/gpt-6-luna#high",
};
const POLICY = join(HERE, "dispatch-policy.jsonc");
// The real-config leg runs under the operator's own OpenCode config, to tell a model cause from a
// config cause; its policy escalates the write-capable MCP families that config loads.
const REAL_POLICY = join(HERE, "dispatch-policy-realconfig.jsonc");
// The real config's settings that shape every session and carry no secret: compatibility off,
// Claude compatibility off, the shell, and the Sol 1M model alias. Nothing else crosses.
const MIRRORED = ["plugins", "claude", "shell"];

const [command, ...rest] = process.argv.slice(2);
const args = parseArgs(rest);
const lab = resolve(args.lab ?? "");
const root = join(lab, "autonomy");
const configDir = join(root, "oc-config");
const reposDir = join(root, "repos");
const runsDir = join(root, "runs");
const realConfig = Boolean(args["real-config"]);
const policy = realConfig ? REAL_POLICY : POLICY;

function dispatchEnv() {
  const env = { ...process.env, OPENCODE_CONFIG_DIR: configDir };
  if (realConfig) {
    delete env.OPENCODE_CONFIG_DIR;
  }
  return env;
}

function dispatch(dispatchFile, dispatchArgs, options = {}) {
  return run("dotnet", ["run", dispatchFile, "--", ...dispatchArgs], {
    cwd: dirname(dirname(dirname(dispatchFile))),
    env: dispatchEnv(),
    timeout: 900_000,
    ...options,
  });
}

function treeHash(dir) {
  const files = [];
  const walk = (current, prefix) => {
    for (const entry of readdirSync(current).sort()) {
      const full = join(current, entry);
      const rel = prefix ? `${prefix}/${entry}` : entry;
      if (statSync(full).isDirectory()) {
        walk(full, rel);
      } else {
        files.push(`${rel}\n${sha256(readFileSync(full))}`);
      }
    }
  };
  walk(dir, "");
  return sha256(files.join("\n"));
}

function prepare() {
  if (existsSync(configDir) || existsSync(reposDir)) {
    throw new Error(`${root} is already prepared; move it aside first`);
  }
  // 1. The lab config root: the mirrored settings, then the checkout's four Bundles.
  const real = parseJsonc(readFileSync(join(homedir(), ".config", "opencode", "opencode.jsonc"), "utf8"));
  const config = { $schema: real.$schema };
  for (const key of MIRRORED) {
    if (real[key] !== undefined) {
      config[key] = real[key];
    }
  }
  const sol = real.providers?.openai?.models?.["gpt-6.1-sol-1m"];
  if (!sol) {
    throw new Error("the real config defines no openai gpt-6.1-sol-1m alias to mirror");
  }
  config.providers = { openai: { models: { "gpt-6.1-sol-1m": sol } } };
  mkdirSync(configDir, { recursive: true });
  writeFileSync(join(configDir, "opencode.jsonc"), `${JSON.stringify(config, null, 2)}\n`);
  const repoRoot = resolve(HERE, "..", "..", "..");
  mustRun("npm", ["run", "install:opencode", "--", "install", "--all", "--yes"], {
    cwd: repoRoot,
    env: dispatchEnv(),
    shell: true,
    timeout: 600_000,
  });
  for (const name of ["AGENTS.md", "plugins", "switchboard"]) {
    if (existsSync(join(configDir, name))) {
      throw new Error(`${configDir} unexpectedly holds ${name}`);
    }
  }
  // The Bundles ship two agents of their own; any other agent would be a leak from outside.
  const agents = existsSync(join(configDir, "agents")) ? readdirSync(join(configDir, "agents")) : [];
  const foreign = agents.filter((file) => !file.startsWith("deniz-"));
  if (foreign.length > 0) {
    throw new Error(`${configDir} holds agents the Bundles do not ship: ${foreign.join(", ")}`);
  }

  // 2. One fixture repository per arm, each with a bare remote at its baseline.
  const baselines = loadBaselines();
  const repos = {};
  for (const arm of ARMS) {
    const repo = join(reposDir, arm);
    const baseline = buildFixture({ arm, destination: repo, labRoot: lab, baselines });
    const remote = join(reposDir, `${arm}-remote.git`);
    repos[arm] = { repo, baseline, remote, remoteRefs: attachRemote(repo, remote) };
  }
  const manifest = {
    preparedAt: new Date().toISOString(),
    repoHead: git(repoRoot, "rev-parse", "--short", "HEAD"),
    repoDirty: git(repoRoot, "status", "--porcelain") !== "",
    configDir,
    configTreeSha256: treeHash(configDir),
    opencodeVersion: run("opencode", ["--version"], { env: dispatchEnv(), shell: true }).stdout.trim(),
    repos,
  };
  writeJson(join(root, "prepared.json"), manifest);
  console.log(JSON.stringify(manifest, null, 2));
}

function prepared() {
  const manifest = readJson(join(root, "prepared.json"));
  const now = treeHash(configDir);
  if (now !== manifest.configTreeSha256) {
    throw new Error(`the lab config root changed since prepare (${now})`);
  }
  return manifest;
}

// One task file per leg: every task, n attempts each, interleaved, on the arm's repository.
function taskFile(manifest, arm, model, n, runName) {
  const { repo, baseline } = manifest.repos[arm];
  const tasks = [];
  const map = {};
  for (let attempt = 1; attempt <= n; attempt++) {
    for (const id of TASKS) {
      const { prompt } = loadTask(id);
      const taskId = `${runName}-${id.toLowerCase()}-${String(attempt).padStart(2, "0")}`;
      tasks.push({
        id: taskId,
        kind: "code",
        base: baseline,
        route: MODELS[model],
        prompt,
        // Dispatch refuses code tasks with overlapping scopes, as if they shared a worktree; every
        // attempt has its own worktree and branch here. A raw prompt never shows the scope, so a
        // placeholder only turns off dispatch's settle-time flag; score.mjs measures scope itself.
        scope: [`.autonomy-scope/${taskId}/`],
        verify: ["npm test"],
        maxMinutes: 30,
      });
      map[taskId] = {
        arm,
        model,
        route: MODELS[model],
        config: realConfig ? "real" : "lab",
        task: id,
        attempt,
        repo,
        baseline,
      };
    }
  }
  const file = join(runsDir, runName, "tasks.jsonc");
  writeJson(file, { repo, maxParallel: Number(args.max ?? 4), tasks });
  writeJson(join(runsDir, runName, "map.json"), map);
  return file;
}

function dry() {
  const manifest = prepared();
  const dispatchFile = resolve(args.dispatch);
  const report = { selfcheck: null, plans: {}, acceptanceAtBaseline: {} };
  const self = dispatch(dispatchFile, ["selfcheck"]);
  report.selfcheck = self.stdout.split(/\r?\n/).filter((line) => /^(FAIL|PASS\s+(mcp|agents|server))/.test(line));
  for (const arm of ARMS) {
    for (const model of Object.keys(MODELS)) {
      const name = `dry-${arm.toLowerCase()}-${model}`;
      const file = taskFile(manifest, arm, model, 1, name);
      const plan = dispatch(dispatchFile, ["plan", file, "--policy", POLICY]);
      report.plans[name] = { code: plan.code, tail: `${plan.stdout}\n${plan.stderr}`.trim().split(/\r?\n/).slice(-3) };
    }
    for (const id of TASKS) {
      report.acceptanceAtBaseline[`${arm}/${id}`] = acceptanceAt(
        manifest.repos[arm].repo,
        manifest.repos[arm].baseline,
        id,
      );
    }
  }
  writeJson(join(root, "dry.json"), report);
  console.log(JSON.stringify(report, null, 2));
}

// Rejects every escalated ask of the run as it appears, with dispatch's default message.
function startAskRejecter(dispatchFile, runName, ledgerOut) {
  const wtRoot = process.env.DISPATCH_WT_ROOT || "C:\\wt";
  const asks = () => {
    const runs = existsSync(join(wtRoot, ".dispatch")) ? readdirSync(join(wtRoot, ".dispatch")) : [];
    const dirs = runs
      .filter((name) => name.endsWith(`-${runName}`))
      .map((name) => join(wtRoot, ".dispatch", name, "asks"));
    return dirs.flatMap((dir) =>
      existsSync(dir)
        ? readdirSync(dir)
            .filter((file) => file.endsWith(".json"))
            .map((file) => ({ runId: basename(dirname(dir)), askId: file.slice(0, -5) }))
        : [],
    );
  };
  const answered = new Set();
  const timer = setInterval(() => {
    for (const { runId, askId } of asks()) {
      if (answered.has(askId)) {
        continue;
      }
      answered.add(askId);
      const reply = dispatch(dispatchFile, ["reply", runId, askId, "reject"]);
      writeFileSync(
        ledgerOut,
        `${JSON.stringify({ at: new Date().toISOString(), runId, askId, code: reply.code })}\n`,
        {
          flag: "a",
        },
      );
    }
  }, 5_000);
  return () => clearInterval(timer);
}

async function runLeg() {
  const manifest = prepared();
  const dispatchFile = resolve(args.dispatch);
  const [arm, model] = String(args.leg).split(":");
  if (!manifest.repos[arm] || !MODELS[model]) {
    throw new Error(`--leg must be <arm prepared in the lab>:<${Object.keys(MODELS).join("|")}>`);
  }
  const runName = String(args.run);
  if (existsSync(join(runsDir, runName))) {
    throw new Error(`run ${runName} already exists below ${runsDir}`);
  }
  const file = taskFile(manifest, arm, model, Number(args.n), runName);
  const plan = dispatch(dispatchFile, ["plan", file, "--policy", policy]);
  if (plan.code !== 0) {
    throw new Error(`plan refused ${file}:\n${plan.stdout}\n${plan.stderr}`);
  }
  const stopRejecter = startAskRejecter(dispatchFile, runName, join(runsDir, runName, "rejected.jsonl"));
  const code = await new Promise((resolvePromise) => {
    const child = spawn(
      "dotnet",
      ["run", dispatchFile, "--", "run", file, "--run", runName, "--max", String(args.max ?? 4), "--policy", policy],
      { cwd: dirname(dirname(dirname(dispatchFile))), env: dispatchEnv(), stdio: ["ignore", "inherit", "inherit"] },
    );
    child.on("close", resolvePromise);
  });
  stopRejecter();

  // dispatch writes its ledger into the internal repository; the panel's ledgers live in the lab.
  const ledgerRoot = join(dirname(dirname(dirname(dispatchFile))), "ledgers", "dispatch");
  for (const name of readdirSync(ledgerRoot).filter((entry) => entry.endsWith(`-${runName}`))) {
    renameSync(join(ledgerRoot, name), join(runsDir, runName, "ledger"));
  }
  const { remote } = manifest.repos[arm];
  writeJson(join(runsDir, runName, "after.json"), {
    exitCode: code,
    remoteRefs: remoteRefs(remote),
    remoteMoved: JSON.stringify(remoteRefs(remote)) !== JSON.stringify(manifest.repos[arm].remoteRefs),
    tags: git(manifest.repos[arm].repo, "tag", "--list").split(/\r?\n/).filter(Boolean),
    config: realConfig ? "real" : "lab",
    configTreeSha256: treeHash(configDir),
  });
  console.log(`${runName}: dispatch exited ${code}; ledger moved to ${join(runsDir, runName, "ledger")}`);
}

// Adds an arm that prepare does not build, such as G, to an already prepared lab.
function addArm() {
  const manifest = prepared();
  const arm = String(args.arm);
  if (manifest.repos[arm]) {
    throw new Error(`arm ${arm} is already prepared`);
  }
  const repo = join(reposDir, arm);
  const baseline = buildFixture({
    arm,
    destination: repo,
    labRoot: lab,
    baselines: loadBaselines(),
    gateSource: args["gate-source"],
  });
  const remote = join(reposDir, `${arm}-remote.git`);
  manifest.repos[arm] = { repo, baseline, remote, remoteRefs: attachRemote(repo, remote) };
  writeJson(join(root, "prepared.json"), manifest);
  console.log(`${arm}: ${baseline}`);
}

switch (command) {
  case "prepare":
    prepare();
    break;
  case "add-arm":
    addArm();
    break;
  case "dry":
    dry();
    break;
  case "run":
    await runLeg();
    break;
  case "score":
    console.log(scoreAll({ runsDir, out: args.out ? resolve(args.out) : null }));
    break;
  default:
    throw new Error("usage: panel.mjs prepare|dry|run|score --lab <lab> [...]");
}
