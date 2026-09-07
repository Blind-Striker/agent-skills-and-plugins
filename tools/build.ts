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
import { basename, join } from "node:path";
import { pathToFileURL } from "node:url";
import { type AssembledItem, assembleItems, overlayBodyFile } from "./lib/assemble.ts";
import {
  loadAttributions,
  manifestAttributions,
  requireReadableRegularFile,
  writeDistributionNotices,
} from "./lib/attribution.ts";
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
import { OPENCODE_SKILL_KEYS, writeLedger } from "./lib/ledger.ts";
import { type CurationItem, type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { createModuleManifest } from "./lib/opencode-bundle.ts";
import { ownSkillIdentities } from "./lib/own-skills.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import {
  checkPatch,
  driftedFiles,
  driftedMergeSources,
  listFiles,
  liveStampKeys,
  loadLock,
  lockKey,
  type OverlayLock,
  PATCH_FILE,
  patchTargets,
} from "./lib/overlay.ts";
import {
  collectIdentityProblems,
  deriveModuleRequirements,
  isOmitted,
  resolveItem,
  upstreamBase,
} from "./lib/resolve.ts";
import { buildRewriteMap, type RefStyle, rewriteRefs } from "./lib/rewrite.ts";
import { type ComponentInfo, scanSubmodule } from "./lib/scan.ts";

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
  const claudeRewrite = buildRewriteMap(manifests, components, "claude", ownSkills);
  const opencodeRewrite = buildRewriteMap(manifests, components, "opencode", ownSkills);
  const codexRewrite = buildRewriteMap(manifests, components, "codex", ownSkills);

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

    rmSync(join(root, "plugins"), { recursive: true, force: true });
    rmSync(join(root, "opencode"), { recursive: true, force: true });
    rmSync(join(root, "codex"), { recursive: true, force: true });
    // `.agents/` may later contain authored repository configuration. Remove only this generator's
    // owned file, never the broad tree.
    rmSync(join(root, ".agents", "plugins", "marketplace.json"), { force: true });

    for (const manifest of manifests) {
      for (const item of assembled.filter((candidate) => candidate.plugin === manifest.plugin.name)) {
        emitClaudeItem(root, item);
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
    const codexMetadataTransformations = emitCodex(root, manifests, assembled, notices, report);
    rewriteTree(join(root, "plugins"), claudeRewrite);
    rewriteTree(join(root, "opencode"), opencodeRewrite);
    rewriteTree(join(root, "codex"), codexRewrite, "codex");
    finalizeCodexSkillMetadata(root, assembled, codexMetadataTransformations, report);
    // Manifests come last so they hash the final bytes: post-rewrite, and with the manifest itself
    // excluded from the walk.
    writeOpenCodeManifests(root, manifests, moduleRequirements);
    writeLedger(root, manifests, components, assembled, codexMetadataTransformations);
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
): Map<string, string[]> {
  const metadataTransformations = new Map<string, string[]>();
  const publisher = loadCodexPublisherMetadata(root);
  for (const manifest of manifests) {
    const pluginRoot = join(root, "codex", manifest.plugin.name);
    for (const item of assembled
      .filter((candidate) => candidate.plugin === manifest.plugin.name)
      .sort((left, right) => left.outName.localeCompare(right.outName))) {
      const destination = join(pluginRoot, "skills", item.outName);
      cpSync(item.dir, destination, { recursive: true });
      const doc = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const adapted = adaptCodexSkillDocument(item.outName, doc);
      writeFileSync(join(destination, "SKILL.md"), serializeDoc(adapted.document));
      if (adapted.dropped.length) {
        report.push(`codex skill ${item.outName}: dropped frontmatter keys: ${adapted.dropped.join(", ")}`);
      }
      for (const transformation of adapted.transformations) {
        report.push(`codex skill ${item.outName}: ${transformation}`);
      }
      if (adapted.transformations.length) {
        metadataTransformations.set(`${item.plugin}/${item.outName}`, adapted.transformations);
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
        item.item?.invocation,
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
  return metadataTransformations;
}

/** Re-apply Codex metadata bounds after reference localization, which can lengthen descriptions. */
function finalizeCodexSkillMetadata(
  root: string,
  assembled: AssembledItem[],
  metadataTransformations: Map<string, string[]>,
  report: string[],
): void {
  for (const item of assembled) {
    const skillRoot = join(root, "codex", item.plugin, "skills", item.outName);
    const skillPath = join(skillRoot, "SKILL.md");
    const adapted = adaptCodexSkillDocument(item.outName, parseDoc(readFileSync(skillPath, "utf8")));
    writeFileSync(skillPath, serializeDoc(adapted.document));
    if (adapted.transformations.length) {
      const key = `${item.plugin}/${item.outName}`;
      const combined = [...(metadataTransformations.get(key) ?? []), ...adapted.transformations];
      metadataTransformations.set(key, [...new Set(combined)]);
      for (const transformation of adapted.transformations) {
        report.push(`codex skill ${item.outName}: ${transformation}`);
      }
    }

    const agentManifest = createCodexSkillAgentManifest(
      item.plugin,
      item.outName,
      String(adapted.document.frontmatter.description),
      item.item?.invocation,
    );
    if (agentManifest) {
      const agentsDir = join(skillRoot, "agents");
      mkdirSync(agentsDir, { recursive: true });
      writeFileSync(join(agentsDir, "openai.yaml"), serializeCodexSkillAgentManifest(agentManifest));
    }
  }
}

/** The two Claude Code invocation keys, so a stated intent replaces whatever upstream said. */
const CLAUDE_INVOCATION_KEYS = ["user-invocable", "disable-model-invocation"] as const;

/**
 * Claude Code's half of ADR-0005: one artifact, and the dial is frontmatter. An item that states no
 * intent is left alone entirely — silence is not a default, so adopting the field item by item
 * costs nothing on the items that have not adopted it.
 */
function claudeInvocation(item: CurationItem): Record<string, unknown> {
  switch (item.invocation) {
    case "auto":
      return { "user-invocable": false };
    case "manual":
      return { "disable-model-invocation": true };
    default:
      return {}; // `both` sets neither; absent sets nothing at all
  }
}

/** Apply Claude-only invocation posture to a copy of the neutral assembled document. */
function claudeDocument(assembled: AssembledItem): ParsedDoc {
  const doc = parseDoc(readFileSync(join(assembled.dir, "SKILL.md"), "utf8"));
  const item = assembled.item;
  if (assembled.outType !== "skill" || !item?.invocation) {
    return doc;
  }
  const frontmatter = { ...doc.frontmatter };
  for (const key of CLAUDE_INVOCATION_KEYS) {
    delete frontmatter[key];
  }
  return {
    frontmatter: { ...frontmatter, ...claudeInvocation(item), name: assembled.outName },
    body: doc.body,
  };
}

/** Emit one shared assembled item in Claude Code's native Plugin shape. */
function emitClaudeItem(root: string, assembled: AssembledItem): void {
  const pluginDir = join(root, "plugins", assembled.plugin);
  if (assembled.outType === "skill") {
    const destination = join(pluginDir, "skills", assembled.outName);
    cpSync(assembled.dir, destination, { recursive: true });
    // Original skills were byte-preserving copies before this refactor and remain so. Curated
    // skills are serialized by assembly, then receive only Claude's invocation adaptation here.
    if (!assembled.own) {
      writeFileSync(join(destination, "SKILL.md"), serializeDoc(claudeDocument(assembled)));
    }
    return;
  }

  const kindDir = assembled.outType === "command" ? "commands" : "agents";
  mkdirSync(join(pluginDir, kindDir), { recursive: true });
  writeFileSync(join(pluginDir, kindDir, `${assembled.outName}.md`), readFileSync(join(assembled.dir, "SKILL.md")));
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
): string[] {
  const id = `${plugin}/${outName}`;
  const bless = `npm run eject -- ${plugin} ${outName} --bless`;
  const entry = lock[lockKey(plugin, outName)];
  if (!entry) {
    return [`${id}: overlay is not recorded in overlays/overlays.lock.json — run: ${bless}`];
  }
  if (entry.source !== item.source) {
    return [`${id}: lock was blessed against ${entry.source}, but the item now curates ${item.source} — run: ${bless}`];
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
    return [`${id}: overlay stamp target set changed (${changes.join("; ")}) — run: ${bless}`];
  }
  if (!Object.keys(entry.files).length) {
    return [`${id}: lock records no upstream file, so nothing guards this overlay — run: ${bless}`];
  }
  const drifted = driftedFiles(base, entry);
  if (drifted.length) {
    return [`${id}: upstream changed under the overlay (${drifted.join(", ")}) — review the diff, then: ${bless}`];
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
    return [`${id}: merge sources are not blessed (declared: ${declared}; lock has: ${held}) — run: ${bless} --yes`];
  }
  // Naming the same sources is still not enough once a source can declare its own files: that list
  // grows without the source SET changing, so the check above passes while the lock holds only the
  // older names. Declared, unstamped, guarding nothing — the unblessed-source failure one level down.
  for (const ms of item.merged_from ?? []) {
    const stamped = entry.mergeSources?.[ms.source] ?? {};
    const unstamped = (ms.files ?? []).filter((f) => !(f in stamped));
    if (unstamped.length) {
      return [
        `${id}: merge source ${ms.source} declares ${unstamped.join(", ")}, which the lock does not stamp — run: ${bless} --yes`,
      ];
    }
  }
  // Naming the same sources is not yet a guard. A stamp is taken under the same-filename rule, so an
  // address that shares no file name with the overlay records absence and nothing else — and an
  // absent stamp only speaks when a file APPEARS, which is why blessing such an address succeeds and
  // the guard can then never fire. Re-blessing would only re-record the nulls: the address is wrong.
  const unguarded = Object.entries(entry.mergeSources ?? {})
    .filter(([, files]) => Object.values(files).every((sha) => sha === null))
    .map(([addr]) => addr);
  if (unguarded.length) {
    return unguarded.map(
      (addr) =>
        `${id}: merge source ${addr} shares no filename with the overlay — nothing guards it; wrong address? Fix merged_from, then re-bless`,
    );
  }
  const mergeDrift = driftedMergeSources(root, entry);
  if (mergeDrift.length) {
    return [
      `${id}: merge source changed under the overlay (${mergeDrift.join("; ")}) — review the diff, then: ${bless} --yes`,
    ];
  }
  return [];
}

// Mirrors every throw in emitItem, with the identical wording, so the messages a user sees are the
// same whichever side reports them. The emitItem throws stay as unreachable safety nets.
function collectProblems(root: string, manifests: CurationManifest[], components: ComponentInfo[]): string[] {
  const problems = collectIdentityProblems(root, manifests, components);
  const lock = loadLock(root);
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { comp, outName, outType, overlayDir, id } = resolveItem(root, m.plugin.name, item, components);
      if (!comp) {
        problems.push(`${m.plugin.name}: source not found in external/: ${item.source}`);
        continue;
      }
      if (item.body && !existsSync(overlayDir)) {
        problems.push(
          `${id}: body is ${item.body} but overlays/${id}/ is missing — run: npm run eject -- ${m.plugin.name} ${outName}${item.body === "patch" ? " --patch" : ""}`,
        );
      } else if (item.body === "patch") {
        // A conversion re-serializes frontmatter around a body, so there is no stable file for a
        // diff to land on — those items take a full-file overlay instead (ADR-0001).
        if (outType !== "skill") {
          problems.push(`${id}: body: patch applies to skill output only — a ${outType} needs body: overlay`);
        } else if (!existsSync(join(overlayDir, PATCH_FILE))) {
          problems.push(
            `${id}: body is patch but overlays/${id}/${PATCH_FILE} is missing — run: npm run eject -- ${m.plugin.name} ${outName} --patch`,
          );
        } else {
          problems.push(...overlayDrift(root, item, comp, m.plugin.name, outName, overlayDir, lock));
          // omit runs first, so a pattern that swallows a file the patch edits leaves the hunk
          // nothing to land on. git apply would say so — but only after the output tree was
          // already deleted, which is precisely what this pass exists to prevent.
          const swallowed = patchTargets(readFileSync(join(overlayDir, PATCH_FILE), "utf8")).filter((t) =>
            isOmitted(t, item.omit),
          );
          if (swallowed.length) {
            problems.push(`${id}: omit drops ${swallowed.join(", ")}, which ${PATCH_FILE} edits`);
          }
          // --check writes nothing, so this runs against pristine upstream in the fail-fast pass.
          // No cause is asserted: a patch also stops applying when upstream adopted the same edit,
          // or when the path now sits at or beyond a symlink, and git's own message says which.
          const err = checkPatch(upstreamBase(root, item, comp), join(overlayDir, PATCH_FILE));
          if (err) {
            problems.push(`${id}: ${PATCH_FILE} no longer applies to ${item.source}:\n${err}`);
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
        problems.push(...overlayDrift(root, item, comp, m.plugin.name, outName, overlayDir, lock));
      }
      if (outType === "skill" && comp.type !== "skill") {
        problems.push(`${item.source}: ${comp.type} -> skill conversion not supported`);
      }
    }
  }
  return problems;
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

function rewriteTree(dir: string, map: Map<string, string>, style: RefStyle = "claude"): void {
  if (!existsSync(dir)) {
    return;
  }
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      rewriteTree(p, map, style);
    } else if (e.name.endsWith(".md")) {
      writeFileSync(p, rewriteRefs(readFileSync(p, "utf8"), map, style));
    }
  }
}

/** No silent loss (ADR-0002): whatever the target harness cannot represent is named in the report. */
function reportDropped(label: string, from: Record<string, unknown>, kept: Record<string, unknown>, report: string[]) {
  const dropped = Object.keys(from).filter((k) => !(k in kept));
  if (dropped.length) {
    report.push(`${label}: dropped frontmatter keys: ${dropped.join(", ")}`);
  }
}

/**
 * OpenCode's half of ADR-0005: the dial is which artifact exists, because its skills are model-only
 * by construction and a command is its only user-invocable surface.
 *
 * `manual` therefore emits a command and no skill — but a command is a single file, and the bundled
 * files a skill directory carries would have nowhere to live. Bundled manual items park their parsed
 * body as non-discoverable `BODY.md` under `skills/<name>/`, which OpenCode's discovery ignores
 * (measured; see the research note). The installer is global-only, so commands resolve the global
 * root through XDG_CONFIG_HOME with the documented HOME fallback, and Markdown self-links in the
 * parked bundle are repointed to `BODY.md`.
 */
function emitOpenCodeSkill(moduleRoot: string, assembled: AssembledItem, report: string[]): void {
  const srcDir = assembled.dir;
  const name = assembled.outName;
  const invocation = assembled.item?.invocation;
  const destSkill = join(moduleRoot, "skills", name);
  const wantsSkill = invocation !== "manual";
  // Preserve the exact pre-refactor OpenCode input: its drop reporting observed the pristine
  // Claude-adapted document before reference localization. The body and dependency closure still
  // come from common assembly; this pure view adds no target bytes to that shared state.
  const doc = claudeDocument(assembled);

  cpSync(srcDir, destSkill, {
    recursive: true,
    // `manual` parks the assets but must not leave a SKILL.md, or the item would still be
    // model-reachable — which is the one thing `manual` exists to prevent.
    filter: (src) => wantsSkill || basename(src) !== "SKILL.md",
  });
  if (wantsSkill) {
    // Adapt rather than mirror: OpenCode recognises a fixed set of skill keys and ignores the rest,
    // so a Claude-only key reaching this tree is dead metadata. Dropping it silently is the failure
    // ADR-0002 exists to prevent, so the drop is reported instead.
    const kept = Object.fromEntries(Object.entries(doc.frontmatter).filter(([k]) => OPENCODE_SKILL_KEYS.has(k)));
    reportDropped(`opencode skill ${name}`, doc.frontmatter, kept, report);
    writeFileSync(join(destSkill, "SKILL.md"), serializeDoc({ frontmatter: kept, body: doc.body }));
  } else if (!readdirSync(destSkill).length) {
    rmSync(destSkill, { recursive: true, force: true }); // nothing was bundled; leave no husk
  }

  const bundledManual = !wantsSkill && existsSync(destSkill);
  if (bundledManual) {
    writeFileSync(join(destSkill, "BODY.md"), doc.body);
    for (const file of listFiles(destSkill).filter((f) => f.endsWith(".md"))) {
      const path = join(destSkill, file);
      const body = readFileSync(path, "utf8").replaceAll(
        /(\]\((?:(?:\.\.\/)+|\.\/)?)SKILL\.md(?=[)#?\s])/g,
        "$1BODY.md",
      );
      writeFileSync(path, body);
    }
  }

  if (invocation !== "manual" && invocation !== "both") {
    return;
  }
  const command = { description: doc.frontmatter.description };
  reportDropped(`opencode command ${name}`, doc.frontmatter, command, report);
  const commandBody = bundledManual
    ? [
        // The Module directory is distribution layout only; after installation the body resolves
        // from the OpenCode configuration root, so the stub keeps the installed spelling.
        "Resolve the global OpenCode configuration root as `$XDG_CONFIG_HOME/opencode` when `$XDG_CONFIG_HOME` is set; otherwise use `~/.config/opencode`.",
        `Read \`skills/${name}/BODY.md\` under that global root before doing anything else.`,
        `Follow that file as this command's full instructions.`,
        "",
        `Arguments: $ARGUMENTS`,
      ].join("\n")
    : doc.body;
  mkdirSync(join(moduleRoot, "commands"), { recursive: true });
  writeFileSync(join(moduleRoot, "commands", `${name}.md`), serializeDoc({ frontmatter: command, body: commandBody }));
  const parked = bundledManual ? listFiles(destSkill).filter((f) => f !== "BODY.md") : [];
  if (bundledManual) {
    report.push(`opencode command ${name}: body parked at skills/${name}/BODY.md (bundle: ${parked.join(", ")})`);
  }
}

// OpenCode reads SKILL.md natively, so skills copy verbatim; commands/agents keep only
// the frontmatter OpenCode understands and every dropped key is reported (no silent loss).
// Each plugin becomes one Module bundle: opencode/<plugin>/{skills,commands,agents,manifest.json}.
function emitOpenCode(root: string, manifests: CurationManifest[], assembled: AssembledItem[], report: string[]): void {
  for (const manifest of manifests) {
    const moduleRoot = join(root, "opencode", manifest.plugin.name);
    const pluginItems = assembled.filter((item) => item.plugin === manifest.plugin.name);
    for (const item of pluginItems
      .filter((candidate) => candidate.outType === "skill")
      .sort((left, right) => left.outName.localeCompare(right.outName))) {
      emitOpenCodeSkill(moduleRoot, item, report);
    }

    for (const outKind of ["command", "agent"] as const) {
      const kind = `${outKind}s`;
      const kindItems = pluginItems
        .filter((candidate) => candidate.outType === outKind)
        .sort((left, right) => left.outName.localeCompare(right.outName));
      if (!kindItems.length) {
        continue;
      }
      // `kind` is the output directory (OpenCode documents plural); `outKind` is the singular label
      mkdirSync(join(moduleRoot, kind), { recursive: true });
      for (const item of kindItems) {
        const filename = `${item.outName}.md`;
        const doc = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
        const kept: Record<string, unknown> = { description: doc.frontmatter.description };
        if (outKind === "agent") {
          kept.mode = "subagent";
        }
        const dropped = Object.keys(doc.frontmatter).filter((k) => k !== "description" && k !== "name");
        if (dropped.length) {
          report.push(`opencode ${outKind} ${filename}: dropped frontmatter keys: ${dropped.join(", ")}`);
        }
        writeFileSync(join(moduleRoot, kind, filename), serializeDoc({ frontmatter: kept, body: doc.body }));
      }
    }
  }
}

/**
 * One manifest per curated Module, written only after `rewriteTree` so every hash covers the final
 * bytes. Every manifest gets written — an items: [] Module still has to name itself — and modes
 * split by provenance: copied skill files may carry their committed plugins/MODULE Git index mode
 * (that is where an upstream 100755 lands in the repo), while build-generated documents — commands,
 * agents, parked BODY.md, anything with no plugin counterpart — are always 100644.
 */
function writeOpenCodeManifests(
  root: string,
  manifests: CurationManifest[],
  moduleRequirements: Map<string, string[]>,
): void {
  for (const m of manifests) {
    const moduleRoot = join(root, "opencode", m.plugin.name);
    mkdirSync(moduleRoot, { recursive: true });
    const pluginModes = indexModes(root, [`plugins/${m.plugin.name}/`]);
    const requiredModules = moduleRequirements.get(m.plugin.name);
    if (requiredModules === undefined) {
      throw new Error(`internal error: missing requiredModules for ${m.plugin.name}`);
    }
    const manifest = createModuleManifest(
      moduleRoot,
      m.plugin.name,
      m.plugin.version,
      (path) => {
        if (path.startsWith("commands/") || path.startsWith("agents/") || path.endsWith("/BODY.md")) {
          return "100644";
        }
        return pluginModes.get(`plugins/${m.plugin.name}/${path}`) ?? "100644";
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
