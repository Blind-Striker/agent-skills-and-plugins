import { mkdirSync, mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { stringify as stringifyYaml } from "yaml";
import { serializeDoc } from "./lib/frontmatter.ts";
import { loadLock, lockKey, type OverlayLock, saveLock, stampFiles } from "./lib/overlay.ts";

/** A file or directory inside one OpenCode Module bundle: opencode/<module>/<parts...>. */
export function opencodeModulePath(root: string, module: string, ...parts: string[]): string {
  return join(root, "opencode", module, ...parts);
}

/** An OpenCode artifact at its namespaced Bundle path: skills/<m>.<name>/..., commands|agents/<m>.<name>.md. */
export function opencodeIdPath(
  root: string,
  module: string,
  kind: "skill" | "command" | "agent",
  name: string,
  ...rest: string[]
): string {
  const id = `${module}.${name}`;
  return kind === "skill"
    ? join(root, "opencode", module, "skills", id, ...rest)
    : join(root, "opencode", module, `${kind}s`, `${id}.md`);
}

/** A file or directory inside one generated Codex Plugin: codex/<plugin>/<parts...>. */
export function codexPluginPath(root: string, plugin: string, ...parts: string[]): string {
  return join(root, "codex", plugin, ...parts);
}

export function makeRepo(): string {
  const root = mkdtempSync(join(tmpdir(), "build-"));
  // upstream submodule 'sp' with namespace 'superpowers'
  mkdirSync(join(root, "external", "sp", ".claude-plugin"), { recursive: true });
  writeFileSync(join(root, "external", "sp", ".claude-plugin", "plugin.json"), JSON.stringify({ name: "superpowers" }));
  writeFileSync(join(root, "external", "sp", "LICENSE"), "upstream license\n");
  mkdirSync(join(root, "external", "sp", "skills", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\n\nUse superpowers:beta next.\n",
  );
  mkdirSync(join(root, "external", "sp", "skills", "beta", "references"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "beta", "SKILL.md"),
    "---\nname: beta\ndescription: Beta upstream\n---\n\nBeta body.\n",
  );
  writeFileSync(join(root, "external", "sp", "skills", "beta", "references", "notes.md"), "extra asset\n");
  // a plain passthrough skill that carries a subdirectory — the shape most upstream skills have,
  // and the one every overlay path has to survive
  mkdirSync(join(root, "external", "sp", "skills", "delta", "references"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "delta", "SKILL.md"),
    "---\nname: delta\ndescription: Delta upstream\n---\n\nDelta body.\n",
  );
  writeFileSync(join(root, "external", "sp", "skills", "delta", "references", "notes.md"), "delta note\n");
  mkdirSync(join(root, "external", "sp", "skills", "gamma"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "gamma", "SKILL.md"),
    // long enough that "Far region." sits outside the patch hunk's 3 lines of context, so a test
    // can change upstream there and prove the patch still applies
    [
      "---",
      "name: gamma",
      "description: Gamma upstream",
      "---",
      "",
      "Keep this line.",
      "Replace this line.",
      "Filler A.",
      "Filler B.",
      "Filler C.",
      "Filler D.",
      "Far region.",
      "",
    ].join("\n"),
  );
  // curation manifest
  mkdirSync(join(root, "curation"), { recursive: true });
  writeFileSync(
    join(root, "curation", "attribution.json"),
    `${JSON.stringify(
      {
        sp: {
          name: "Superpowers",
          repository: "https://example.test/superpowers",
          license: "MIT",
          copyright: "Copyright (c) Example Author",
          licenseFile: "LICENSE",
        },
      },
      null,
      2,
    )}\n`,
  );
  writeFileSync(join(root, "LICENSE"), "repository license\n");
  writeFileSync(
    join(root, "package.json"),
    `${JSON.stringify(
      {
        name: "test-skills",
        version: "0.1.0",
        author: { name: "Deniz İrgin", email: "deniz@example.test", url: "https://example.test/deniz" },
        repository: { type: "git", url: "git+https://example.test/repository.git" },
        homepage: "https://example.test/project",
        license: "MIT",
      },
      null,
      2,
    )}\n`,
  );
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    `${[
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    frontmatter:",
      "      description: Alpha curated",
      "      name: sneaky",
      "  - source: sp/skills/beta",
      "    as: command",
      "    name: deniz-beta",
      "    body: overlay",
      // same source emitted a second time, as an agent: covers the agent branches of
      // emitItem (forced name) and emitOpenCode (mode: subagent + dropped-key report)
      "  - source: sp/skills/beta",
      "    as: agent",
      "    name: beta-agent",
      "    frontmatter:",
      "      model: opus",
      "  - source: sp/skills/gamma",
      "    body: patch",
      "  - source: sp/skills/delta",
    ].join("\n")}\n`,
  );
  // full-file overlay for beta, blessed against the upstream content it was written from
  mkdirSync(join(root, "overlays", "deniz-process", "deniz-beta"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "deniz-beta", "SKILL.md"),
    "---\nname: beta\ndescription: Beta overlay\n---\n\nOverlay body.\n",
  );
  const blessed = (item: string, source: string, files: string[]) => ({
    [lockKey("deniz-process", item)]: {
      source,
      files: stampFiles(join(root, "external", ...source.split("/")), files),
    },
  });
  // patch overlay for gamma: item-relative paths so `git apply -p1` lands on the emitted dir
  mkdirSync(join(root, "overlays", "deniz-process", "gamma"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "gamma", "overlay.patch"),
    [
      "diff --git a/SKILL.md b/SKILL.md",
      "--- a/SKILL.md",
      "+++ b/SKILL.md",
      "@@ -4,7 +4,7 @@",
      " ---",
      " ",
      " Keep this line.",
      "-Replace this line.",
      "+Patched line.",
      " Filler A.",
      " Filler B.",
      " Filler C.",
      "",
    ].join("\n"),
  );
  // Both overlay kinds are blessed against the upstream content they were written from, and so is
  // each frontmatter override on an item that owns no body (alpha, beta-agent).
  const frontmatterBlessed = (item: string, source: string): OverlayLock => ({
    [lockKey("deniz-process", item)]: {
      source,
      files: stampFiles(join(root, "external", ...source.split("/")), ["SKILL.md"]),
      frontmatter: true,
    },
  });
  saveLock(root, {
    ...blessed("deniz-beta", "sp/skills/beta", ["SKILL.md"]),
    ...blessed("gamma", "sp/skills/gamma", ["SKILL.md"]),
    ...frontmatterBlessed("alpha", "sp/skills/alpha"),
    ...frontmatterBlessed("beta-agent", "sp/skills/beta"),
  });
  return root;
}

/**
 * Plants an original skill at skills/<plugin>/<name>/ and declares it under `original_skills` in
 * curation/<plugin>.yaml. The declaration is inserted as text, so the rest of the manifest keeps
 * its spelling and a later `.replace()` on it still matches.
 */
export function addOriginalSkill(
  root: string,
  name: string,
  options: {
    plugin?: string;
    declaration?: Record<string, unknown>;
    frontmatter?: Record<string, unknown>;
    body?: string;
    files?: Record<string, string>;
  } = {},
): void {
  const plugin = options.plugin ?? "deniz-process";
  const dir = join(root, "skills", plugin, name);
  mkdirSync(dir, { recursive: true });
  writeFileSync(
    join(dir, "SKILL.md"),
    serializeDoc({
      frontmatter: { name, description: "Mine", ...options.frontmatter },
      body: options.body ?? "Mine.\n",
    }),
  );
  for (const [path, content] of Object.entries(options.files ?? {})) {
    mkdirSync(dirname(join(dir, path)), { recursive: true });
    writeFileSync(join(dir, path), content);
  }

  const manifestPath = join(root, "curation", `${plugin}.yaml`);
  let text = readFileSync(manifestPath, "utf8");
  if (!/^original_skills:$/m.test(text)) {
    if (/^items:/m.test(text)) {
      text = text.replace(/^items:/m, "original_skills:\nitems:");
    } else {
      text = `${text.endsWith("\n") ? text : `${text}\n`}original_skills:\n`;
    }
  }
  const entry = stringifyYaml({ [name]: options.declaration ?? { invocation: "both" } })
    .split("\n")
    .map((line) => (line ? `  ${line}` : line))
    .join("\n");
  writeFileSync(
    manifestPath,
    text.replace(/^original_skills:\n/m, () => `original_skills:\n${entry}`),
  );
}

/**
 * Records every frontmatter stamp in the fixture lock against current upstream, as a reviewed
 * `eject --bless` would, for a test that rewrites an upstream document under such an override.
 */
export function restampFrontmatter(root: string): void {
  const lock = loadLock(root);
  for (const entry of Object.values(lock)) {
    if (entry.frontmatter === true) {
      entry.files = stampFiles(join(root, "external", ...entry.source.split("/")), Object.keys(entry.files));
    }
  }
  saveLock(root, lock);
}

/** delta ships references/tools.md from a different source in each tree (curation/SCHEMA.md harness_files). */
export function makeHarnessFilesRepo(opencode = "references/opencode.md"): string {
  const root = makeRepo();
  const refs = join(root, "external", "sp", "skills", "delta", "references");
  writeFileSync(join(refs, "claude.md"), "# Claude notes\n\n`Agent` calls; see superpowers:gamma.\n");
  writeFileSync(join(refs, "codex.md"), "# Codex notes\n\nUse spawn_agent.\n");
  writeFileSync(join(refs, "opencode.md"), "# OpenCode notes\n\nUse the `subagent` tool.\n");
  const p = join(root, "curation", "deniz-process.yaml");
  writeFileSync(
    p,
    readFileSync(p, "utf8").replace(
      "  - source: sp/skills/delta\n",
      [
        "  - source: sp/skills/delta",
        "    harness_files:",
        "      references/tools.md:",
        "        claude: references/claude.md",
        "        codex: references/codex.md",
        `        opencode: ${opencode}`,
        "",
      ].join("\n"),
    ),
  );
  return root;
}
