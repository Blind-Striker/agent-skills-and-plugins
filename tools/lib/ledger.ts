import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { AssembledItem } from "./assemble.ts";
import { adaptCodexSkillDocument, resolveCodexInvocation } from "./codex-plugin.ts";
import { parseDoc } from "./frontmatter.ts";
import type { ComponentType, CurationManifest } from "./manifest.ts";
import {
  adaptOpenCodeAgentDocument,
  adaptOpenCodeCommandDocument,
  adaptOpenCodeSkillDocument,
  openCodeBundlePath,
  openCodeId,
} from "./opencode-target.ts";
import { listFiles } from "./overlay.ts";
import { ownSkillIdentities } from "./own-skills.ts";
import { extractRefs, type RefKind } from "./refs.ts";
import { resolveItem } from "./resolve.ts";
import type { ComponentInfo } from "./scan.ts";

interface HarnessState {
  artifacts: string[];
  edges: Record<RefKind, string[]>;
  flags?: {
    "user-invocable"?: boolean;
    "disable-model-invocation"?: boolean;
  };
}
/** OpenCode 2 projection: one artifact per item at its `<plugin>.<name>` ID path (spec section 8). */
interface OpenCodeState {
  artifacts: ComponentType[];
  identity: string;
  /** Skills only: false when the emitted document carries the hide key. */
  advertised?: boolean;
  edges: Record<RefKind, string[]>;
  dropped: string[];
  metadataTransformations?: string[];
}
interface LedgerEntry {
  source: string;
  invocation?: string;
  body?: string;
  mergedFrom?: string[];
  dependsOn?: string[];
  description: string;
  claude: HarnessState;
  opencode: OpenCodeState;
  codex: {
    artifacts: ["skill"];
    identity: string;
    sourceKind: string;
    resolvedKind: string;
    emittedKind: "skill";
    kindTransformation?: string;
    implicit: "enabled" | "disabled" | "target-default";
    explicit: "available";
    policyFiles: string[];
    edges: Record<RefKind, string[]>;
    dropped: string[];
    metadataTransformations?: string[];
    bodyTransformations: string[];
  };
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

export function writeLedger(
  root: string,
  manifests: CurationManifest[],
  components: ComponentInfo[],
  assembled: AssembledItem[],
  codexMetadataTransformations: Map<string, string[]> = new Map(),
): void {
  const ownNs = new Set(manifests.map((m) => m.plugin.name));
  // Claude address `<plugin>:<name>` -> resolved output kind, over every curated item and original
  // skill, so an edge's OpenCode pointer prefix comes from the target's kind, not from parsing output.
  const kindOf = new Map<string, ComponentType>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (!item.exclude) {
        const { outName, outType } = resolveItem(root, m.plugin.name, item, components);
        kindOf.set(`${m.plugin.name}:${outName}`, outType);
      }
    }
  }
  for (const own of ownSkillIdentities(root, manifests)) {
    kindOf.set(own.address, "skill");
  }
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
      const ocArtifacts = (["skill", "command", "agent"] as const).filter((kind) => {
        const path = join(moduleRoot, openCodeBundlePath(kind, m.plugin.name, outName));
        return existsSync(kind === "skill" ? join(path, "SKILL.md") : path);
      });
      const doc = parseDoc(readFileSync(outType === "skill" ? join(claudeDir, "SKILL.md") : claudeDir, "utf8"));
      const neutral = assembled.find(
        (candidate) =>
          candidate.plugin === m.plugin.name &&
          candidate.source === item.source &&
          candidate.outName === outName &&
          candidate.outType === outType,
      );
      if (!neutral) {
        throw new Error(`internal error: missing assembled item for ${ledgerId}`);
      }
      const neutralDoc = parseDoc(readFileSync(join(neutral.dir, "SKILL.md"), "utf8"));
      const codexAdaptation = adaptCodexSkillDocument(outName, neutralDoc);
      const codexInvocation = resolveCodexInvocation(item.invocation);
      const claudeFlags = emittedClaudeFlags(outType, doc.frontmatter);
      const claudeEdges = edgesIn(claudeFiles, ownNs);
      // OpenCode text is dotted — respell the Claude facts through the known mapping instead of
      // parsing rendered text back (ADR-0008: detection never runs on rendered output).
      const ocId = (address: string) => {
        const [plugin, name] = address.split(":") as [string, string];
        return openCodeId(plugin, name);
      };
      const ocPointer = (address: string) => `${kindOf.get(address) === "command" ? "/" : "@"}${ocId(address)}`;
      // The emitter's own adaptation on the same neutral document, so drops and the hide decision
      // cannot drift from what was written.
      const identity = openCodeId(m.plugin.name, outName);
      const ocSkill =
        outType === "skill" ? adaptOpenCodeSkillDocument(identity, neutralDoc, item.invocation) : undefined;
      const ocDropped =
        ocSkill?.dropped ??
        (outType === "command" ? adaptOpenCodeCommandDocument(neutralDoc) : adaptOpenCodeAgentDocument(neutralDoc))
          .dropped;
      const ocTransformations = ocSkill?.transformations ?? [];
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
          identity,
          ...(ocSkill ? { advertised: ocSkill.advertised } : {}),
          edges: {
            model: sortedUnique(claudeEdges.model.map(ocId)),
            pointer: sortedUnique(claudeEdges.pointer.map(ocPointer)),
          },
          dropped: ocDropped,
          ...(ocTransformations.length ? { metadataTransformations: ocTransformations } : {}),
        },
        codex: {
          artifacts: ["skill"],
          identity: `${m.plugin.name}:${outName}`,
          sourceKind: neutral.sourceType,
          resolvedKind: outType,
          emittedKind: "skill",
          ...(outType !== "skill" ? { kindTransformation: `${outType}->skill` } : {}),
          implicit: codexInvocation.implicit,
          explicit: codexInvocation.explicit,
          policyFiles: codexInvocation.agentManifest ? ["agents/openai.yaml"] : [],
          edges: {
            model: claudeEdges.model.map((address) => `$${address}`),
            pointer: claudeEdges.pointer.map((address) => `$${address}`),
          },
          dropped: codexAdaptation.dropped,
          ...((codexMetadataTransformations.get(`${m.plugin.name}/${outName}`) ?? codexAdaptation.transformations)
            .length
            ? {
                metadataTransformations:
                  codexMetadataTransformations.get(`${m.plugin.name}/${outName}`) ?? codexAdaptation.transformations,
              }
            : {}),
          bodyTransformations: [],
        },
      };
      ledger[ledgerId] = entry;
    }
  }
  const sorted = Object.fromEntries(Object.entries(ledger).sort(([a], [b]) => a.localeCompare(b)));
  mkdirSync(join(root, "docs"), { recursive: true });
  writeFileSync(join(root, "docs", "ledger.json"), `${JSON.stringify(sorted, null, 2)}\n`);
}
