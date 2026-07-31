import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseDoc } from "./frontmatter.ts";
import type { CurationManifest } from "./manifest.ts";
import { listFiles } from "./overlay.ts";
import { extractRefs, type RefKind } from "./refs.ts";
import { resolveItem } from "./resolve.ts";
import type { ComponentInfo } from "./scan.ts";

/** Skill frontmatter OpenCode recognises (moved here from build.ts — build imports it back). */
export const OPENCODE_SKILL_KEYS = new Set(["name", "description", "license", "compatibility", "metadata"]);

interface HarnessState {
  artifacts: string[];
  edges: Record<RefKind, string[]>;
  dropped?: string[];
  parked?: string[];
}
interface LedgerEntry {
  source: string;
  invocation?: string;
  body?: string;
  dependsOn?: string[];
  description: string;
  claude: HarnessState;
  opencode: HarnessState;
}

function sortedUnique(xs: string[]): string[] {
  return [...new Set(xs)].sort();
}

/** Facts in one built artifact set, filtered to our own output namespaces, spelled as found. */
function edgesIn(files: string[], ownNs: Set<string>): Record<RefKind, string[]> {
  const model: string[] = [];
  const pointer: string[] = [];
  for (const f of files) {
    for (const r of extractRefs(readFileSync(f, "utf8"))) {
      if (ownNs.has(r.ns)) {
        (r.kind === "model" ? model : pointer).push(r.address);
      }
    }
  }
  return { model: sortedUnique(model), pointer: sortedUnique(pointer) };
}

export function writeLedger(root: string, manifests: CurationManifest[], components: ComponentInfo[]): void {
  const ownNs = new Set(manifests.map((m) => m.plugin.name));
  const ledger: Record<string, LedgerEntry> = {};
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { outName, outType, id } = resolveItem(root, m.plugin.name, item, components);
      const claudeDir = join(
        root,
        "plugins",
        m.plugin.name,
        `${outType}s`,
        outType === "skill" ? outName : `${outName}.md`,
      );
      const claudeFiles =
        outType === "skill"
          ? listFiles(claudeDir)
              .filter((f) => f.endsWith(".md"))
              .map((f) => join(claudeDir, f))
          : [claudeDir];
      const ocSkill = join(root, "opencode", "skills", outName, "SKILL.md");
      const ocCommand = join(root, "opencode", "commands", `${outName}.md`);
      const ocAgent = join(root, "opencode", "agents", `${outName}.md`);
      const ocArtifacts = [
        ...(existsSync(ocSkill) ? ["skill"] : []),
        ...(existsSync(ocCommand) ? ["command"] : []),
        ...(existsSync(ocAgent) ? ["agent"] : []),
      ];
      const parkedDir = join(root, "opencode", "skills", outName);
      const parked = !existsSync(ocSkill) && existsSync(parkedDir) ? listFiles(parkedDir) : [];
      const doc = parseDoc(readFileSync(outType === "skill" ? join(claudeDir, "SKILL.md") : claudeDir, "utf8"));
      const claudeEdges = edgesIn(claudeFiles, ownNs);
      // OpenCode text is bare — respell the Claude facts through the known mapping instead of
      // parsing bare words back (ADR-0008: detection never runs on rendered output).
      const respell = (xs: string[]) => xs.map((a) => a.split(":")[1] as string).sort();
      // Mirrors each emitter's drop policy, derived from output alone: an emitted skill keeps the
      // recognised keys; a command/agent keeps description (+ its forced fields).
      const ocDropped = existsSync(ocSkill)
        ? Object.keys(doc.frontmatter)
            .filter((k) => !OPENCODE_SKILL_KEYS.has(k))
            .sort()
        : ocArtifacts.length
          ? Object.keys(doc.frontmatter)
              .filter((k) => k !== "description")
              .sort()
          : [];
      const entry: LedgerEntry = {
        source: item.source,
        ...(item.invocation ? { invocation: item.invocation } : {}),
        ...(item.body ? { body: item.body } : {}),
        ...(item.depends_on ? { dependsOn: [...item.depends_on].sort() } : {}),
        description: String(doc.frontmatter.description ?? ""),
        claude: { artifacts: [outType], edges: claudeEdges },
        opencode: {
          artifacts: ocArtifacts,
          edges: { model: respell(claudeEdges.model), pointer: respell(claudeEdges.pointer) },
          dropped: ocDropped,
          parked,
        },
      };
      ledger[id] = entry;
    }
  }
  const sorted = Object.fromEntries(Object.entries(ledger).sort(([a], [b]) => a.localeCompare(b)));
  mkdirSync(join(root, "docs"), { recursive: true });
  writeFileSync(join(root, "docs", "ledger.json"), `${JSON.stringify(sorted, null, 2)}\n`);
}
