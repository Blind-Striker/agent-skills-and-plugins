import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
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
  assert.doesNotMatch(readme, /installer-v0\.[123]\.0|deniz-agent-skills-0\.[123]\.0\.tgz/);
  assert.doesNotMatch(readme, /refuses\s+alternate\s+config-dir\s+mounts/);
  const section = readme.slice(readme.indexOf("### OpenCode from a Release Package"));
  assert.match(section, /`OPENCODE_CONFIG_DIR`/);
  // PowerShell's npm.ps1 shim consumes a bare `--`, so npm would take the installer's flags as its own.
  assert.doesNotMatch(section, /npm exec [^\n]* -- deniz-skills/);
  const download = "gh release download installer-v0.4.0 --repo Blind-Striker/agent-skills-and-plugins";
  const asset = '"deniz-agent-skills-0.4.0.tgz"';
  const digest = "5108a3ee3673196891644370bb92f538743cdcafad57db7876d8a40f0bf95dce";
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
