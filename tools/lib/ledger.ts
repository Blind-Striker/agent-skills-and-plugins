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
  flags?: {
    "user-invocable"?: boolean;
    "disable-model-invocation"?: boolean;
  };
  dropped?: string[];
  parked?: string[];
}
interface LedgerEntry {
  source: string;
  invocation?: string;
  body?: string;
  mergedFrom?: string[];
  dependsOn?: string[];
  description: string;
  claude: HarnessState;
  opencode: HarnessState;
}

function sortedUnique(xs: string[]): string[] {
  return [...new Set(xs)].sort();
}

function emittedClaudeFlags(outType: string, frontmatter: Record<string, unknown>): HarnessState["flags"] {
  if (outType !== "skill") {
    return undefined;
  }
  const flags: NonNullable<HarnessState["flags"]> = {};
  for (const key of ["user-invocable", "disable-model-invocation"] as const) {
    const value = frontmatter[key];
    if (typeof value === "boolean") {
      flags[key] = value;
    }
  }
  return Object.keys(flags).length > 0 ? flags : undefined;
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
      const { outName, outType } = resolveItem(root, m.plugin.name, item, components);
      const ledgerId = `${m.plugin.name}/${outType}/${outName}`;
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
      const moduleRoot = join(root, "opencode", m.plugin.name);
      const ocSkill = join(moduleRoot, "skills", outName, "SKILL.md");
      const ocCommand = join(moduleRoot, "commands", `${outName}.md`);
      const ocAgent = join(moduleRoot, "agents", `${outName}.md`);
      const ocArtifacts = [
        ...(existsSync(ocSkill) ? ["skill"] : []),
        ...(existsSync(ocCommand) ? ["command"] : []),
        ...(existsSync(ocAgent) ? ["agent"] : []),
      ];
      const parkedDir = join(moduleRoot, "skills", outName);
      const parked = !existsSync(ocSkill) && existsSync(parkedDir) ? listFiles(parkedDir) : [];
      const doc = parseDoc(readFileSync(outType === "skill" ? join(claudeDir, "SKILL.md") : claudeDir, "utf8"));
      const claudeFlags = emittedClaudeFlags(outType, doc.frontmatter);
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
        // Addresses only: which files a merge drew from is a guard detail, and the lock's
        // `mergeSources` map is already the review surface for it.
        ...(item.merged_from ? { mergedFrom: item.merged_from.map((ms) => ms.source).sort() } : {}),
        ...(item.depends_on ? { dependsOn: [...item.depends_on].sort() } : {}),
        description: String(doc.frontmatter.description ?? ""),
        claude: { artifacts: [outType], edges: claudeEdges, ...(claudeFlags ? { flags: claudeFlags } : {}) },
        opencode: {
          artifacts: ocArtifacts,
          edges: { model: respell(claudeEdges.model), pointer: respell(claudeEdges.pointer) },
          dropped: ocDropped,
          parked,
        },
      };
      ledger[ledgerId] = entry;
    }
  }
  const sorted = Object.fromEntries(Object.entries(ledger).sort(([a], [b]) => a.localeCompare(b)));
  mkdirSync(join(root, "docs"), { recursive: true });
  writeFileSync(join(root, "docs", "ledger.json"), `${JSON.stringify(sorted, null, 2)}\n`);
}
