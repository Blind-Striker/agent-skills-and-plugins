import {
  cpSync,
  existsSync,
  mkdtempSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, sep } from "node:path";
import { pathToFileURL } from "node:url";
import { type AssembledItem, assembleItems, overlayBodyFile } from "./lib/assemble.ts";
import {
  loadAttributions,
  manifestAttributions,
  requireReadableRegularFile,
  writeDistributionNotices,
} from "./lib/attribution.ts";
import { CLAUDE_INVOCATION_KEYS, claudeInvocation } from "./lib/claude-target.ts";
import {
  adaptCodexSkillDocument,
  collectCodexEmissionProblems,
  createCodexMarketplace,
  createCodexPluginManifest,
  createCodexSkillAgentManifest,
  loadCodexPublisherMetadata,
  serializeCodexJson,
  serializeCodexSkillAgentManifest,
} from "./lib/codex-plugin.ts";
import { parseDoc, type ParsedDoc, serializeDoc } from "./lib/frontmatter.ts";
import { indexModes } from "./lib/git.ts";
import { collectHarnessFileProblems, placeHarnessFiles } from "./lib/harness-files.ts";
import { writeLedger } from "./lib/ledger.ts";
import {
  type CurationItem,
  type CurationManifest,
  collectCoverageProblems,
  emitsTo,
  loadManifest,
  UNKNOWN_SOURCE_RULE,
} from "./lib/manifest.ts";
import { createModuleManifest } from "./lib/opencode-bundle.ts";
import {
  adaptOpenCodeAgentDocument,
  adaptOpenCodeCommandDocument,
  adaptOpenCodeSkillDocument,
  claudeCounterpartPath,
  collectOpenCodeEmissionProblems,
  openCodeBundlePath,
  openCodeId,
  openCodeSkillIds,
} from "./lib/opencode-target.ts";
import { collectOriginalSkillProblems, ownSkillIdentities } from "./lib/own-skills.ts";
import { assembledReferenceFiles, collectOptionalReferenceProblems } from "./lib/optional-refs.ts";
import { scanRefs } from "./lib/refs.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import {
  checkPatch,
  driftedFiles,
  driftedMergeSources,
  ejectCommand,
  LOCK_FILE,
  listFiles,
  liveStampKeys,
  loadLock,
  lockKey,
  type OverlayLock,
  PATCH_FILE,
  patchTargets,
  stampsFrontmatter,
} from "./lib/overlay.ts";
import {
  collectIdentityProblems,
  deriveModuleRequirements,
  isOmitted,
  resolveItem,
  upstreamBase,
} from "./lib/resolve.ts";
import {
  buildRewriteMap,
  isBundledText,
  localize,
  type RefStyle,
  type RewriteTarget,
  rewriteOpenCodePaths,
} from "./lib/rewrite.ts";
import { type ComponentInfo, scanSubmodule } from "./lib/scan.ts";
import { descriptionLengthProblem, MAX_SKILL_DESCRIPTION_LENGTH } from "./lib/skill-description.ts";

export function buildAll(root: string): string[] {
  requireSubmodules(root);
  requireReadableRegularFile(join(root, "LICENSE"), "repository LICENSE");
  const report: string[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
  const attributions = loadAttributions(root);
  const notices = new Map(manifests.map((m) => [m.plugin.name, manifestAttributions(m, attributions)]));
  for (const m of manifests) {
    if (m.hooks?.include?.length) {
      throw new Error(`${m.plugin.name}: hooks.include is not implemented yet — keep it empty (YAGNI)`);
    }
  }
  const components = readdirSync(join(root, "external"))
    .filter((s) => statSync(join(root, "external", s)).isDirectory())
    .flatMap((s) => scanSubmodule(join(root, "external"), s));

  // Everything emitItem could fail on is resolved here, BEFORE the rmSync pair below: a failure
  // discovered mid-emit would leave the committed output half-deleted and marketplace.json stale,
  // so a single typo'd source: would cost a `git checkout`. One error lists every problem.
  const problems = collectProblems(root, manifests, components);
  if (problems.length) {
    const header = problems.length > 1 ? `${problems.length} unresolvable curation items, nothing deleted:\n` : "";
    throw new Error(header + problems.join("\n"));
  }
  const ownSkills = ownSkillIdentities(root, manifests);
  const moduleRequirements = deriveModuleRequirements(root, manifests, components, ownSkills);
  const rewriteMap = buildRewriteMap(manifests, components, ownSkills);

  // Assemble every authored item once before deleting any committed output. The staging directory
  // is internal pipeline state: Claude Code and OpenCode consume the same resolved bodies, and a
  // later Codex emitter joins at this boundary rather than resolving the estate a second time.
  const stagingRoot = mkdtempSync(join(tmpdir(), "deniz-agent-skills-assembly-"));
  try {
    const assembled = assembleItems(root, stagingRoot, manifests, components, report);
    const codexProblems = collectCodexEmissionProblems(root, manifests, assembled);
    if (codexProblems.length) {
      const header =
        codexProblems.length > 1 ? `${codexProblems.length} invalid Codex emission identities, nothing deleted:\n` : "";
      throw new Error(header + codexProblems.join("\n"));
    }
    const openCodeProblems = collectOpenCodeEmissionProblems(manifests, assembled);
    if (openCodeProblems.length) {
      const header =
        openCodeProblems.length > 1
          ? `${openCodeProblems.length} invalid OpenCode emission documents, nothing deleted:\n`
          : "";
      throw new Error(header + openCodeProblems.join("\n"));
    }
    const harnessFileProblems = collectHarnessFileProblems(assembled);
    if (harnessFileProblems.length) {
      const header =
        harnessFileProblems.length > 1
          ? `${harnessFileProblems.length} unplaceable harness files, nothing deleted:\n`
          : "";
      throw new Error(header + harnessFileProblems.join("\n"));
    }
    const descriptionProblems = collectDescriptionProblems(assembled, rewriteMap);
    if (descriptionProblems.length) {
      const header =
        descriptionProblems.length > 1
          ? `${descriptionProblems.length} skill descriptions over the ${MAX_SKILL_DESCRIPTION_LENGTH}-character limit after rendering, nothing deleted:\n`
          : "";
      throw new Error(header + descriptionProblems.join("\n"));
    }

    const owners = new Map(assembled.map((item) => [item.outName, item.plugin]));
    const optionalProblems = assembled.flatMap((item) => {
      const neutral = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const files = assembledReferenceFiles(item, rewriteMap, (harness) => {
        if (harness === "claude") {
          return serializeDoc(claudeDocument(item).document);
        }
        if (harness === "codex") {
          return serializeDoc(adaptCodexSkillDocument(item.outName, neutral).document);
        }
        return serializeDoc(
          (item.outType === "skill"
            ? adaptOpenCodeSkillDocument(openCodeId(item.plugin, item.outName), neutral, item.intent.invocation)
            : item.outType === "command"
              ? adaptOpenCodeCommandDocument(neutral)
              : adaptOpenCodeAgentDocument(neutral)
          ).document,
        );
      });
      const issues = collectOptionalReferenceProblems(
        `${item.plugin}/${item.outName}`,
        item.intent,
        files,
        owners,
        moduleRequirements.get(item.plugin),
      );
      for (const file of files) {
        for (const ref of scanRefs(file.text)) {
          if (!item.intent.optional_refs?.includes(ref.name) || owners.get(ref.name) !== ref.ns) {
            continue;
          }
          const target = assembled.find((candidate) => candidate.outName === ref.name);
          if (!target) {
            continue;
          }
          const claude = claudeDocument(target).document.frontmatter;
          const neutral = parseDoc(readFileSync(join(target.dir, "SKILL.md"), "utf8"));
          const oc =
            target.outType === "skill"
              ? adaptOpenCodeSkillDocument(openCodeId(target.plugin, target.outName), neutral, target.intent.invocation)
              : undefined;
          for (const harness of file.harnesses) {
            if (!emitsTo(target.intent, harness)) {
              issues.push(
                `${file.path}: ${ref.kind === "model" ? "model-edge" : "pointer"} to ${ref.address}: the ${harness} tree does not ship the target (harnesses)`,
              );
              continue;
            }
            const reachable =
              ref.kind === "model"
                ? harness === "claude"
                  ? target.outType === "command" ||
                    (target.outType === "skill" && claude["disable-model-invocation"] !== true)
                  : harness === "opencode"
                    ? oc?.advertised === true
                    : target.intent.invocation !== "manual"
                : harness !== "claude" || target.outType !== "skill" || claude["user-invocable"] !== false;
            if (!reachable) {
              issues.push(
                `${file.path}: ${ref.kind === "model" ? "model-edge" : "pointer"} to ${ref.address}: ${harness} audience cannot reach target`,
              );
            }
          }
        }
      }
      return issues;
    });
    if (optionalProblems.length) {
      throw new Error(`Invalid optional references, nothing deleted:\n${optionalProblems.join("\n")}`);
    }

    rmSync(join(root, "plugins"), { recursive: true, force: true });
    rmSync(join(root, "opencode"), { recursive: true, force: true });
    rmSync(join(root, "codex"), { recursive: true, force: true });
    // `.agents/` may later contain authored repository configuration. Remove only this generator's
    // owned file, never the broad tree.
    rmSync(join(root, ".agents", "plugins", "marketplace.json"), { force: true });

    for (const manifest of manifests) {
      for (const item of assembled.filter(
        (candidate) => candidate.plugin === manifest.plugin.name && emitsTo(candidate.intent, "claude"),
      )) {
        emitClaudeItem(root, item, report);
      }
      const pluginDir = join(root, "plugins", manifest.plugin.name);
      writeDistributionNotices(root, pluginDir, notices.get(manifest.plugin.name) ?? []);
      mkdirSync(join(pluginDir, ".claude-plugin"), { recursive: true });
      writeFileSync(join(pluginDir, ".claude-plugin", "plugin.json"), `${JSON.stringify(manifest.plugin, null, 2)}\n`);
    }

    writeMarketplace(root, manifests);
    // Every emitter consumes the same pre-localization assembly. Reference spelling remains a
    // target decision and is applied only after the native artifact trees exist.
    emitOpenCode(root, manifests, assembled, report);
    for (const manifest of manifests) {
      writeDistributionNotices(
        root,
        join(root, "opencode", manifest.plugin.name),
        notices.get(manifest.plugin.name) ?? [],
      );
    }
    emitCodex(root, manifests, assembled, notices, report);
    rewriteTree(join(root, "plugins"), rewriteMap, "claude");
    rewriteOpenCodeTree(root, manifests, rewriteMap, openCodeSkillIds(assembled));
    rewriteTree(join(root, "codex"), rewriteMap, "codex");
    finalizeCodexSkillMetadata(root, assembled);
    // Manifests come last so they hash the final bytes: post-rewrite, and with the manifest itself
    // excluded from the walk.
    writeOpenCodeManifests(root, manifests, moduleRequirements);
    writeLedger(root, manifests, components, assembled);
    return report;
  } finally {
    rmSync(stagingRoot, { recursive: true, force: true });
  }
}

/** Emit the common assembled estate as native Codex Plugins with one flat skill namespace. */
function emitCodex(
  root: string,
  manifests: CurationManifest[],
  assembled: AssembledItem[],
  notices: Map<string, ReturnType<typeof manifestAttributions>>,
  report: string[],
): void {
  const publisher = loadCodexPublisherMetadata(root);
  for (const manifest of manifests) {
    const pluginRoot = join(root, "codex", manifest.plugin.name);
    for (const item of assembled
      .filter((candidate) => candidate.plugin === manifest.plugin.name && emitsTo(candidate.intent, "codex"))
      .sort((left, right) => left.outName.localeCompare(right.outName))) {
      const destination = join(pluginRoot, "skills", item.outName);
      cpSync(item.dir, destination, { recursive: true });
      placeHarnessFiles(item.dir, destination, item.intent.harness_files, "codex");
      const doc = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const adapted = adaptCodexSkillDocument(item.outName, doc);
      writeFileSync(join(destination, "SKILL.md"), serializeDoc(adapted.document));
      if (adapted.dropped.length) {
        report.push(`codex skill ${item.outName}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`);
      }

      // Invocation policy is emitter-owned. Never inherit an upstream target's policy file: absent,
      // auto, and both use Codex's ordinary implicit behavior; manual alone gets an explicit-only
      // agent manifest. Other dependency files in agents/ remain part of the selected closure.
      const agentsDir = join(destination, "agents");
      rmSync(join(agentsDir, "openai.yaml"), { force: true });
      if (existsSync(agentsDir) && !readdirSync(agentsDir).length) {
        rmSync(agentsDir, { recursive: true, force: true });
      }
      const agentManifest = createCodexSkillAgentManifest(
        item.plugin,
        item.outName,
        String(adapted.document.frontmatter.description),
        item.intent.invocation,
      );
      if (agentManifest) {
        mkdirSync(agentsDir, { recursive: true });
        writeFileSync(join(agentsDir, "openai.yaml"), serializeCodexSkillAgentManifest(agentManifest));
      }
    }

    writeDistributionNotices(root, pluginRoot, notices.get(manifest.plugin.name) ?? []);
    mkdirSync(join(pluginRoot, ".codex-plugin"), { recursive: true });
    writeFileSync(
      join(pluginRoot, ".codex-plugin", "plugin.json"),
      serializeCodexJson(createCodexPluginManifest(manifest, publisher)),
    );
  }

  const marketplacePath = join(root, ".agents", "plugins", "marketplace.json");
  mkdirSync(join(root, ".agents", "plugins"), { recursive: true });
  writeFileSync(marketplacePath, serializeCodexJson(createCodexMarketplace(manifests)));
}

/**
 * Re-serialize each Codex skill after reference localization and derive its explicit-only policy from
 * the localized description. Rendering lengthens a description, but its length was checked before any
 * output was deleted (collectDescriptionProblems); nothing here shortens it.
 */
function finalizeCodexSkillMetadata(root: string, assembled: AssembledItem[]): void {
  for (const item of assembled.filter((candidate) => emitsTo(candidate.intent, "codex"))) {
    const skillRoot = join(root, "codex", item.plugin, "skills", item.outName);
    const skillPath = join(skillRoot, "SKILL.md");
    const adapted = adaptCodexSkillDocument(item.outName, parseDoc(readFileSync(skillPath, "utf8")));
    writeFileSync(skillPath, serializeDoc(adapted.document));

    const agentManifest = createCodexSkillAgentManifest(
      item.plugin,
      item.outName,
      String(adapted.document.frontmatter.description),
      item.intent.invocation,
    );
    if (agentManifest) {
      const agentsDir = join(skillRoot, "agents");
      mkdirSync(agentsDir, { recursive: true });
      writeFileSync(join(agentsDir, "openai.yaml"), serializeCodexSkillAgentManifest(agentManifest));
    }
  }
}

/**
 * The shared description limit, measured on the text each harness will emit: the neutral description
 * after that harness's reference rendering, plus OpenCode's path respelling, through the same functions
 * the trees are rewritten with. Claude Code and OpenCode emit a skill only for a skill item; Codex
 * emits every resolved kind as one. Each finding names the emitted path, as validate's does.
 */
function collectDescriptionProblems(assembled: AssembledItem[], rewriteMap: Map<string, RewriteTarget>): string[] {
  const skillIds = openCodeSkillIds(assembled);
  const problems: string[] = [];
  for (const item of assembled) {
    const doc = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
    const neutral = serializeDoc({ frontmatter: { description: String(doc.frontmatter.description ?? "") }, body: "" });
    const emitted: Array<[string, string]> = [];
    if (item.outType === "skill" && emitsTo(item.intent, "claude")) {
      emitted.push([`plugins/${item.plugin}/skills/${item.outName}/SKILL.md`, localize(neutral, rewriteMap, "claude")]);
    }
    if (item.outType === "skill" && emitsTo(item.intent, "opencode")) {
      emitted.push([
        `opencode/${item.plugin}/skills/${openCodeId(item.plugin, item.outName)}/SKILL.md`,
        rewriteOpenCodePaths(localize(neutral, rewriteMap, "opencode"), 0, skillIds),
      ]);
    }
    if (emitsTo(item.intent, "codex")) {
      emitted.push([`codex/${item.plugin}/skills/${item.outName}/SKILL.md`, localize(neutral, rewriteMap, "codex")]);
    }
    for (const [path, text] of emitted) {
      const problem = descriptionLengthProblem(path, String(parseDoc(text).frontmatter.description ?? ""));
      if (problem) {
        problems.push(problem);
      }
    }
  }
  return problems;
}

/**
 * Frontmatter Claude Code reads from a skill or a command: the keys of its skill frontmatter
 * reference, which includes the Agent Skills fields `license`, `compatibility`, and `metadata`
 * (code.claude.com/docs/en/skills). Any other key is dropped and reported, as the OpenCode and
 * Codex emitters do (ADR-0002, ADR-0006).
 */
export const CLAUDE_SKILL_KEYS: ReadonlySet<string> = new Set([
  "name",
  "description",
  "when_to_use",
  "argument-hint",
  "arguments",
  "disable-model-invocation",
  "user-invocable",
  "allowed-tools",
  "disallowed-tools",
  "model",
  "effort",
  "context",
  "agent",
  "background",
  "hooks",
  "paths",
  "shell",
  "metadata",
  "license",
  "compatibility",
]);
/**
 * Frontmatter Claude Code reads from a plugin agent: its subagent reference without `hooks`,
 * `mcpServers`, `permissionMode`, and `initialPrompt`, which it ignores in an agent a plugin ships
 * (code.claude.com/docs/en/sub-agents). Any other key is dropped and reported.
 */
export const CLAUDE_AGENT_KEYS: ReadonlySet<string> = new Set([
  "name",
  "description",
  "tools",
  "disallowedTools",
  "model",
  "maxTurns",
  "skills",
  "memory",
  "background",
  "omitClaudeMd",
  "effort",
  "isolation",
  "color",
  "experimental",
]);

/**
 * Adapt a copy of the neutral assembled document to Claude Code: a skill takes the invocation
 * posture its item or original-skill declaration states, and every document keeps only the
 * frontmatter keys its kind reads.
 */
function claudeDocument(assembled: AssembledItem): { document: ParsedDoc; dropped: string[] } {
  const doc = parseDoc(readFileSync(join(assembled.dir, "SKILL.md"), "utf8"));
  const { invocation } = assembled.intent;
  let frontmatter: Record<string, unknown> = { ...doc.frontmatter };
  if (assembled.outType === "skill" && invocation) {
    for (const key of CLAUDE_INVOCATION_KEYS) {
      delete frontmatter[key];
    }
    frontmatter = { ...frontmatter, ...claudeInvocation(invocation), name: assembled.outName };
  }
  const keys = assembled.outType === "agent" ? CLAUDE_AGENT_KEYS : CLAUDE_SKILL_KEYS;
  const dropped = Object.keys(frontmatter)
    .filter((key) => !keys.has(key))
    .sort();
  for (const key of dropped) {
    delete frontmatter[key];
  }
  return { document: { frontmatter, body: doc.body }, dropped };
}

/** Emit one shared assembled item in Claude Code's native Plugin shape. */
function emitClaudeItem(root: string, assembled: AssembledItem, report: string[]): void {
  const pluginDir = join(root, "plugins", assembled.plugin);
  let target: string;
  if (assembled.outType === "skill") {
    const destination = join(pluginDir, "skills", assembled.outName);
    cpSync(assembled.dir, destination, { recursive: true });
    placeHarnessFiles(assembled.dir, destination, assembled.intent.harness_files, "claude");
    target = join(destination, "SKILL.md");
  } else {
    const kindDir = assembled.outType === "command" ? "commands" : "agents";
    mkdirSync(join(pluginDir, kindDir), { recursive: true });
    target = join(pluginDir, kindDir, `${assembled.outName}.md`);
  }
  const adapted = claudeDocument(assembled);
  writeFileSync(target, serializeDoc(adapted.document));
  if (adapted.dropped.length) {
    report.push(
      `claude ${assembled.outType} ${assembled.plugin}:${assembled.outName}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`,
    );
  }
}

/**
 * Neither overlay kind can notice upstream moving underneath it on its own. `git apply` looked like
 * a guard for patches, but it searches for its context with an unbounded offset and takes the first
 * match — so a hunk relocates, or lands on a different region that still matches, and exits 0. Both
 * kinds are therefore blessed against recorded content hashes, and an upstream edit to any file the
 * overlay depends on stops the build until a human has looked.
 */
function overlayDrift(
  root: string,
  item: CurationItem,
  comp: ComponentInfo,
  plugin: string,
  outName: string,
  overlayDir: string,
  lock: OverlayLock,
  next: string,
): string[] {
  const id = `${plugin}/${outName}`;
  const entry = lock[lockKey(plugin, outName)];
  if (!entry) {
    return [`${id}: overlay is not recorded in overlays/overlays.lock.json — run: ${next}`];
  }
  if (entry.source !== item.source) {
    return [`${id}: lock was blessed against ${entry.source}, but the item now curates ${item.source} — run: ${next}`];
  }
  const base = upstreamBase(root, item, comp);
  // liveKeys === eject bless keys: both use overlay.patch existence, patchTargets/listFiles, then stampFiles.
  const liveKeys = liveStampKeys(base, overlayDir);
  const lockKeys = Object.keys(entry.files);
  const liveOnly = liveKeys.filter((key) => !lockKeys.includes(key));
  const lockOnly = lockKeys.filter((key) => !liveKeys.includes(key));
  if (liveOnly.length || lockOnly.length) {
    const changes = [
      ...(liveOnly.length ? [`not in lock: ${liveOnly.join(", ")}`] : []),
      ...(lockOnly.length ? [`no longer targeted: ${lockOnly.join(", ")}`] : []),
    ];
    return [`${id}: overlay stamp target set changed (${changes.join("; ")}) — run: ${next}`];
  }
  if (!Object.keys(entry.files).length) {
    return [`${id}: lock records no upstream file, so nothing guards this overlay — run: ${next}`];
  }
  const drifted = driftedFiles(base, entry);
  if (drifted.length) {
    return [`${id}: upstream changed under the overlay (${drifted.join(", ")}) — review the diff, then: ${next}`];
  }
  // A merged body has ingredients the primary stamp knows nothing about, so the declaration and the
  // lock have to name the same sources before either guards anything. Both directions are caught:
  // a source declared but never stamped is unguarded, and a stamp the manifest no longer declares
  // is a lock still guarding an ingredient this body stopped using.
  const declaredSources = new Set((item.merged_from ?? []).map((ms) => ms.source));
  const blessedSources = new Set(Object.keys(entry.mergeSources ?? {}));
  const sameSources =
    declaredSources.size === blessedSources.size && [...declaredSources].every((s) => blessedSources.has(s));
  if ((declaredSources.size || blessedSources.size) && !sameSources) {
    const declared = [...declaredSources].join(", ") || "none";
    const held = [...blessedSources].join(", ") || "none";
    return [`${id}: merge sources are not blessed (declared: ${declared}; lock has: ${held}) — run: ${next}`];
  }
  // Naming the same sources is still not enough once a source can declare its own files: that list
  // grows without the source SET changing, so the check above passes while the lock holds only the
  // older names. Declared, unstamped, guarding nothing — the unblessed-source failure one level down.
  for (const ms of item.merged_from ?? []) {
    const stamped = entry.mergeSources?.[ms.source] ?? {};
    const unstamped = (ms.files ?? []).filter((f) => !(f in stamped));
    if (unstamped.length) {
      return [
        `${id}: merge source ${ms.source} declares ${unstamped.join(", ")}, which the lock does not stamp — run: ${next}`,
      ];
    }
  }
  // Naming the same sources is not yet a guard. A stamp is taken under the same-filename rule, so an
  // address that shares no file name with the overlay records absence and nothing else — and an
  // absent stamp only speaks when a file APPEARS, which is why blessing such an address succeeds and
  // the guard can then never fire. Blessing again would only re-record the nulls: the address is wrong.
  const unguarded = Object.entries(entry.mergeSources ?? {})
    .filter(([, files]) => Object.values(files).every((sha) => sha === null))
    .map(([addr]) => addr);
  if (unguarded.length) {
    return unguarded.map(
      (addr) =>
        `${id}: merge source ${addr} shares no filename with the overlay — nothing guards it; wrong address? Fix merged_from, then: ${next}`,
    );
  }
  const mergeDrift = driftedMergeSources(root, entry);
  if (mergeDrift.length) {
    return [
      `${id}: merge source changed under the overlay (${mergeDrift.join("; ")}) — review the diff, then: ${next}`,
    ];
  }
  return [];
}

// Mirrors every throw in emitItem, with the identical wording, so the messages a user sees are the
// same whichever side reports them. The emitItem throws stay as unreachable safety nets.
function collectProblems(root: string, manifests: CurationManifest[], components: ComponentInfo[]): string[] {
  const problems = [
    ...collectIdentityProblems(root, manifests, components),
    ...collectOriginalSkillProblems(root, manifests),
    ...collectCoverageProblems(manifests),
  ];
  const lock = loadLock(root);
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { comp, outName, outType, overlayDir, id } = resolveItem(root, m.plugin.name, item, components);
      if (!comp) {
        problems.push(`${m.plugin.name}: source not found in external/: ${item.source} — ${UNKNOWN_SOURCE_RULE}`);
        continue;
      }
      if (item.body && !existsSync(overlayDir)) {
        problems.push(
          `${id}: body is ${item.body} but overlays/${id}/ is missing — run: ${ejectCommand(m.plugin.name, outName, item.body === "patch" ? "patch" : "overlay")}`,
        );
      } else if (item.body === "patch") {
        // A conversion re-serializes frontmatter around a body, so there is no stable file for a
        // diff to land on — those items take a full-file overlay instead (ADR-0001).
        if (outType !== "skill") {
          problems.push(`${id}: body: patch applies to skill output only — a ${outType} needs body: overlay`);
        } else if (!existsSync(join(overlayDir, PATCH_FILE))) {
          problems.push(
            `${id}: body is patch but overlays/${id}/${PATCH_FILE} is missing — run: ${ejectCommand(m.plugin.name, outName, "patch")}`,
          );
        } else {
          // --check writes nothing, so this runs against pristine upstream in the fail-fast pass.
          // It runs before the drift check because it decides the next step: a patch that still
          // applies is blessed, one that does not is rebased first. No cause is asserted: a patch
          // also stops applying when upstream adopted the same edit, or when the path now sits at
          // or beyond a symlink, and git's own message says which.
          const err = checkPatch(upstreamBase(root, item, comp), join(overlayDir, PATCH_FILE));
          const rebase = ejectCommand(m.plugin.name, outName, "rebase");
          const next = err ? rebase : ejectCommand(m.plugin.name, outName, "bless");
          problems.push(...overlayDrift(root, item, comp, m.plugin.name, outName, overlayDir, lock, next));
          // omit runs first, so a pattern that swallows a file the patch edits leaves the hunk
          // nothing to land on. git apply would say so — but only after the output tree was
          // already deleted, which is precisely what this pass exists to prevent.
          const swallowed = patchTargets(readFileSync(join(overlayDir, PATCH_FILE), "utf8")).filter((t) =>
            isOmitted(t, item.omit),
          );
          if (swallowed.length) {
            problems.push(`${id}: omit drops ${swallowed.join(", ")}, which ${PATCH_FILE} edits`);
          }
          if (err) {
            problems.push(`${id}: ${PATCH_FILE} no longer applies to ${item.source} — run: ${rebase}\n${err}`);
          }
        }
      } else if (item.body === "overlay") {
        if (outType !== "skill") {
          // A skill target copies the whole overlay dir, but a conversion reads one file out of it —
          // and the build looks it up by the SOURCE name, so an overlay renamed by hand is invisible.
          const file = overlayBodyFile(comp, item);
          if (!existsSync(join(overlayDir, file))) {
            problems.push(
              `${id}: overlays/${id}/${file} is missing — a ${outType} overlay is read by its source file name, so do not rename it`,
            );
          }
        }
        const next = ejectCommand(m.plugin.name, outName, "bless");
        problems.push(...overlayDrift(root, item, comp, m.plugin.name, outName, overlayDir, lock, next));
      }
      if (stampsFrontmatter(item)) {
        problems.push(...frontmatterDrift(root, item, comp, m.plugin.name, outName, lock));
      }
      if (outType === "skill" && comp.type !== "skill") {
        problems.push(`${item.source}: ${comp.type} -> skill conversion not supported`);
      }
    }
  }
  return problems;
}

/**
 * A `frontmatter:` override merges in after body assembly, so an upstream rewrite never makes it
 * drift on its own: it goes on describing the body it was written against. An item that owns its
 * body is guarded by that overlay's stamp; one that owns nothing records the upstream document the
 * override rewrites, and that document moving stops the build until the override is reviewed.
 */
function frontmatterDrift(
  root: string,
  item: CurationItem,
  comp: ComponentInfo,
  plugin: string,
  outName: string,
  lock: OverlayLock,
): string[] {
  const id = `${plugin}/${outName}`;
  const next = ejectCommand(plugin, outName, "bless");
  const entry = lock[lockKey(plugin, outName)];
  const document = overlayBodyFile(comp, item);
  if (entry?.frontmatter !== true) {
    return [`${id}: frontmatter override is not stamped in overlays/${LOCK_FILE} — run: ${next}`];
  }
  if (entry.source !== item.source) {
    return [
      `${id}: frontmatter stamp was blessed against ${entry.source}, but the item now curates ${item.source} — run: ${next}`,
    ];
  }
  if (Object.keys(entry.files).join("\n") !== document) {
    return [`${id}: frontmatter stamp must record ${document} and nothing else — run: ${next}`];
  }
  if (driftedFiles(upstreamBase(root, item, comp), entry).length) {
    return [`${id}: upstream changed ${document} under the frontmatter override — review the diff, then: ${next}`];
  }
  return [];
}

function writeMarketplace(root: string, manifests: CurationManifest[]): void {
  const marketplace = {
    name: "deniz-skills",
    owner: { name: "Deniz İrgin", email: "1965259+Blind-Striker@users.noreply.github.com" },
    plugins: manifests.map((m) => ({
      name: m.plugin.name,
      source: `./plugins/${m.plugin.name}`,
      description: m.plugin.description,
    })),
  };
  mkdirSync(join(root, ".claude-plugin"), { recursive: true });
  writeFileSync(join(root, ".claude-plugin", "marketplace.json"), `${JSON.stringify(marketplace, null, 2)}\n`);
}

function rewriteTree(dir: string, map: Map<string, RewriteTarget>, style: RefStyle): void {
  if (!existsSync(dir)) {
    return;
  }
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      rewriteTree(p, map, style);
    } else if (e.name.endsWith(".md")) {
      writeFileSync(p, localize(readFileSync(p, "utf8"), map, style));
    }
  }
}

/**
 * OpenCode 2 reference spelling (docs/architecture/references-and-linking.md, Localization): every
 * Markdown file in a Module gets the harness phrasing and the dotted-ID rendering. Inside `skills/<id>/`, every file is walked: a Markdown file
 * gets the phrasing and rendering, then its sibling climbs and item-root `skills/<name>/` paths
 * re-rooted onto the ID folders (climbs measured from the file's depth below its own skill folder);
 * any other file that `isBundledText` accepts (a script, a template) gets only the path respelling;
 * a binary is never read as text. A file is written only when its text changed, and the Module
 * manifest keeps taking modes from the Claude counterpart, so a respelled `100755` script keeps its
 * mode. Everything outside `skills/` (`commands/`, `agents/`, distribution notices) gets only the
 * phrasing and the rendering: no climb from there lands on the shared skills directory.
 */
function rewriteOpenCodeTree(
  root: string,
  manifests: CurationManifest[],
  map: Map<string, RewriteTarget>,
  skillIds: Map<string, string>,
): void {
  for (const manifest of manifests) {
    const moduleRoot = join(root, "opencode", manifest.plugin.name);
    if (!existsSync(moduleRoot)) {
      continue;
    }
    for (const entry of readdirSync(moduleRoot, { withFileTypes: true })) {
      const path = join(moduleRoot, entry.name);
      if (entry.isDirectory() && entry.name !== "skills") {
        rewriteTree(path, map, "opencode");
      } else if (entry.isFile() && entry.name.endsWith(".md")) {
        writeFileSync(path, localize(readFileSync(path, "utf8"), map, "opencode"));
      }
    }
    const skillsRoot = join(moduleRoot, "skills");
    if (!existsSync(skillsRoot)) {
      continue;
    }
    for (const folder of readdirSync(skillsRoot, { withFileTypes: true })) {
      if (!folder.isDirectory()) {
        continue;
      }
      const skillFolder = join(skillsRoot, folder.name);
      for (const file of listFiles(skillFolder)) {
        const path = join(skillFolder, file);
        const bytes = readFileSync(path);
        if (!isBundledText(bytes)) {
          continue;
        }
        const depth = relative(skillFolder, dirname(path)).split(sep).filter(Boolean).length;
        const before = bytes.toString("utf8");
        const text = path.endsWith(".md") ? localize(before, map, "opencode") : before;
        const after = rewriteOpenCodePaths(text, depth, skillIds);
        if (after !== before) {
          writeFileSync(path, after); // only on change: an unchanged script is not touched
        }
      }
    }
  }
}

/**
 * OpenCode 2: one artifact per item at its `<plugin>.<name>` ID, read from the neutral assembled
 * document. Invocation is frontmatter only (ADR-0005): a hidden skill carries
 * `metadata.opencode/autoinvoke: false`, and no item changes shape because of its invocation. Each
 * plugin becomes one Module Bundle: opencode/<plugin>/{skills,commands,agents,manifest.json}.
 */
function emitOpenCode(root: string, manifests: CurationManifest[], assembled: AssembledItem[], report: string[]): void {
  for (const manifest of manifests) {
    const module = manifest.plugin.name;
    const moduleRoot = join(root, "opencode", module);
    for (const item of assembled
      .filter((candidate) => candidate.plugin === module && emitsTo(candidate.intent, "opencode"))
      .sort((left, right) => left.outName.localeCompare(right.outName))) {
      const id = openCodeId(module, item.outName);
      const neutral = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const destination = join(moduleRoot, openCodeBundlePath(item.outType, module, item.outName));
      if (item.outType === "skill") {
        cpSync(item.dir, destination, { recursive: true });
        placeHarnessFiles(item.dir, destination, item.intent.harness_files, "opencode");
        const adapted = adaptOpenCodeSkillDocument(id, neutral, item.intent.invocation);
        writeFileSync(join(destination, "SKILL.md"), serializeDoc(adapted.document));
        if (adapted.dropped.length) {
          report.push(`opencode skill ${id}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`);
        }
        for (const transformation of adapted.transformations) {
          report.push(`opencode skill ${id}: ${transformation}`);
        }
        continue;
      }
      const adapted =
        item.outType === "command" ? adaptOpenCodeCommandDocument(neutral) : adaptOpenCodeAgentDocument(neutral);
      mkdirSync(dirname(destination), { recursive: true });
      writeFileSync(destination, serializeDoc(adapted.document));
      if (adapted.dropped.length) {
        report.push(`opencode ${item.outType} ${id}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`);
      }
      // A command is a single file, so the selected closure has nowhere to live in OpenCode; it stays
      // in Claude and Codex, and the cost of the `as: command` choice is named (ADR-0002).
      const bundled = listFiles(item.dir).filter((file) => file !== "SKILL.md");
      if (item.outType === "command" && bundled.length) {
        report.push(`opencode command ${id}: bundled files not emitted: ${bundled.join(", ")}`);
      }
    }
  }
}

/**
 * One manifest per curated Module, written only after the reference rewrite so every hash covers
 * the final bytes. Every manifest gets written — an items: [] Module still has to name itself — and modes
 * split by provenance: a copied skill file under `skills/<module>.<name>/` carries the Git index mode
 * of its committed `plugins/<module>/skills/<name>/` counterpart (that is where an upstream 100755
 * lands in the repo), while build-generated documents — commands, agents, distribution notices,
 * anything with no plugin counterpart — are always 100644.
 */
function writeOpenCodeManifests(
  root: string,
  manifests: CurationManifest[],
  moduleRequirements: Map<string, string[]>,
): void {
  for (const m of manifests) {
    const moduleRoot = join(root, "opencode", m.plugin.name);
    mkdirSync(moduleRoot, { recursive: true });
    const pluginModes = indexModes(root, [
      `plugins/${m.plugin.name}/`,
      `codex/${m.plugin.name}/`,
      `skills/${m.plugin.name}/`,
    ]);
    const requiredModules = moduleRequirements.get(m.plugin.name);
    if (requiredModules === undefined) {
      throw new Error(`internal error: missing requiredModules for ${m.plugin.name}`);
    }
    const manifest = createModuleManifest(
      moduleRoot,
      m.plugin.name,
      m.plugin.version,
      (path) => {
        const counterpart = claudeCounterpartPath(m.plugin.name, path);
        if (counterpart === undefined) {
          return "100644";
        }
        // An item the Claude tree does not ship (harnesses) takes the mode from the Codex copy, then
        // from an original skill's own directory.
        const rest = counterpart.slice(`plugins/${m.plugin.name}/skills/`.length);
        return (
          pluginModes.get(counterpart) ??
          pluginModes.get(`codex/${m.plugin.name}/skills/${rest}`) ??
          pluginModes.get(`skills/${m.plugin.name}/${rest}`) ??
          "100644"
        );
      },
      requiredModules,
    );
    writeFileSync(join(moduleRoot, "manifest.json"), `${JSON.stringify(manifest, null, 2)}\n`);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  for (const line of buildAll(process.cwd())) {
    console.log(line);
  }
  console.log("Build complete.");
}
