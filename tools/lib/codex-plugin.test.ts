import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import type { AssembledItem } from "./assemble.ts";
import {
  adaptCodexSkillDocument,
  collectCodexEmissionProblems,
  createCodexMarketplace,
  createCodexPluginManifest,
  createCodexSkillAgentManifest,
  resolveCodexRepositoryPath,
  resolveCodexInvocation,
  serializeCodexJson,
  serializeCodexSkillAgentManifest,
  type CodexPublisherMetadata,
} from "./codex-plugin.ts";
import type { CurationManifest } from "./manifest.ts";
import { MAX_SKILL_DESCRIPTION_LENGTH } from "./skill-description.ts";

function manifest(name: string): CurationManifest {
  return { plugin: { name, description: `${name} description`, version: "1.2.3" }, items: [] };
}

function assembled(root: string, plugin: string, outName: string, outType: AssembledItem["outType"]): AssembledItem {
  const dir = join(root, plugin, `${outName}-${outType}`);
  mkdirSync(dir, { recursive: true });
  writeFileSync(join(dir, "SKILL.md"), `---\nname: ${outName}\ndescription: Useful skill\n---\n\nBody.\n`);
  return { plugin, source: `source/${outName}`, sourceType: outType, outName, outType, dir, own: false };
}

test("Codex JSON serialization is canonical for minimal data", () => {
  assert.equal(
    serializeCodexJson({ name: "minimal", skills: "./skills/" }),
    '{\n  "name": "minimal",\n  "skills": "./skills/"\n}\n',
  );
});

test("Codex plugin manifest derives rich metadata without target leakage", () => {
  const publisher: CodexPublisherMetadata = {
    author: { name: "Deniz İrgin", email: "deniz@example.test", url: "https://example.test/deniz" },
    homepage: "https://example.test/project",
    repository: "https://example.test/repository",
    license: "MIT",
  };
  const result = createCodexPluginManifest(manifest("deniz-dotnet-akka"), publisher);

  assert.equal(result.name, "deniz-dotnet-akka");
  assert.equal(result.skills, "./skills/");
  assert.equal(result.interface.displayName, "Deniz .NET Akka");
  assert.equal(result.interface.websiteURL, publisher.homepage);
  assert.deepEqual(result.interface.capabilities, ["Read", "Write"]);
  assert.equal("hooks" in result, false);
  assert.equal("mcpServers" in result, false);
  assert.equal("apps" in result, false);
});

test("Codex marketplace ordering and repository source paths are deterministic", () => {
  const result = createCodexMarketplace([manifest("zeta"), manifest("alpha")]);
  assert.deepEqual(
    result.plugins.map((plugin) => [plugin.name, plugin.source.path]),
    [
      ["alpha", "./codex/alpha"],
      ["zeta", "./codex/zeta"],
    ],
  );
  assert.deepEqual(result.plugins[0]?.policy, { installation: "AVAILABLE", authentication: "ON_INSTALL" });
});

test("Codex marketplace paths must be explicit portable repository children", () => {
  const root = mkdtempSync(join(tmpdir(), "codex-path-"));
  assert.equal(resolveCodexRepositoryPath(root, "./codex/plugin"), join(root, "codex", "plugin"));
  for (const path of ["codex/plugin", "./../escape", "./codex\\plugin", "./", "C:/escape"]) {
    assert.throws(() => resolveCodexRepositoryPath(root, path), /repository-relative|escapes/);
  }
});

test("Codex preflight reports flattened, case-insensitive, and invalid namespaces together", () => {
  const root = mkdtempSync(join(tmpdir(), "codex-identities-"));
  const items = [
    assembled(root, "deniz-process", "same-name", "skill"),
    assembled(root, "deniz-process", "Same-Name", "command"),
    assembled(root, "deniz-process", "con", "agent"),
  ];
  const problems = collectCodexEmissionProblems(root, [manifest("deniz-process")], items);

  assert.ok(problems.some((problem) => problem.includes("lowercase")));
  assert.ok(problems.some((problem) => problem.includes("flattened Codex skill collision")));
  assert.ok(problems.some((problem) => problem.includes("not portable on Windows")));
});

test("Codex preflight enforces the 64-character skill-name limit", () => {
  const root = mkdtempSync(join(tmpdir(), "codex-length-"));
  const name = `a${"b".repeat(64)}`;
  const problems = collectCodexEmissionProblems(
    root,
    [manifest("deniz-process")],
    [assembled(root, "deniz-process", name, "skill")],
  );
  assert.ok(problems.some((problem) => problem.includes("exceeds 64 characters")));
});

test("Codex invocation resolves all manifest values without claiming auto is model-only", () => {
  assert.deepEqual(resolveCodexInvocation("auto"), {
    implicit: "enabled",
    explicit: "available",
    agentManifest: false,
  });
  assert.deepEqual(resolveCodexInvocation("manual"), {
    implicit: "disabled",
    explicit: "available",
    agentManifest: true,
  });
  assert.deepEqual(resolveCodexInvocation("both"), {
    implicit: "enabled",
    explicit: "available",
    agentManifest: false,
  });
  assert.deepEqual(resolveCodexInvocation(undefined), {
    implicit: "target-default",
    explicit: "available",
    agentManifest: false,
  });
});

test("only manual invocation emits quoted Codex agent metadata with implicit selection disabled", () => {
  for (const invocation of ["auto", "both", undefined] as const) {
    assert.equal(
      createCodexSkillAgentManifest("deniz-process", "alpha", "Alpha skill description", invocation),
      undefined,
    );
  }
  const manual = createCodexSkillAgentManifest("deniz-process", "alpha", "Alpha skill description", "manual");
  assert.ok(manual);
  assert.equal(manual.policy.allow_implicit_invocation, false);
  assert.match(manual.interface.default_prompt, /^Use \$deniz-process:alpha/);
  const yaml = serializeCodexSkillAgentManifest(manual);
  assert.match(yaml, /display_name: "Alpha"/);
  assert.match(yaml, /allow_implicit_invocation: false/);
});

test("Codex skill adaptation keeps supported metadata and reports every other key deterministically", () => {
  const result = adaptCodexSkillDocument("renamed", {
    frontmatter: {
      model: "opus",
      description: "Useful",
      name: "source-name",
      "user-invocable": false,
      license: "MIT",
      metadata: { owner: "team" },
    },
    body: "Body.\n",
  });
  assert.deepEqual(result.document.frontmatter, {
    name: "renamed",
    description: "Useful",
    license: "MIT",
    metadata: { owner: "team" },
  });
  assert.deepEqual(result.dropped, ["model", "user-invocable"]);
  assert.equal(result.document.body, "Body.\n");
});

test("Codex skill adaptation never shortens a description; the length rule fails the build instead", () => {
  const long = "x".repeat(MAX_SKILL_DESCRIPTION_LENGTH + 20);
  const result = adaptCodexSkillDocument("bounded", { frontmatter: { description: long }, body: "Body.\n" });
  assert.equal(result.document.frontmatter.description, long);
  assert.deepEqual(Object.keys(result).sort(), ["document", "dropped"]);
});

test("a manual skill's short description ends at a sentence boundary, never mid-word", () => {
  const short = (description: string): string =>
    createCodexSkillAgentManifest("deniz-process", "alpha", description, "manual")?.interface.short_description ?? "";
  assert.equal(
    short("Ask which skill or flow fits your situation. A router over the module's skills and flows."),
    "Ask which skill or flow fits your situation.",
  );
  assert.equal(
    short("Stop. That last message did not land. Re-pitch it so it lands with the reader this time."),
    "Stop. That last message did not land.",
    "as many whole sentences as fit",
  );
  assert.equal(
    short("Stop. That last message did not land: re-pitch it."),
    "Stop. That last message did not land: re-pitch it.",
  );
  assert.equal(
    short("Use the build tool, e.g. Make, to run every target in one place. Second sentence here."),
    "Use the build tool, e.g. Make, to run every target in one place.",
    "an abbreviation before a capital is a boundary only when a sentence fits there",
  );
  const long = "A relentless interview to sharpen a plan or design, which also records decisions as they land.";
  const cut = short(long);
  assert.equal(cut, "A relentless interview to sharpen a plan or design, which also…");
  assert.ok(cut.length <= 64);
  assert.ok(!cut.includes("..."), "no ASCII ellipsis");
});

test("a short description keeps a word that ends exactly where the ellipsis starts", () => {
  const sixtyThree = `${"word ".repeat(12)}abc`; // 63 characters, the last word whole
  assert.equal(sixtyThree.length, 63);
  const label = createCodexSkillAgentManifest("p", "s", `${sixtyThree} and more text`, "manual")?.interface
    .short_description;
  assert.equal(label, `${sixtyThree}…`);
});
