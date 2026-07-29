import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

export function makeRepo(): string {
  const root = mkdtempSync(join(tmpdir(), "build-"));
  // upstream submodule 'sp' with namespace 'superpowers'
  mkdirSync(join(root, "external", "sp", ".claude-plugin"), { recursive: true });
  writeFileSync(join(root, "external", "sp", ".claude-plugin", "plugin.json"), JSON.stringify({ name: "superpowers" }));
  mkdirSync(join(root, "external", "sp", "skills", "alpha"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "alpha", "SKILL.md"),
    "---\nname: alpha\ndescription: Alpha upstream\n---\n\nUse superpowers:beta next.\n",
  );
  mkdirSync(join(root, "external", "sp", "skills", "beta"), { recursive: true });
  writeFileSync(
    join(root, "external", "sp", "skills", "beta", "SKILL.md"),
    "---\nname: beta\ndescription: Beta upstream\n---\n\nBeta body.\n",
  );
  // curation manifest
  mkdirSync(join(root, "curation"), { recursive: true });
  writeFileSync(
    join(root, "curation", "deniz-process.yaml"),
    [
      "plugin:",
      "  name: deniz-process",
      "  description: Process skills",
      "  version: 0.1.0",
      "items:",
      "  - source: sp/skills/alpha",
      "    frontmatter:",
      "      description: Alpha curated",
      "  - source: sp/skills/beta",
      "    as: command",
      "    name: deniz-beta",
      "    body: overlay",
    ].join("\n") + "\n",
  );
  // overlay for beta
  mkdirSync(join(root, "overlays", "deniz-process", "deniz-beta"), { recursive: true });
  writeFileSync(
    join(root, "overlays", "deniz-process", "deniz-beta", "SKILL.md"),
    "---\nname: beta\ndescription: Beta overlay\n---\n\nOverlay body.\n",
  );
  // own skill
  mkdirSync(join(root, "skills", "deniz-process", "my-own"), { recursive: true });
  writeFileSync(
    join(root, "skills", "deniz-process", "my-own", "SKILL.md"),
    "---\nname: my-own\ndescription: Mine\n---\n\nMine.\n",
  );
  return root;
}
