import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { basename, join, relative } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc, serializeDoc } from "./lib/frontmatter.ts";
import { OPENCODE_SKILL_KEYS, writeLedger } from "./lib/ledger.ts";
import { type CurationItem, type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import {
  applyPatch,
  checkPatch,
  driftedFiles,
  driftedMergeSources,
  listFiles,
  loadLock,
  lockKey,
  type OverlayLock,
  PATCH_FILE,
  patchTargets,
} from "./lib/overlay.ts";
import { isOmitted, itemRelative, resolveItem, upstreamBase } from "./lib/resolve.ts";
import { buildRewriteMap, rewriteRefs } from "./lib/rewrite.ts";
import { type ComponentInfo, scanSubmodule } from "./lib/scan.ts";

export function buildAll(root: string): string[] {
  requireSubmodules(root);
  const report: string[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
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

  rmSync(join(root, "plugins"), { recursive: true, force: true });
  rmSync(join(root, "opencode"), { recursive: true, force: true });

  for (const m of manifests) {
    for (const item of m.items) {
      emitItem(root, m, item, components, report);
    }
    emitOwnSkills(root, m, report);
    const pluginDir = join(root, "plugins", m.plugin.name);
    mkdirSync(join(pluginDir, ".claude-plugin"), { recursive: true });
    writeFileSync(join(pluginDir, ".claude-plugin", "plugin.json"), `${JSON.stringify(m.plugin, null, 2)}\n`);
  }

  writeMarketplace(root, manifests);
  // Output name -> stated intent. Own skills and items that state nothing are absent from the map,
  // and an absent entry means "a plain skill", which is what OpenCode makes of silence anyway.
  const invocations = new Map<string, NonNullable<CurationItem["invocation"]>>();
  for (const m of manifests) {
    for (const item of m.items) {
      const { outName, outType } = resolveItem(root, m.plugin.name, item, components);
      if (!item.exclude && item.invocation && outType === "skill") {
        invocations.set(outName, item.invocation);
      }
    }
  }
  // OpenCode is emitted from the PRISTINE plugin tree, before the Claude rewrite, so each tree can
  // then be rewritten with the spelling its own harness resolves (ADR-0006 axis 3). Emitting after
  // the rewrite is what left OpenCode carrying `<plugin>:<name>` references it cannot resolve.
  emitOpenCode(root, invocations, report);
  rewriteTree(join(root, "plugins"), buildRewriteMap(manifests, components, "claude"));
  rewriteTree(join(root, "opencode"), buildRewriteMap(manifests, components, "opencode"));
  writeLedger(root, manifests, components);
  return report;
}

/** The single file emitItem reads out of an overlay when the target is a command or an agent. */
function overlayBodyFile(comp: ComponentInfo, item: CurationItem): string {
  return comp.type === "skill" ? "SKILL.md" : basename(item.source);
}

/**
 * Upstream files an item leaves behind. Applied to the copy of upstream, BEFORE any overlay or
 * patch, so `body:` describes edits to what survives rather than racing against a later deletion.
 * A directory is kept whenever anything under it is kept; emptied ones are pruned afterwards.
 */
function omitFilter(srcRoot: string, item: CurationItem): (src: string) => boolean {
  if (!item.omit?.length) {
    return () => true;
  }
  return (src) => {
    const rel = itemRelative(srcRoot, src);
    return rel === "" || !isOmitted(rel, item.omit);
  };
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

/** cpSync still creates a directory whose every child was filtered out; it should not survive. */
function pruneEmptyDirs(dir: string): void {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (!e.isDirectory()) {
      continue;
    }
    const p = join(dir, e.name);
    pruneEmptyDirs(p);
    if (!readdirSync(p).length) {
      rmSync(p, { recursive: true, force: true });
    }
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
  if (!Object.keys(entry.files).length) {
    return [`${id}: lock records no upstream file, so nothing guards this overlay — run: ${bless}`];
  }
  const drifted = driftedFiles(upstreamBase(root, item, comp), entry);
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
  const problems: string[] = [];
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
          problems.push(...overlayDrift(root, item, comp, m.plugin.name, outName, lock));
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
        problems.push(...overlayDrift(root, item, comp, m.plugin.name, outName, lock));
      }
      if (outType === "skill" && comp.type !== "skill") {
        problems.push(`${item.source}: ${comp.type} -> skill conversion not supported`);
      }
    }
  }
  return problems;
}

// cpSync copies a symlink as a symlink but resolves its target to an ABSOLUTE local path, so a
// copied link is machine-specific and dangles in every other clone. Committed build output must be
// plain files: skip links (a rejected directory link skips its subtree) and report every skip.
function skipSymlinks(root: string, label: string, report: string[]): (src: string) => boolean {
  return (src) => {
    if (!lstatSync(src).isSymbolicLink()) {
      return true;
    }
    const rel = relative(root, src).replaceAll("\\", "/");
    report.push(`WARN ${label}: skipped symlink ${rel.startsWith("..") ? basename(src) : rel}`);
    return false;
  };
}

function emitItem(
  root: string,
  m: CurationManifest,
  item: CurationItem,
  components: ComponentInfo[],
  report: string[],
): void {
  if (item.exclude) {
    return;
  }
  const { comp, outName, outType, overlayDir } = resolveItem(root, m.plugin.name, item, components);
  if (!comp) {
    throw new Error(`${m.plugin.name}: source not found in external/: ${item.source}`);
  }
  const srcPath = join(root, "external", item.source);
  const pluginDir = join(root, "plugins", m.plugin.name);

  if (item.body && !existsSync(overlayDir)) {
    throw new Error(
      `${m.plugin.name}/${outName}: body is ${item.body} but overlays/${m.plugin.name}/${outName}/ is missing — run: npm run eject -- ${m.plugin.name} ${outName}`,
    );
  }

  if (outType === "skill") {
    if (comp.type !== "skill") {
      throw new Error(`${item.source}: ${comp.type} -> skill conversion not supported`);
    }
    const destDir = join(pluginDir, "skills", outName);
    const filter = skipSymlinks(root, `${m.plugin.name}/${outName}`, report);
    const keep = omitFilter(srcPath, item);
    cpSync(srcPath, destDir, { recursive: true, filter: (src) => filter(src) && keep(src) });
    if (item.omit?.length) {
      pruneEmptyDirs(destDir);
    }
    if (item.body === "overlay") {
      cpSync(overlayDir, destDir, { recursive: true, force: true, filter });
    } else if (item.body === "patch") {
      // collectProblems already --check'd this against pristine upstream; a failure here means the
      // copy above differs from what was checked, so report it rather than emit a half-patched item.
      const err = applyPatch(destDir, join(overlayDir, PATCH_FILE));
      if (err) {
        throw new Error(`${m.plugin.name}/${outName}: ${PATCH_FILE} failed to apply:\n${err}`);
      }
    }
    const skillMd = join(destDir, "SKILL.md");
    const doc = parseDoc(readFileSync(skillMd, "utf8"));
    const merged: Record<string, unknown> = { ...doc.frontmatter, ...item.frontmatter };
    // A stated intent replaces whatever upstream said, so its keys go before one is written back.
    // Absent states nothing, and upstream's own keys survive untouched (ADR-0005).
    if (item.invocation) {
      for (const k of CLAUDE_INVOCATION_KEYS) {
        delete merged[k];
      }
    }
    // forced name last: emitted dir names and the rewrite map both key on outName
    doc.frontmatter = { ...merged, ...claudeInvocation(item), name: outName };
    writeFileSync(skillMd, serializeDoc(doc));
    report.push(`${m.plugin.name}: skill ${outName} <- ${item.source}${item.body === "overlay" ? " (overlay)" : ""}`);
  } else {
    const srcFile = comp.type === "skill" ? join(srcPath, "SKILL.md") : srcPath;
    let doc = parseDoc(readFileSync(srcFile, "utf8"));
    if (item.body === "overlay") {
      doc = parseDoc(readFileSync(join(overlayDir, overlayBodyFile(comp, item)), "utf8"));
    }
    if (comp.type === "skill") {
      const extras = readdirSync(srcPath).filter((f) => f !== "SKILL.md");
      if (extras.length) {
        report.push(`WARN ${m.plugin.name}/${outName}: dropped in skill->${outType} conversion: ${extras.join(", ")}`);
      }
    }
    const base: Record<string, unknown> =
      outType === "command"
        ? { description: String(doc.frontmatter.description ?? "") }
        : { name: outName, description: String(doc.frontmatter.description ?? "") };
    const forcedName: Record<string, unknown> = outType === "agent" ? { name: outName } : {};
    doc = { frontmatter: { ...base, ...item.frontmatter, ...forcedName }, body: doc.body };
    const kindDir = outType === "command" ? "commands" : "agents";
    mkdirSync(join(pluginDir, kindDir), { recursive: true });
    writeFileSync(join(pluginDir, kindDir, `${outName}.md`), serializeDoc(doc));
    report.push(`${m.plugin.name}: ${outType} ${outName} <- ${item.source}`);
  }
}

function emitOwnSkills(root: string, m: CurationManifest, report: string[]): void {
  const ownDir = join(root, "skills", m.plugin.name);
  if (!existsSync(ownDir)) {
    return;
  }
  for (const name of readdirSync(ownDir)) {
    if (!statSync(join(ownDir, name)).isDirectory()) {
      continue;
    }
    cpSync(join(ownDir, name), join(root, "plugins", m.plugin.name, "skills", name), {
      recursive: true,
      filter: skipSymlinks(root, `${m.plugin.name}/${name}`, report),
    });
    report.push(`${m.plugin.name}: skill ${name} <- skills/ (own)`);
  }
}

function writeMarketplace(root: string, manifests: CurationManifest[]): void {
  const marketplace = {
    name: "deniz-skills",
    owner: { name: "Deniz Irgin", email: "denizirgin@gmail.com" },
    plugins: manifests.map((m) => ({
      name: m.plugin.name,
      source: `./plugins/${m.plugin.name}`,
      description: m.plugin.description,
    })),
  };
  mkdirSync(join(root, ".claude-plugin"), { recursive: true });
  writeFileSync(join(root, ".claude-plugin", "marketplace.json"), `${JSON.stringify(marketplace, null, 2)}\n`);
}

function rewriteTree(dir: string, map: Map<string, string>): void {
  if (!existsSync(dir)) {
    return;
  }
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      rewriteTree(p, map);
    } else if (e.name.endsWith(".md")) {
      writeFileSync(p, rewriteRefs(readFileSync(p, "utf8"), map));
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
 * files a skill directory carries would have nowhere to live. They are parked under `skills/<name>/`
 * without a `SKILL.md`, which OpenCode's discovery ignores (measured; see the research note), so the
 * command body can still reach them. Their references are not rewritten yet: the spelling depends on
 * which mount point is supported, which is an open curation decision, so the drop is reported.
 */
function emitOpenCodeSkill(
  root: string,
  srcDir: string,
  name: string,
  invocation: NonNullable<CurationItem["invocation"]> | undefined,
  report: string[],
): void {
  const filter = skipSymlinks(root, `opencode/${name}`, report);
  const destSkill = join(root, "opencode", "skills", name);
  const wantsSkill = invocation !== "manual";
  const doc = parseDoc(readFileSync(join(srcDir, "SKILL.md"), "utf8"));

  cpSync(srcDir, destSkill, {
    recursive: true,
    // `manual` parks the assets but must not leave a SKILL.md, or the item would still be
    // model-reachable — which is the one thing `manual` exists to prevent.
    filter: (src) => filter(src) && (wantsSkill || basename(src) !== "SKILL.md"),
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

  if (invocation !== "manual" && invocation !== "both") {
    return;
  }
  const command = { description: doc.frontmatter.description };
  reportDropped(`opencode command ${name}`, doc.frontmatter, command, report);
  mkdirSync(join(root, "opencode", "commands"), { recursive: true });
  writeFileSync(
    join(root, "opencode", "commands", `${name}.md`),
    serializeDoc({ frontmatter: command, body: doc.body }),
  );
  // Parking is what `manual` costs, so only `manual` reports it. A `both` item's directory is a
  // live discoverable skill carrying its own SKILL.md — calling that "parked" made most of this
  // warning class false, and a report more than half wrong is one nobody reads. The command copy's
  // bundle references are unrewritten there too, but the same body is reachable through the skill,
  // where OpenCode resolves them natively; what is left is the out-of-project read (ROADMAP).
  const parked = !wantsSkill && existsSync(destSkill) ? listFiles(destSkill) : [];
  if (parked.length) {
    report.push(
      `WARN opencode command ${name}: bundled files parked at skills/${name}/ (${parked.join(", ")}) — body references to them are not rewritten`,
    );
  }
}

// OpenCode reads SKILL.md natively, so skills copy verbatim; commands/agents keep only
// the frontmatter OpenCode understands and every dropped key is reported (no silent loss).
function emitOpenCode(
  root: string,
  invocations: Map<string, NonNullable<CurationItem["invocation"]>>,
  report: string[],
): void {
  const pluginsDir = join(root, "plugins");
  if (!existsSync(pluginsDir)) {
    return;
  }
  for (const plugin of readdirSync(pluginsDir)) {
    const skillsDir = join(pluginsDir, plugin, "skills");
    if (existsSync(skillsDir)) {
      for (const name of readdirSync(skillsDir)) {
        emitOpenCodeSkill(root, join(skillsDir, name), name, invocations.get(name), report);
      }
    }
    for (const kind of ["commands", "agents"] as const) {
      const dir = join(pluginsDir, plugin, kind);
      if (!existsSync(dir)) {
        continue;
      }
      // `kind` is the output directory (OpenCode documents plural); `outKind` is the singular label
      const outKind = kind === "commands" ? "command" : "agent";
      mkdirSync(join(root, "opencode", kind), { recursive: true });
      for (const f of readdirSync(dir)) {
        const doc = parseDoc(readFileSync(join(dir, f), "utf8"));
        const kept: Record<string, unknown> = { description: doc.frontmatter.description };
        if (outKind === "agent") {
          kept.mode = "subagent";
        }
        const dropped = Object.keys(doc.frontmatter).filter((k) => k !== "description" && k !== "name");
        if (dropped.length) {
          report.push(`opencode ${outKind} ${f}: dropped frontmatter keys: ${dropped.join(", ")}`);
        }
        writeFileSync(join(root, "opencode", kind, f), serializeDoc({ frontmatter: kept, body: doc.body }));
      }
    }
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  for (const line of buildAll(process.cwd())) {
    console.log(line);
  }
  console.log("Build complete.");
}
