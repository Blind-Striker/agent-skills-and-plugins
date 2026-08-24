import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { lockKey, saveLock, stampFiles } from "./lib/overlay.ts";

/** A file or directory inside one OpenCode Module bundle: opencode/<module>/<parts...>. */
export function opencodeModulePath(root: string, module: string, ...parts: string[]): string {
  return join(root, "opencode", module, ...parts);
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
  // Both overlay kinds are blessed against the upstream content they were written from.
  saveLock(root, {
    ...blessed("deniz-beta", "sp/skills/beta", ["SKILL.md"]),
    ...blessed("gamma", "sp/skills/gamma", ["SKILL.md"]),
  });
  // own skill
  mkdirSync(join(root, "skills", "deniz-process", "my-own"), { recursive: true });
  writeFileSync(
    join(root, "skills", "deniz-process", "my-own", "SKILL.md"),
    "---\nname: my-own\ndescription: Mine\n---\n\nMine.\n",
  );
  return root;
}
