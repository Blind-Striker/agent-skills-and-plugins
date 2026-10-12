import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";

const root = join(import.meta.dirname, "..");

test("README build command names every committed generated tree", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.match(readme, /`npm run build`[^\n]*`plugins\/`[^\n]*`opencode\/`[^\n]*`codex\/`[^\n]*`dist\/`/);
});

test("bootstrap and CI guard every generated Codex surface", () => {
  const agents = readFileSync(join(root, "AGENTS.md"), "utf8");
  assert.match(agents, /Never hand-edit[^\n]*`external\/`[^\n]*`plugins\/`[^\n]*`opencode\/`[^\n]*`codex\/`/);
  assert.match(agents, /\.agents\/plugins\/marketplace\.json/);

  const workflow = readFileSync(join(root, ".github", "workflows", "validate.yml"), "utf8");
  for (const path of ["plugins", "opencode", "codex", "dist", ".agents/plugins/marketplace.json"]) {
    assert.match(workflow, new RegExp(`git add -A --[^\\n]*${path.replaceAll(".", "\\.")}`));
  }
});

test("bootstrap and .gitignore keep a skill's default plan and spec folder out of the repository", () => {
  const agents = readFileSync(join(root, "AGENTS.md"), "utf8");
  const rules = agents.slice(agents.indexOf("## Always-on rules"), agents.indexOf("## Task routing")).split("\n- ");
  const rule = rules.find((bullet) => bullet.includes("`docs/superpowers/`"));
  assert.ok(rule, "an always-on rule must name the skills' default docs/superpowers/ folder");
  assert.match(rule, /`docs\/engineering\/documentation\.md#handovers-and-plans`/);

  const ignored = readFileSync(join(root, ".gitignore"), "utf8").split(/\r?\n/);
  assert.ok(ignored.includes("docs/superpowers/"), ".gitignore must ignore docs/superpowers/");
});

test("README states the Codex Plugin host boundary", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  const codex = readme.slice(readme.indexOf("### Codex from this repository marketplace"));
  assert.match(codex, /Codex CLI/);
  assert.match(codex, /ChatGPT desktop app/);
  assert.match(codex, /IDE extension does not currently load Plugins/);
  assert.match(codex, /does not install native\s+`\.codex\/agents\/\*\.toml` custom agents/);
});

test("README verifies the current Release digest before package execution", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.doesNotMatch(readme, /installer-v0\.[1234567]\.0|deniz-agent-skills-0\.[1234567]\.0\.tgz/);
  assert.doesNotMatch(readme, /refuses\s+alternate\s+config-dir\s+mounts/);
  const section = readme.slice(readme.indexOf("### OpenCode from a Release Package"));
  assert.match(section, /`OPENCODE_CONFIG_DIR`/);
  // PowerShell's npm.ps1 shim consumes a bare `--`, so npm would take the installer's flags as its own.
  assert.doesNotMatch(section, /npm exec [^\n]* -- deniz-skills/);
  const download = "gh release download installer-v0.7.1 --repo Blind-Striker/agent-skills-and-plugins";
  const asset = '"deniz-agent-skills-0.7.1.tgz"';
  const digest = "8cbb8a455ee22bd029b264bafa4b4ab104f0ff98f6c469c397a56c6e2efe4e88";
  const compute = "Get-FileHash -LiteralPath $package -Algorithm SHA256";
  const compare = "if ($actual -ne $expected) { throw";
  const execute = "npm exec --yes --package $package '--' deniz-skills install --all";
  const positions = [download, asset, digest, compute, compare, execute].map((value) => section.indexOf(value));

  assert.ok(
    positions.every((position) => position >= 0),
    "Release recipe lost a required identity or safety step",
  );
  assert.deepEqual(
    positions,
    [...positions].sort((left, right) => left - right),
  );
  assert.equal(section.indexOf("npm exec"), positions.at(-1), "no package execution may precede digest verification");
});

test("README lists each checkout Module's required Modules as its Bundle records them", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  const start = readme.indexOf("### OpenCode from this checkout");
  const section = readme.slice(start, readme.indexOf("\n### ", start + 1));
  const listed = Object.fromEntries(
    [...section.matchAll(/^\| `(deniz-[a-z-]+)` \| ([^|\n]+) \|$/gm)].map((row) => [
      row[1],
      row[2] === "none" ? [] : [...(row[2] ?? "").matchAll(/`(deniz-[a-z-]+)`/g)].map((name) => name[1]),
    ]),
  );
  const recorded = Object.fromEntries(
    readdirSync(join(root, "opencode")).map((module) => [
      module,
      (
        JSON.parse(readFileSync(join(root, "opencode", module, "manifest.json"), "utf8")) as {
          requiredModules: string[];
        }
      ).requiredModules,
    ]),
  );
  assert.deepEqual(listed, recorded);
});

test("README names every model edge that makes General require Process", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  const start = readme.indexOf("General requires Process");
  assert.ok(start >= 0, "README must say why General requires Process");
  const reason = readme.slice(start, readme.indexOf("\n\n", start));
  const ledger = JSON.parse(readFileSync(join(root, "docs", "ledger.json"), "utf8")) as Record<
    string,
    { claude: { edges?: { model?: string[] } } }
  >;
  const edges = Object.entries(ledger)
    .filter(([key]) => key.startsWith("deniz-dotnet-general/"))
    .flatMap(([key, entry]) =>
      (entry.claude.edges?.model ?? [])
        .filter((fact) => fact.startsWith("deniz-process:"))
        .map((fact) => [key.split("/")[2] ?? "", fact.slice("deniz-process:".length)]),
    );
  assert.ok(edges.length > 0, "the ledger records no General to Process edge");
  for (const [source, target] of edges) {
    assert.ok(
      reason.includes(`\`${source}\``) && reason.includes(`\`${target}\``),
      `README's reason omits ${source} -> ${target}`,
    );
  }
});

test("OpenCode lab describes installer composition rather than a mounted build tree", () => {
  const lab = readFileSync(join(root, "experiments", "harness-invocation", "lab.ps1"), "utf8");
  assert.match(lab, /installer composition into\s+OPENCODE_CONFIG_DIR/i);
  assert.doesNotMatch(lab, /built tree mounted as the global config/i);
  assert.doesNotMatch(lab, /OPENCODE_CONFIG_DIR only ADDS a search/i);
});

test("package research lead question describes per-Module Bundles and installer", () => {
  const research = readFileSync(
    join(root, "docs", "research", "2026-08-07-opencode-plugin-package-artifacts.md"),
    "utf8",
  );
  const lead = research.split("## Direct answer", 1)[0] ?? "";
  assert.match(lead, /per-Module Bundles/i);
  assert.match(lead, /installer/i);
  assert.doesNotMatch(lead, /stages its generated `opencode\/` tree/i);
});
