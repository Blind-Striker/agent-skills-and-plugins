// The verified-waves stall check (../autonomy-panel.md#verified-waves-stall-check): one audit on the
// waves fixture with the verified-waves skill attached as the TUI attaches it, under the skill text
// of the last commit ("old") or of the working tree ("new"), on a private `opencode serve` with a lab
// config root and the operator's data root (credentials stay where they are). The driver answers
// every permission prompt once, records a form as a stop, waits until the whole session tree is
// idle, and writes result.json. It spends tokens: run it only on the curator's word.
//
//   node waves.mjs --lab <lab> --variant old|new --run <name> [--route openai/gpt-6.1-sol-fast-1m#high]
import { spawn } from "node:child_process";
import { cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { homedir } from "node:os";
import { join, resolve } from "node:path";
import { git, HERE, mustRun, parseArgs, parseJsonc, run, sha256, writeJson } from "./lib.mjs";

const args = parseArgs(process.argv.slice(2));
const lab = resolve(args.lab ?? "");
const variant = String(args.variant);
if (!["old", "new"].includes(variant) || !args.run) {
  throw new Error("usage: waves.mjs --lab <lab> --variant old|new --run <name> [--route p/m#v]");
}
const [providerID, rest] = String(args.route ?? "openai/gpt-6.1-sol-fast-1m#high").split("/");
const [modelID, modelVariant] = rest.split("#");
const repoRoot = resolve(HERE, "..", "..", "..");
const SKILL = "deniz-process.verified-waves";
const TOKEN = `@${SKILL}`;
// The verified-waves panel's P2 prompt: six modules, so a plan with skeptics and a critic needs
// more children than the default ceiling of six.
const PROMPT = `${TOKEN} Audit every module under src/ for boundary errors: a limit, window, or expiry check that is off by one against the module's own documented contract. Report each defect with file and line.`;
const runDir = join(lab, "autonomy", "waves", args.run);
const QUESTION =
  /\?\s*$|\b(should i|shall i|do you want|would you like|want me to|let me know|onay|devam edeyim mi|ister misin)\b/im;
const LIMIT =
  /agent (total|budget|limit)|ajan (sınırı|bütçesi|toplamı)|budget (is )?(spent|reached|exhausted)|sınır(ı)? doldu|over the (announced )?(agent )?total|exceed(s|ing)? the (agent )?total/i;

// One config root per variant: the lab config with the Bundles, the Sol Fast alias added, and for
// "old" the verified-waves skill of the last commit in place of the working tree's.
function configFor() {
  const dir = join(lab, "autonomy", `vw-${variant}`);
  if (!existsSync(dir)) {
    cpSync(join(lab, "autonomy", "oc-config"), dir, { recursive: true });
    const config = JSON.parse(readFileSync(join(dir, "opencode.jsonc"), "utf8"));
    const real = parseJsonc(readFileSync(join(homedir(), ".config", "opencode", "opencode.jsonc"), "utf8"));
    const alias = real.providers?.[providerID]?.models?.[modelID];
    if (alias) {
      config.providers[providerID] ??= { models: {} };
      config.providers[providerID].models[modelID] = alias;
    }
    writeFileSync(join(dir, "opencode.jsonc"), `${JSON.stringify(config, null, 2)}\n`);
    if (variant === "old") {
      const target = join(dir, "skills", SKILL);
      rmSync(target, { recursive: true, force: true });
      for (const file of git(
        repoRoot,
        "ls-tree",
        "-r",
        "--name-only",
        "HEAD",
        `opencode/deniz-process/skills/${SKILL}`,
      ).split(/\r?\n/)) {
        const out = join(target, file.slice(`opencode/deniz-process/skills/${SKILL}/`.length));
        mkdirSync(resolve(out, ".."), { recursive: true });
        writeFileSync(out, mustRun("git", ["-C", repoRoot, "show", `HEAD:${file}`]));
      }
    }
  }
  return { dir, skillSha256: sha256(readFileSync(join(dir, "skills", SKILL, "SKILL.md"))) };
}

async function startServer(configDir, cwd) {
  const password = sha256(`${Date.now()}-${Math.random()}`);
  const port = 40000 + Math.floor(Math.random() * 20000);
  const env = {
    ...process.env,
    OPENCODE_CONFIG_DIR: configDir,
    OPENCODE_PASSWORD: password,
    OPENCODE_DISABLE_FILEWATCHER: "1",
    TEMP: join(runDir, "tmp"),
    TMP: join(runDir, "tmp"),
    TMPDIR: join(runDir, "tmp"),
  };
  mkdirSync(env.TEMP, { recursive: true });
  const server = spawn("opencode", ["serve", "--hostname", "127.0.0.1", "--port", String(port)], {
    cwd,
    env,
    shell: true,
    windowsHide: true,
    stdio: "ignore",
  });
  const base = `http://127.0.0.1:${port}`;
  const auth = `Basic ${Buffer.from(`opencode:${password}`).toString("base64")}`;
  const api = async (method, path, body) => {
    const response = await fetch(base + path, {
      method,
      headers: { authorization: auth, "content-type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const text = await response.text();
    let data;
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
    if (response.status < 200 || response.status >= 300) {
      throw new Error(`${method} ${path}: HTTP ${response.status} ${String(text).slice(0, 300)}`);
    }
    return data;
  };
  for (let i = 0; i < 90; i++) {
    try {
      await api("GET", "/api/info");
      return { api, stop: () => run("taskkill", ["/pid", String(server.pid), "/T", "/F"]) };
    } catch {
      await new Promise((r) => setTimeout(r, 1000));
    }
  }
  throw new Error("opencode serve did not answer within 90 s");
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function main() {
  if (existsSync(runDir)) {
    throw new Error(`${runDir} exists`);
  }
  mkdirSync(runDir, { recursive: true });
  const { dir: configDir, skillSha256 } = configFor();
  const fixture = join(runDir, "waves-repo");
  const baseline = mustRun("pwsh", [
    "-NoProfile",
    "-File",
    join(HERE, "..", "fixtures", "make-fixture.ps1"),
    "-Set",
    "waves",
    "-Destination",
    fixture,
    "-Quiet",
  ])
    .split(/\r?\n/)
    .pop()
    .trim();
  const expected = readFileSync(join(HERE, "..", "fixtures", "WAVES_BASELINE"), "utf8").trim();
  if (baseline !== expected) {
    throw new Error(`waves fixture built at ${baseline}, expected ${expected}`);
  }
  const record = {
    variant,
    route: `${providerID}/${modelID}#${modelVariant}`,
    skillSha256,
    baseline,
    startedAt: new Date().toISOString(),
    permissions: [],
    forms: [],
  };
  const { api, stop } = await startServer(configDir, fixture);
  const location = `location[directory]=${encodeURIComponent(fixture)}`;
  try {
    for (let i = 0; i < 60; i++) {
      const models = await api("GET", `/api/model?${location}`);
      const list = Array.isArray(models) ? models : (models.data ?? []);
      if (list.some((m) => m.providerID === providerID && m.id === modelID)) {
        break;
      }
      await sleep(2000);
    }
    const session = (
      await api("POST", "/api/session", {
        title: `waves ${args.run}`,
        model: { providerID, id: modelID, variant: modelVariant },
        location: { directory: fixture },
      })
    ).data;
    record.sessionID = session.id;
    await api("POST", `/api/session/${session.id}/prompt`, {
      text: PROMPT,
      skills: [{ id: SKILL, mention: { start: 0, end: TOKEN.length, text: TOKEN } }],
    });

    const tree = async (id) => {
      const out = [];
      for (const child of (await api("GET", `/api/session?parentID=${id}&limit=200`)).data ?? []) {
        out.push(child, ...(await tree(child.id)));
      }
      return out;
    };
    const started = Date.now();
    let idleSince = null;
    let maxLive = 0;
    let end = "timeout";
    while (Date.now() - started < 90 * 60_000) {
      for (const p of (await api("GET", `/api/permission/request?${location}`)).data ?? []) {
        record.permissions.push({ action: p.action, resources: p.resources });
        await api("POST", `/api/session/${p.sessionID}/permission/${p.id}/reply`, { decision: "once" });
      }
      const forms = (await api("GET", `/api/form?${location}`)).data ?? [];
      if (forms.length > 0) {
        record.forms.push(...forms.map((f) => JSON.stringify(f).slice(0, 1500)));
        end = "form";
        break;
      }
      const active = Object.keys((await api("GET", "/api/session/active")).data ?? {});
      const ids = [session.id, ...(await tree(session.id)).map((s) => s.id)];
      const live = ids.filter((id) => active.includes(id));
      maxLive = Math.max(maxLive, live.filter((id) => id !== session.id).length);
      const inbox = ((await api("GET", `/api/session/${session.id}/inbox`)).data ?? []).length;
      if (live.length === 0 && inbox === 0) {
        idleSince ??= Date.now();
        if (Date.now() - idleSince > 30_000) {
          end = "idle";
          break;
        }
      } else {
        idleSince = null;
      }
      await sleep(3000);
    }

    // The root's own record: its last text, and every child it started.
    const children = await tree(session.id);
    const rootMessages = [];
    let cursor = null;
    for (let i = 0; i < 50; i++) {
      const page = await api(
        "GET",
        `/api/session/${session.id}/message?limit=200${cursor ? `&cursor=${encodeURIComponent(cursor)}` : "&order=asc"}`,
      );
      rootMessages.push(...(page.data ?? []));
      cursor = page.cursor?.next;
      if (!cursor || (page.data ?? []).length < 200) {
        break;
      }
    }
    const texts = rootMessages
      .filter((m) => m.type === "assistant")
      .flatMap((m) => (m.content ?? []).filter((c) => c.type === "text").map((c) => c.text));
    const finalText = texts.at(-1) ?? "";
    Object.assign(record, {
      end,
      minutes: Math.round((Date.now() - started) / 6000) / 10,
      children: children.length,
      maxLiveChildren: maxLive,
      subagentCalls: rootMessages.flatMap((m) =>
        (m.content ?? []).filter((c) => c.type === "tool" && c.name === "subagent"),
      ).length,
      endsWithQuestion: QUESTION.test(finalText),
      mentionsAgentLimit: LIMIT.test(texts.join("\n")),
      finalText: finalText.slice(-1500),
      fixtureStatus: git(fixture, "status", "--porcelain"),
      ledgerDirs: existsSync(join(runDir, "tmp", "verified-waves"))
        ? readdirSync(join(runDir, "tmp", "verified-waves"))
        : [],
      cost: (await api("GET", `/api/session/${session.id}`)).data?.cost ?? null,
    });
  } finally {
    record.finishedAt = new Date().toISOString();
    writeJson(join(runDir, "result.json"), record);
    stop();
  }
  console.log(
    JSON.stringify({
      end: record.end,
      children: record.children,
      endsWithQuestion: record.endsWithQuestion,
      mentionsAgentLimit: record.mentionsAgentLimit,
      cost: record.cost,
    }),
  );
}

await main();
