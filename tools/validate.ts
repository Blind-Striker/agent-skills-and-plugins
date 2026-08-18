import { existsSync, lstatSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc } from "./lib/frontmatter.ts";
import { indexModes } from "./lib/git.ts";
import { loadManifest } from "./lib/manifest.ts";
import { loadModuleManifest, verifyModuleManifest, type ModuleManifest } from "./lib/opencode-bundle.ts";
import { LOCK_FILE, listFiles, loadLock, PATCH_FILE } from "./lib/overlay.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { extractRefs } from "./lib/refs.ts";
import { collectIdentityProblems, isOmitted, itemRelative, resolveItem, upstreamBase } from "./lib/resolve.ts";
import { scanSubmodule } from "./lib/scan.ts";

export interface Finding {
  level: "error" | "warn";
  message: string;
}

/** Markdown link targets, minus anchors and anything with a scheme (http:, mailto:, file:). */
const MD_LINK = /\[[^\]]*\]\(([^)\s]+)\)/g;
// Exact-address only: these final-tree spellings are CSS declarations, host/file placeholders,
// tracker labels, or Akka topic/path examples — not references to agent artifacts. Never suppress
// their whole namespace: a different address under one of these namespaces must still warn.
const NON_SYMBOL_REF_ADDRESSES = new Set([
  "a:hover",
  "align-items:center",
  "app:app",
  "bl:build",
  "bl:cold-build",
  "bl:first",
  "bl:graph",
  "bl:graph-build",
  "bl:noop-build",
  "bl:perf",
  "bl:second",
  "bl:warm-build",
  "bug:triage",
  "display:flex",
  "file:line",
  "for:timeout",
  "go:build",
  "h1:has-text",
  "host:port",
  "justify-content:center",
  "monitor:latest",
  "myapp:latest",
  "node:test",
  "postgres:alpine",
  "postgres:latest",
  "pp:preprocess",
  "rabbitmq:management-alpine",
  "redis:alpine",
  "system:announcements",
  "warnaserror:nullable",
  "wayfinder:map",
  "workers:rss-poller",
]);
function linkTargets(text: string): string[] {
  const out: string[] = [];
  for (const m of text.matchAll(MD_LINK)) {
    const raw = (m[1] as string).split("#")[0];
    if (raw && !/^[a-z][a-z0-9+.-]*:/i.test(raw)) {
      out.push(raw);
    }
  }
  return out;
}

function* walk(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === ".git") {
      continue;
    }
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      yield* walk(p);
    } else {
      yield p;
    }
  }
}

// walk() cannot see links: a symlinked directory fails isDirectory() and a symlinked file is
// dropped by every caller's .md filter, so the portability check needs its own pass.
function* walkSymlinks(dir: string): Generator<string> {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === ".git") {
      continue;
    }
    const p = join(dir, e.name);
    if (e.isSymbolicLink()) {
      yield p;
    } else if (e.isDirectory()) {
      yield* walkSymlinks(p);
    }
  }
}

function openCodeModuleRoot(root: string, module: string): string {
  return join(root, "opencode", module);
}

function openCodeArtifact(root: string, module: string, kind: "skills" | "commands" | "agents", name: string): string {
  return kind === "skills"
    ? join(openCodeModuleRoot(root, module), "skills", name)
    : join(openCodeModuleRoot(root, module), kind, `${name}.md`);
}

export function validateRepo(root: string): Finding[] {
  requireSubmodules(root);
  const findings: Finding[] = [];
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .sort()
    .map((f) => loadManifest(join(root, "curation", f)));
  const components = readdirSync(join(root, "external"))
    .filter((s) => statSync(join(root, "external", s)).isDirectory())
    .flatMap((s) => scanSubmodule(join(root, "external"), s));
  findings.push(
    ...collectIdentityProblems(root, manifests, components).map((message) => ({ level: "error" as const, message })),
  );
  const firstUse = new Map<string, string>();

  // 1. manifest sources exist, plus the two curation footguns the build resolves silently
  for (const m of manifests) {
    const ownDir = join(root, "skills", m.plugin.name);
    const own = existsSync(ownDir) ? readdirSync(ownDir).filter((n) => statSync(join(ownDir, n)).isDirectory()) : [];
    for (const item of m.items) {
      const { comp, outName, outType } = resolveItem(root, m.plugin.name, item, components);
      if (!comp) {
        findings.push({ level: "error", message: `${m.plugin.name}: unknown source ${item.source}` });
      }
      if (item.exclude) {
        continue;
      }
      const ref = `${m.plugin.name}:${outName}`;
      // 1a. same source curated twice: buildRewriteMap keys on the source, so the last item wins
      // and upstream references to the earlier one silently resolve to the later name.
      const earlier = firstUse.get(item.source);
      if (earlier === undefined) {
        firstUse.set(item.source, ref);
      } else {
        findings.push({
          level: "warn",
          message: `${item.source} is curated twice (as ${earlier} and ${ref}) — the rewrite map is last-write-wins, so upstream references to it all resolve to ${ref}; exclude one item if that is not intended`,
        });
      }
      // 1b. dead frontmatter name: the build forces the output name for skills and agents,
      // and a command is addressed by its file name, so this key never takes effect.
      const declared = item.frontmatter?.name;
      if (typeof declared === "string" && declared !== outName) {
        const fate =
          outType === "command"
            ? `a command is addressed by its file name (${outName}.md), so it is dead metadata`
            : `the build forces name: ${outName} on the output, so it is discarded`;
        findings.push({
          level: "warn",
          message: `${m.plugin.name}/${outName}: item frontmatter.name is "${declared}" but ${fate} — use the item's own name: field to rename it`,
        });
      }
      // 1c. invocation belongs to skill output. A command or an agent is user-invoked by nature
      // (ADR-0005), so the field states an intent neither emitter has anywhere to put.
      if (item.invocation && outType !== "skill") {
        findings.push({
          level: "warn",
          message: `${m.plugin.name}/${outName}: invocation: ${item.invocation} has no effect on a ${outType} — the field applies to skill output only`,
        });
      }
      // 1d. the manifest's own key beats a hand-written frontmatter override of the same thing,
      // silently, so the override is dead weight rather than a second opinion.
      for (const k of ["user-invocable", "disable-model-invocation"]) {
        if (item.invocation && item.frontmatter && k in item.frontmatter) {
          findings.push({
            level: "warn",
            message: `${m.plugin.name}/${outName}: frontmatter.${k} is overwritten by invocation: ${item.invocation} — drop one`,
          });
        }
      }
      // merged_from is a claim about the BODY, and the build reads merge stamps only for an item
      // that declares one — so a declaration without `body:` ships pristine upstream while reading
      // as a guarded merge, which is the overlay-wiring bypass below spelled a second way.
      if (item.merged_from?.length && !item.body) {
        findings.push({
          level: "error",
          message: `${m.plugin.name}/${outName}: merged_from without body: — a merge is a body edit; declare body: overlay|patch or drop merged_from`,
        });
      }
      // An address that is in no submodule has nothing to stamp, so it is a guard over nothing —
      // and the build cannot say so: an unstampable source is simply absent from the lock, which
      // reads exactly like a source nobody declared.
      for (const ms of item.merged_from ?? []) {
        if (!components.some((c) => c.sourcePath === ms.source)) {
          findings.push({
            level: "error",
            message: `${m.plugin.name}/${outName}: merged_from source not found in external/: ${ms.source}`,
          });
          continue;
        }
        // Absence means two different things either side of the filename rule. Under the rule the
        // file list comes from the overlay, so a source that lacks one is recorded absent on
        // purpose and its later appearance is drift. A file a HUMAN named is a claim about where
        // the merge drew from — misspell it and the stamp is null, guarding nothing, with the
        // all-null check silent because the other names stamped fine.
        for (const f of ms.files ?? []) {
          if (!existsSync(join(root, "external", ms.source, f))) {
            findings.push({
              level: "warn",
              message: `${m.plugin.name}/${outName}: merged_from ${ms.source} declares ${f}, which is not there — a stamp over a missing file guards nothing; check the spelling`,
            });
          }
        }
      }
      // L7. own skills are copied last and into the same directory, so one of the same name
      // overwrites the curated item at emit time — and the cross-plugin duplicate check below sees
      // the single surviving directory, never the collision.
      if (own.includes(outName)) {
        findings.push({
          level: "error",
          message: `${m.plugin.name}/${outName}: own skill skills/${m.plugin.name}/${outName}/ silently overwrites this curated item — rename one`,
        });
      }
    }
  }

  // 1c. overlay wiring. The build consults an overlay ONLY when its item says `body:`, so an
  // overlay no item claims is skipped in silence and pristine upstream ships in its place — every
  // hash and patch guard ADR-0001 specifies bypassed by simply never being consulted. That is the
  // one overlay failure the build cannot report, because from its side nothing happened.
  const claimed = new Map<string, { body: "overlay" | "patch" | undefined; exclude: boolean; overlayDir: string }>();
  for (const m of manifests) {
    for (const item of m.items) {
      const { id, overlayDir } = resolveItem(root, m.plugin.name, item, components);
      claimed.set(id, { body: item.body, exclude: item.exclude === true, overlayDir });
    }
  }
  const overlaysDir = join(root, "overlays");
  for (const plugin of existsSync(overlaysDir) ? readdirSync(overlaysDir) : []) {
    const pluginDir = join(overlaysDir, plugin);
    if (!statSync(pluginDir).isDirectory()) {
      continue; // overlays.lock.json lives beside the plugin directories
    }
    for (const name of readdirSync(pluginDir)) {
      if (!statSync(join(pluginDir, name)).isDirectory()) {
        continue;
      }
      const id = `${plugin}/${name}`;
      const entry = claimed.get(id);
      if (!entry) {
        findings.push({
          level: "error",
          message: `overlays/${id}/ matches no curated item — the build never reads it; delete it, or fix the item's name in curation/${plugin}.yaml`,
        });
      } else if (!entry.body && !entry.exclude) {
        // An excluded item is exempt: nothing is emitted for it, so nothing can silently ship.
        findings.push({
          level: "error",
          message: `overlays/${id}/ exists but its item declares no body: — the build ships pristine upstream and every overlay guard is bypassed; add body: overlay or body: patch in curation/${plugin}.yaml`,
        });
      }
    }
  }
  // A lock entry claims a guard over an overlay that is not there. Nothing ships wrong, so this is
  // rot rather than a bypass — but a lock nobody can trace back to a directory is how the next
  // stale entry hides.
  const lock = loadLock(root);
  for (const key of Object.keys(lock)) {
    if (!existsSync(join(overlaysDir, ...key.split("/")))) {
      findings.push({
        level: "warn",
        message: `overlays/${LOCK_FILE} records ${key} but overlays/${key}/ does not exist — drop the entry`,
      });
    }
  }
  // `eject --patch` cuts the patch and then deletes the working copy it came from. Anything left
  // beside overlay.patch is read by nothing, and the next person to edit it is not told so. A
  // warning rather than an error: `--force` re-cutting passes through this state legitimately.
  for (const [id, entry] of claimed) {
    if (entry.body !== "patch" || !existsSync(join(entry.overlayDir, PATCH_FILE))) {
      continue;
    }
    const stranded = listFiles(entry.overlayDir);
    if (stranded.length) {
      findings.push({
        level: "warn",
        message: `overlays/${id}/ holds ${stranded.join(", ")} beside ${PATCH_FILE} — a cut patch leaves no working copy, and the build reads only the patch`,
      });
    }
  }

  const pluginsDir = join(root, "plugins");

  // 1d. omit lists. A conversion reads one file and drops the rest by construction, and a pattern
  // that matches nothing removes nothing — silently, so the file you meant to drop still ships.
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude || !item.omit?.length) {
        continue;
      }
      const { comp, outName, outType, id } = resolveItem(root, m.plugin.name, item, components);
      if (outType !== "skill") {
        findings.push({
          level: "warn",
          message: `${id}: omit has no effect on a ${outType} — the conversion already keeps only the body of ${outName}`,
        });
        continue;
      }
      if (!comp) {
        continue; // already reported as an unknown source
      }
      const base = upstreamBase(root, item, comp);
      const upstream = existsSync(base) ? [...walk(base)].map((f) => itemRelative(base, f)) : [];
      for (const pattern of item.omit) {
        if (!upstream.some((rel) => isOmitted(rel, [pattern]))) {
          findings.push({
            level: "warn",
            message: `${id}: omit pattern ${pattern} matches nothing under ${item.source} — check the spelling, it is dropping no file`,
          });
        }
      }
    }
  }

  // 1e. executable bit. A Windows checkout cannot see it, so a script curated there is committed
  // 100644 while a Linux rebuild produces 0755: CI's freshness gate fails on the mode diff, and the
  // shipped script is not executable for anyone who installs the plugin. git's index is the only
  // place the bit survives such a checkout, so both sides are read from there. Untracked output is
  // skipped — it cannot be wrong yet, and it becomes visible the moment it is staged.
  const ourModes = indexModes(root, ["plugins", "opencode"]);
  const subModes = new Map<string, Map<string, string>>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { comp, outName, outType } = resolveItem(root, m.plugin.name, item, components);
      if (!comp || outType !== "skill") {
        continue;
      }
      const [sub, ...rest] = item.source.split("/");
      if (!sub) {
        continue;
      }
      if (!subModes.has(sub)) {
        subModes.set(sub, indexModes(join(root, "external", sub), []));
      }
      const upstream = subModes.get(sub) ?? new Map();
      const builtDir = join(pluginsDir, m.plugin.name, "skills", outName);
      if (!existsSync(builtDir)) {
        continue;
      }
      for (const file of walk(builtDir)) {
        const rel = itemRelative(builtDir, file);
        if (upstream.get([...rest, rel].join("/")) !== "100755") {
          continue;
        }
        for (const out of [
          `plugins/${m.plugin.name}/skills/${outName}/${rel}`,
          `opencode/${m.plugin.name}/skills/${outName}/${rel}`,
        ]) {
          if (ourModes.has(out) && ourModes.get(out) !== "100755") {
            findings.push({
              level: "error",
              message: `${out}: upstream is executable but this copy is recorded ${ourModes.get(out)} — run: git update-index --chmod=+x ${out}`,
            });
          }
        }
      }
    }
  }

  const outputNames = new Map<string, string>();
  const ownNs = new Set(manifests.map((m) => m.plugin.name));
  const upstreamNs = new Set(components.map((c) => c.namespace));
  for (const m of manifests) {
    upstreamNs.delete(m.plugin.name);
  }

  for (const dir of existsSync(pluginsDir) ? readdirSync(pluginsDir) : []) {
    for (const file of walk(join(pluginsDir, dir))) {
      const rel = relative(root, file).replaceAll("\\", "/");
      // 6. windows-hostile names / length
      if (/[<>:"|?*]/.test(basename(file))) {
        findings.push({ level: "error", message: `${rel}: invalid character for Windows` });
      }
      if (rel.length > 200) {
        findings.push({ level: "warn", message: `${rel}: path longer than 200 chars` });
      }
      if (!file.endsWith(".md")) {
        continue;
      }
      const doc = parseDoc(readFileSync(file, "utf8"));
      // 2. required frontmatter
      if (basename(file) === "SKILL.md") {
        if (!doc.frontmatter.name || !doc.frontmatter.description) {
          findings.push({ level: "error", message: `${rel}: SKILL.md missing name or description` });
        }
        const key = `skill:${String(doc.frontmatter.name)}`;
        if (outputNames.has(key) && outputNames.get(key) !== dir) {
          findings.push({
            level: "error",
            message: `duplicate skill name across plugins: ${String(doc.frontmatter.name)} (${outputNames.get(key)} and ${dir})`,
          });
        }
        outputNames.set(key, dir);
      } else if (/\/(commands|agents)\//.test(rel)) {
        if (!doc.frontmatter.description) {
          findings.push({ level: "error", message: `${rel}: missing description` });
        }
        const kind = rel.includes("/commands/") ? "command" : "agent";
        const key = `${kind}:${basename(file, ".md")}`;
        if (outputNames.has(key) && outputNames.get(key) !== dir) {
          findings.push({
            level: "error",
            message: `duplicate ${kind} name across plugins: ${basename(file, ".md")} (${outputNames.get(key)} and ${dir})`,
          });
        }
        outputNames.set(key, dir);
      }
    }
  }

  // 3. portability: a copied symlink carries an absolute local target, so it dangles in every
  // other clone. Both trees, because both are committed.
  for (const outDir of ["plugins", "opencode"]) {
    const dir = join(root, outDir);
    if (!existsSync(dir)) {
      continue;
    }
    for (const link of walkSymlinks(dir)) {
      findings.push({
        level: "error",
        message: `committed build output must not contain symlinks: ${relative(root, link).replaceAll("\\", "/")}`,
      });
    }
  }

  // 4. leftover upstream references — plugins/ only. opencode/ is rendered from the same bodies, so
  // scanning both reported every leftover twice; the OpenCode tree's own address space is checked
  // by the linker below (L4), where a namespace is a leak rather than a missing rewrite. The scan is
  // the shared one: a leftover is by definition something the rewrite did not take, so the two must
  // agree on what a reference is, or this reports text no rewrite could ever have touched.
  const unknownRefs = new Map<string, { count: number; files: Set<string> }>();
  for (const file of existsSync(pluginsDir) ? walk(pluginsDir) : []) {
    if (!file.endsWith(".md")) {
      continue;
    }
    const rel = relative(root, file).replaceAll("\\", "/");
    for (const ref of extractRefs(readFileSync(file, "utf8"))) {
      if (upstreamNs.has(ref.ns)) {
        findings.push({
          level: "warn",
          message: `${rel}: unrewritten upstream reference ${ref.address} — include it in a manifest or eject and edit the reference out`,
        });
      } else if (!ownNs.has(ref.ns) && !NON_SYMBOL_REF_ADDRESSES.has(ref.address)) {
        const unknown = unknownRefs.get(ref.ns) ?? { count: 0, files: new Set<string>() };
        unknown.count += 1;
        unknown.files.add(rel);
        unknownRefs.set(ref.ns, unknown);
      }
    }
  }
  for (const [ns, { count, files }] of [...unknownRefs].sort(([a], [b]) => a.localeCompare(b))) {
    const examples = [...files].sort().slice(0, 3);
    findings.push({
      level: "warn",
      message: `unknown reference namespace ${ns}: ${count} occurrence${count === 1 ? "" : "s"}; examples: ${examples.join(", ")} — curate the namespace, edit the reference, or allowlist an exact prose address`,
    });
  }

  // Module bundle integrity — before linker analysis so a broken file set is named first.
  const moduleNames = manifests.map((m) => m.plugin.name);
  const expectedModules = new Set(moduleNames);
  const ocDir = join(root, "opencode");
  const actualModules = existsSync(ocDir)
    ? readdirSync(ocDir, { withFileTypes: true })
        .filter((entry) => !entry.isSymbolicLink() && entry.isDirectory())
        .map((entry) => entry.name)
    : [];
  const foldedExpected = new Map<string, string[]>();
  for (const name of moduleNames) {
    const key = name.toLocaleLowerCase("en-US");
    const group = foldedExpected.get(key);
    if (group) {
      group.push(name);
    } else {
      foldedExpected.set(key, [name]);
    }
  }
  for (const group of foldedExpected.values()) {
    if (group.length < 2) {
      continue;
    }
    const sorted = [...group].sort((a, b) => a.localeCompare(b));
    const first = sorted[0];
    if (!first) {
      continue;
    }
    for (const alias of sorted.slice(1)) {
      findings.push({
        level: "error",
        message: `opencode/${alias}: case alias: ${alias} aliases ${first} on a case-insensitive filesystem`,
      });
    }
  }
  for (const dir of actualModules) {
    if (!expectedModules.has(dir)) {
      findings.push({
        level: "error",
        message: `unexpected directory under opencode/: ${dir}`,
      });
    }
  }
  for (const name of moduleNames) {
    if (!actualModules.includes(name)) {
      findings.push({
        level: "error",
        message: `missing Module root opencode/${name}`,
      });
    }
  }
  for (const m of manifests) {
    const moduleRoot = openCodeModuleRoot(root, m.plugin.name);
    let rootStat: ReturnType<typeof lstatSync> | undefined;
    try {
      rootStat = lstatSync(moduleRoot);
    } catch {
      continue;
    }
    if (rootStat.isSymbolicLink() || !rootStat.isDirectory()) {
      continue;
    }
    const manifestPath = join(moduleRoot, "manifest.json");
    if (!existsSync(manifestPath)) {
      findings.push({
        level: "error",
        message: `opencode/${m.plugin.name}/manifest.json is missing`,
      });
      continue;
    }
    let manifest: ModuleManifest;
    try {
      manifest = loadModuleManifest(manifestPath);
    } catch (error) {
      findings.push({
        level: "error",
        message: error instanceof Error ? error.message : String(error),
      });
      continue;
    }
    if (manifest.module !== m.plugin.name) {
      findings.push({
        level: "error",
        message: `opencode/${m.plugin.name}: Module name ${manifest.module} does not match ${m.plugin.name}`,
      });
    }
    if (manifest.version !== m.plugin.version) {
      findings.push({
        level: "error",
        message: `opencode/${m.plugin.name}: Module version ${manifest.version} does not match ${m.plugin.version}`,
      });
    }
    for (const finding of verifyModuleManifest(moduleRoot, manifest, { caseInsensitive: true })) {
      findings.push({
        level: "error",
        message: `opencode/${m.plugin.name}/${finding.path}: ${finding.code.replaceAll("_", " ")}: ${finding.message}`,
      });
    }
  }

  // 4b. reference linking (ADR-0008). plugins/ carries the canonical namespaced text; opencode/ is
  // derived from it, so facts are read once and each tree is checked in its own address space.
  interface TargetState {
    modelReachClaude: boolean;
    userReachClaude: boolean;
    ocSkill: boolean;
    ocCommand: boolean;
  }
  const targetState = new Map<string, TargetState>();
  for (const plugin of existsSync(pluginsDir) ? readdirSync(pluginsDir) : []) {
    const skillsDir = join(pluginsDir, plugin, "skills");
    for (const name of existsSync(skillsDir) ? readdirSync(skillsDir) : []) {
      const doc = parseDoc(readFileSync(join(skillsDir, name, "SKILL.md"), "utf8"));
      targetState.set(name, {
        modelReachClaude: doc.frontmatter["disable-model-invocation"] !== true,
        userReachClaude: doc.frontmatter["user-invocable"] !== false,
        ocSkill: existsSync(join(openCodeArtifact(root, plugin, "skills", name), "SKILL.md")),
        ocCommand: existsSync(openCodeArtifact(root, plugin, "commands", name)),
      });
    }
    for (const kind of ["commands", "agents"] as const) {
      const dir = join(pluginsDir, plugin, kind);
      for (const f of existsSync(dir) ? readdirSync(dir) : []) {
        const name = basename(f, ".md");
        targetState.set(name, {
          modelReachClaude: kind === "commands", // agents are dispatched, not skill-invoked
          userReachClaude: true,
          ocSkill: false,
          ocCommand: existsSync(openCodeArtifact(root, plugin, "commands", name)),
        });
      }
    }
  }

  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { outName, outType, id } = resolveItem(root, m.plugin.name, item, components);
      const built = join(
        pluginsDir,
        m.plugin.name,
        outType === "skill" ? join("skills", outName) : join(`${outType}s`, `${outName}.md`),
      );
      if (!existsSync(built)) {
        continue;
      }
      const files = outType === "skill" ? [...walk(built)].filter((f) => f.endsWith(".md")) : [built];
      const derived = new Set<string>();
      for (const file of files) {
        const rel = relative(root, file).replaceAll("\\", "/");
        for (const ref of extractRefs(readFileSync(file, "utf8"))) {
          if (!ownNs.has(ref.ns)) {
            continue; // upstream-namespace leftovers stay section 4's warning, not the linker's
          }
          const t = targetState.get(ref.name);
          if (!t) {
            findings.push({
              level: "error",
              message: `${rel}: dangling reference ${ref.address} — no built output has that name`,
            });
            continue;
          }
          if (ref.kind === "model") {
            derived.add(ref.name);
            if (!t.modelReachClaude || !t.ocSkill) {
              const cause = !t.modelReachClaude
                ? "disable-model-invocation in the Claude tree"
                : "no opencode/skills entry";
              findings.push({
                level: "error",
                message: `${rel}: model-edge to a target the model cannot reach: ${ref.name} (${cause}) — make the target auto/both, or spell the reference /${ref.address} if the human is the audience`,
              });
            }
          } else if (!t.userReachClaude || !t.ocCommand) {
            const cause = !t.userReachClaude
              ? "user-invocable: false in the Claude tree"
              : "no opencode/commands entry";
            findings.push({
              level: "error",
              message: `${rel}: pointer to a target the user cannot reach: ${ref.name} (${cause}) — make the target manual/both, or drop the slash if the model is the audience`,
            });
          }
        }
      }
      const declared = new Set(item.depends_on ?? []);
      for (const d of derived) {
        if (!declared.has(d)) {
          findings.push({
            level: "error",
            message: `${id}: undeclared dependency: ${d} — add it to depends_on in curation/${m.plugin.name}.yaml`,
          });
        }
      }
      for (const d of declared) {
        if (!derived.has(d)) {
          findings.push({
            level: "error",
            message: `${id}: stale depends_on: ${d} — no model-edge in the shipped body references it`,
          });
        }
      }
    }
  }

  // L4: output namespaces must never reach the OpenCode tree — it has no plugin concept.
  for (const file of existsSync(ocDir) ? [...walk(ocDir)].filter((f) => f.endsWith(".md")) : []) {
    for (const ref of extractRefs(readFileSync(file, "utf8"))) {
      if (ownNs.has(ref.ns)) {
        findings.push({
          level: "error",
          message: `${relative(root, file).replaceAll("\\", "/")}: output namespace leaked into opencode/: ${ref.address}`,
        });
      }
    }
  }

  // L6: a command or parked body naming a parked file that is not there (omitted, renamed) ships a dead path.
  for (const module of moduleNames) {
    const cmdsDir = join(openCodeModuleRoot(root, module), "commands");
    for (const f of existsSync(cmdsDir) ? readdirSync(cmdsDir) : []) {
      const name = basename(f, ".md");
      const parkedDir = openCodeArtifact(root, module, "skills", name);
      if (!existsSync(parkedDir)) {
        continue;
      }
      const parkedFiles = new Set(listFiles(parkedDir));
      const sources = [join(cmdsDir, f), join(parkedDir, "BODY.md")].filter(existsSync);
      const hits = sources.flatMap((source) =>
        [...readFileSync(source, "utf8").matchAll(new RegExp(`skills/${name}/([A-Za-z0-9._/-]+)`, "g"))].map((hit) => ({
          source,
          hit,
        })),
      );
      for (const { source, hit } of hits) {
        const target = (hit[1] as string).replace(/[).,:;'"]+$/, "");
        if (!parkedFiles.has(target)) {
          findings.push({
            level: "error",
            message: `${relative(root, source).replaceAll("\\", "/")}: references skills/${name}/${target}, which is not among the parked files`,
          });
        }
      }
    }
  }

  // L8: the third reference spelling — relative paths (upstream-repo-layouts.md). A namespaced
  // reference is a fact the linker resolves; a bare name is irreducibly heuristic and stays a
  // candidate. A path is neither: resolving it is deterministic. What is NOT deterministic is
  // whether a broken one is our fault — upstream bodies are full of illustrative paths
  // (`./src/ordering/CONTEXT.md`, `FORMS.md`) that never resolved anywhere and never will, and
  // reporting those is the green-build warning nobody reads. So both rules below ask the same
  // narrowing question: could OUR transformation have broken this?
  interface EmittedItem {
    upstream: string;
    manual: boolean;
  }
  const emitted = new Map<string, EmittedItem>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { comp, outName } = resolveItem(root, m.plugin.name, item, components);
      if (comp) {
        emitted.set(outName, { upstream: upstreamBase(root, item, comp), manual: item.invocation === "manual" });
      }
    }
  }
  // A skill is a directory and owns everything under it; a command or an agent is one file and
  // owns nothing, so its same-directory links are the parked-bundle case the build already reports.
  const owningItem = (tree: string, file: string): { name: string; dir: string | null } | null => {
    const p = relative(root, file).replaceAll("\\", "/").split("/");
    if (tree === "plugins" && p[2] === "skills" && p[3]) {
      return { name: p[3], dir: join(root, ...p.slice(0, 4)) };
    }
    if (tree === "opencode" && p[2] === "skills" && p[3]) {
      return { name: p[3], dir: join(root, ...p.slice(0, 4)) };
    }
    const leaf = p.at(-1);
    return leaf?.endsWith(".md") ? { name: basename(leaf, ".md"), dir: null } : null;
  };
  const inside = (parent: string, child: string): boolean =>
    child === parent || !relative(parent, child).startsWith("..");

  for (const tree of ["plugins", "opencode"]) {
    const treeRoot = join(root, tree);
    if (!existsSync(treeRoot)) {
      continue;
    }
    const skillsOf = (name: string): string[] =>
      tree === "plugins"
        ? readdirSync(join(root, "plugins")).map((p) => join(root, "plugins", p, "skills", name))
        : moduleNames.map((module) => openCodeArtifact(root, module, "skills", name));
    for (const file of [...walk(treeRoot)].filter((f) => f.endsWith(".md"))) {
      const own = owningItem(tree, file);
      if (!own) {
        continue;
      }
      const rel = relative(root, file).replaceAll("\\", "/");
      for (const link of linkTargets(readFileSync(file, "utf8"))) {
        const abs = resolve(dirname(file), link);
        if (existsSync(abs)) {
          continue;
        }
        // R1 — a link that CLAIMS another shipped item: it either resolves into that item's
        // directory, or names it in a segment and lands nowhere (a conversion moved the body out
        // of the skill tree, so `../other/` no longer points at anything). Renaming, excluding or
        // omitting the target breaks these silently, and a merge does all three.
        // `../<item>/` is structurally "climb out of my directory into a sibling item's" — the
        // layout both trees have. Requiring the climb is what keeps ordinary words out: an item
        // called `research` matches `../research/x.md` and never `docs/research/x.md`. The target
        // is NOT required to exist first; a target that vanished entirely (excluded, or a husk
        // the emitter removed) is the loudest version of this failure, not an exemption from it.
        const climb = /^(?:\.\.\/)+([^/]+)\//.exec(link)?.[1];
        const claimed = climb && climb !== own.name && emitted.has(climb) ? climb : undefined;
        const landsInOther = [...emitted.keys()]
          .filter((n) => n !== own.name)
          .some((n) => skillsOf(n).some((d) => existsSync(d) && inside(d, abs)));
        if (claimed || landsInOther) {
          // A command is one file in a different directory, so a path written for a skill tree
          // cannot resolve from it — and no single spelling serves both copies of a `both` item.
          // Where the SKILL copy resolves the very same link, the reference is sound and the
          // conversion is what broke it; the right spelling depends on which mount point the
          // install supports, which is a parked decision (ROADMAP), so this is named, not failed.
          const skillCopy = own.dir === null ? (skillsOf(own.name).find(existsSync) ?? null) : null;
          const soundInSkillTree = skillCopy !== null && existsSync(resolve(skillCopy, link));
          findings.push({
            level: soundInSkillTree ? "warn" : "error",
            message: soundInSkillTree
              ? `${rel}: relative reference ${link} resolves from skills/${own.name}/ but not from a converted command — the conversion moved the body out of the skill tree`
              : `${rel}: relative reference ${link} does not resolve in ${tree}/ — the target item was renamed, excluded, omitted, or is unreachable from this artifact's location`,
          });
          continue;
        }
        // R2 — a link into the item's OWN directory that upstream can still satisfy. If the same
        // path is absent upstream too, it is upstream's illustrative prose and none of our
        // business; if upstream has it, we are the ones who removed it.
        if (!own.dir || !inside(own.dir, abs)) {
          continue;
        }
        const info = emitted.get(own.name);
        const within = relative(own.dir, abs).replaceAll("\\", "/");
        if (!info || !existsSync(join(info.upstream, within))) {
          continue;
        }
        // `manual` deliberately withholds the OpenCode SKILL.md so the model cannot reach the item
        // (ADR-0005). The parked file's link is dead all the same, and only an emitter decision
        // fixes it — so it is named rather than treated as a mistake.
        const designed = tree === "opencode" && within === "SKILL.md" && info.manual;
        findings.push({
          level: designed ? "warn" : "error",
          message: designed
            ? `${rel}: links to ${link}, which manual withholds from this tree — the parked bundle keeps a dead link to its own SKILL.md`
            : `${rel}: links to ${link}, which this build dropped from the item though ${own.name} still ships it upstream — an omit or a conversion took a file the body names`,
        });
      }
    }
  }

  // 5. marketplace consistency
  const mpPath = join(root, ".claude-plugin", "marketplace.json");
  if (!existsSync(mpPath)) {
    findings.push({ level: "error", message: ".claude-plugin/marketplace.json missing — run npm run build" });
  } else {
    const mp = JSON.parse(readFileSync(mpPath, "utf8")) as { plugins: { name: string }[] };
    const listed = new Set(mp.plugins.map((p) => p.name));
    const built = new Set(existsSync(pluginsDir) ? readdirSync(pluginsDir) : []);
    for (const p of listed) {
      if (!built.has(p)) {
        findings.push({ level: "error", message: `marketplace lists ${p} but plugins/${p} does not exist` });
      }
    }
    for (const p of built) {
      if (!listed.has(p)) {
        findings.push({ level: "error", message: `plugins/${p} exists but is not in marketplace.json` });
      }
    }
  }

  // 7. provenance: the curation layer stamps no names, no dates — git carries who and when.
  // Scope is the authored layer only: yaml COMMENT segments (values keep their branding),
  // overlay bodies, a patch's added lines (context lines are upstream's), own skills.
  const BANNED = [/\bDeniz\b/, /\bIrgin\b/, /\b20\d{2}-\d{2}-\d{2}\b/];
  const provenance = (text: string, where: string): void => {
    for (const re of BANNED) {
      const m = re.exec(text);
      if (m) {
        findings.push({
          level: "error",
          message: `${where}: the curation layer stamps no names or dates — git carries provenance (found "${m[0]}")`,
        });
      }
    }
  };
  for (const f of readdirSync(join(root, "curation")).filter((n) => n.endsWith(".yaml"))) {
    readFileSync(join(root, "curation", f), "utf8")
      .split("\n")
      .forEach((line, i) => {
        const hash = line.indexOf("#");
        if (hash >= 0) {
          provenance(line.slice(hash), `curation/${f}:${i + 1}`);
        }
      });
  }
  for (const base of ["overlays", "skills"]) {
    const dir = join(root, base);
    if (!existsSync(dir)) {
      continue;
    }
    for (const file of walk(dir)) {
      const rel = relative(root, file).replaceAll("\\", "/");
      if (rel.endsWith(LOCK_FILE)) {
        continue;
      }
      const text = readFileSync(file, "utf8");
      if (rel.endsWith(".patch")) {
        text.split("\n").forEach((line, i) => {
          if (line.startsWith("+") && !line.startsWith("+++")) {
            provenance(line, `${rel}:${i + 1}`);
          }
        });
      } else {
        provenance(text, rel);
      }
    }
  }

  return findings;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const findings = validateRepo(process.cwd());
  for (const f of findings) {
    console.log(`${f.level.toUpperCase()}: ${f.message}`);
  }
  const errors = findings.filter((f) => f.level === "error").length;
  console.log(`${errors} error(s), ${findings.length - errors} warning(s)`);
  if (errors) {
    process.exit(1);
  }
}
