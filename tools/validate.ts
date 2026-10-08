import { existsSync, lstatSync, readdirSync, readFileSync, statSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { isDeepStrictEqual } from "node:util";
import { parse as parseYaml } from "yaml";
import {
  CODEX_SKILL_KEYS,
  createCodexMarketplace,
  createCodexPluginManifest,
  createCodexSkillAgentManifest,
  loadCodexPublisherMetadata,
  MAX_CODEX_SKILL_DESCRIPTION_LENGTH,
  resolveCodexRepositoryPath,
} from "./lib/codex-plugin.ts";
import { parseDoc } from "./lib/frontmatter.ts";
import { indexModes } from "./lib/git.ts";
import { type ComponentType, type CurationManifest, loadManifest } from "./lib/manifest.ts";
import {
  findMissingModuleRequirements,
  loadModuleManifest,
  verifyModuleManifest,
  type ModuleManifest,
} from "./lib/opencode-bundle.ts";
import {
  OPENCODE_AGENT_COLOR,
  OPENCODE_AGENT_KEYS,
  OPENCODE_HIDE_KEY,
  openCodeBundlePath,
  openCodeId,
  splitOpenCodeId,
} from "./lib/opencode-target.ts";
import { ownSkillIdentities } from "./lib/own-skills.ts";
import { LOCK_FILE, listFiles, loadLock, PATCH_FILE } from "./lib/overlay.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { extractRefs, scanRefs } from "./lib/refs.ts";
import {
  collectIdentityProblems,
  deriveModuleRequirements,
  isOmitted,
  itemRelative,
  PORTABLE_NAME,
  resolveItem,
  upstreamBase,
} from "./lib/resolve.ts";
import { type ComponentInfo, scanSubmodule } from "./lib/scan.ts";

export interface Finding {
  level: "error" | "warn";
  message: string;
}

/** Markdown link targets, minus anchors and anything with a scheme (http:, mailto:, file:). */
const MD_LINK = /\[[^\]]*\]\(([^)\s]+)\)/g;
// Exact-address only: these final-tree spellings are CSS declarations, host/file placeholders,
// CLI query fields, search qualifiers, image tags, script/module names, tracker labels, or Akka
// topic/path examples — not references to agent artifacts. Never suppress their whole namespace:
// a different address under one of these namespaces must still warn.
const NON_SYMBOL_REF_ADDRESSES = new Set([
  "a:hover",
  "active:properties",
  "align-items:center",
  "app:app",
  "app:main",
  "azurite:latest",
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
  "fqdn:fqdn",
  "fqdn:properties",
  "go:build",
  "h1:has-text",
  "host:port",
  "id:id",
  "justify-content:center",
  "key:value",
  "location:location",
  "mailcatcher:latest",
  "main:app",
  "monitor:latest",
  "my-app:latest",
  "myapp:latest",
  "name:name",
  "node:test",
  "postgres:alpine",
  "postgres:latest",
  "pp:preprocess",
  "rabbitmq:management-alpine",
  "redis:alpine",
  "resource:api",
  "severity:debug",
  "severity:error",
  "start:dev",
  "state:properties",
  "state:provisioning",
  "state:state",
  "status:error",
  "system:announcements",
  "system:redis",
  "timestamp:properties",
  "type:type",
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
  return join(
    openCodeModuleRoot(root, module),
    openCodeBundlePath(kind === "skills" ? "skill" : kind === "commands" ? "command" : "agent", module, name),
  );
}

interface OpenCodeIndexEntry {
  plugin: string;
  name: string;
  kind: ComponentType;
  hidden: boolean;
}

/**
 * Every emitted OpenCode artifact, keyed by its `<plugin>.<name>` ID: curated items at their resolved
 * kind, plus original skills. `hidden` reads the emitted SKILL.md, so it reflects the hide key as
 * shipped rather than as curation intends it.
 */
function openCodeIndex(
  root: string,
  manifests: CurationManifest[],
  components: ComponentInfo[],
): Map<string, OpenCodeIndexEntry> {
  const index = new Map<string, OpenCodeIndexEntry>();
  const add = (plugin: string, name: string, kind: ComponentType): void => {
    const artifact = openCodeArtifact(root, plugin, `${kind}s`, name);
    const file = kind === "skill" ? join(artifact, "SKILL.md") : artifact;
    if (!existsSync(file)) {
      return;
    }
    const metadata = kind === "skill" ? parseDoc(readFileSync(file, "utf8")).frontmatter.metadata : undefined;
    const hidden =
      typeof metadata === "object" &&
      metadata !== null &&
      !Array.isArray(metadata) &&
      (metadata as Record<string, unknown>)[OPENCODE_HIDE_KEY] === false;
    index.set(openCodeId(plugin, name), { plugin, name, kind, hidden });
  };
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { outName, outType } = resolveItem(root, m.plugin.name, item, components);
      add(m.plugin.name, outName, outType);
    }
  }
  for (const own of ownSkillIdentities(root, manifests)) {
    add(own.plugin, own.name, "skill");
  }
  return index;
}

export interface OpenCodeIdToken {
  prefix: "" | "@" | "/";
  id: string;
  line: number;
}

// The characters that make a candidate token part of a larger word or path rather than a rendered
// ID: a "/" prefix only counts after a non-path character, and an unprefixed or "@" token must not
// continue a word, a path, or a mention.
const BEFORE_SLASH_PATH = /[A-Za-z0-9._/-]/;
const BEFORE_TOKEN = /[A-Za-z0-9._/@-]/;

/**
 * Rendered OpenCode IDs (`<plugin>.<name>`, optionally `@`- or `/`-prefixed) in `text`. A token that
 * continues into `/` is a path segment and is left to the path rules; so is one preceded by a path
 * character. A trailing `.` does not end the token's eligibility, so a standalone
 * `deniz-process.yaml` in prose is a token on purpose.
 */
export function scanOpenCodeIds(text: string, plugins: string[]): OpenCodeIdToken[] {
  if (plugins.length === 0) {
    return [];
  }
  const alternation = plugins.map((p) => p.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")).join("|");
  const token = new RegExp(`([@/]?)((?:${alternation})\\.[a-z0-9]+(?:-[a-z0-9]+)*)(?![A-Za-z0-9_/-])`, "g");
  const out: OpenCodeIdToken[] = [];
  for (const m of text.matchAll(token)) {
    const prefix = (m[1] ?? "") as OpenCodeIdToken["prefix"];
    const id = m[2] as string;
    const before = m.index > 0 ? (text[m.index - 1] as string) : "";
    if (before && (prefix === "/" ? BEFORE_SLASH_PATH : BEFORE_TOKEN).test(before)) {
      continue;
    }
    out.push({ prefix, id, line: text.slice(0, m.index).split("\n").length });
  }
  return out;
}

// The three measured skill-tool phrasings: `with "x"`, `twice, for "x" and "y"`, `for "x"`. Other
// phrasings are not detected; that is a stated limit, not a claim of completeness.
const SKILL_TOOL_HANDLE = /Skill tool(?: twice,)? (?:with|for) ("[^"\n]+"(?:,? (?:and|or) "[^"\n]+")*)/gi;

/** Quoted skill-tool handles on one line, in order. */
export function skillToolHandles(line: string): string[] {
  const out: string[] = [];
  for (const m of line.matchAll(SKILL_TOOL_HANDLE)) {
    for (const q of (m[1] as string).matchAll(/"([^"\n]+)"/g)) {
      out.push(q[1] as string);
    }
  }
  return out;
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
  const ownSkills = ownSkillIdentities(root, manifests);
  let expectedRequirements: Map<string, string[]> | undefined;
  try {
    expectedRequirements = deriveModuleRequirements(root, manifests, components, ownSkills);
  } catch (error) {
    findings.push({
      level: "error",
      message: error instanceof Error ? error.message : String(error),
    });
  }
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
      // 1c. The manifest's own key beats a hand-written frontmatter override of the same thing,
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
  const ourModes = indexModes(root, ["plugins", "opencode", "codex"]);
  const subModes = new Map<string, Map<string, string>>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { comp, outName, outType } = resolveItem(root, m.plugin.name, item, components);
      if (!comp) {
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
      const builtDir = join(root, "codex", m.plugin.name, "skills", outName);
      if (!existsSync(builtDir)) {
        continue;
      }
      for (const file of walk(builtDir)) {
        const rel = itemRelative(builtDir, file);
        if (upstream.get([...rest, rel].join("/")) !== "100755") {
          continue;
        }
        const outputs = [
          `codex/${m.plugin.name}/skills/${outName}/${rel}`,
          ...(outType === "skill"
            ? [
                `plugins/${m.plugin.name}/skills/${outName}/${rel}`,
                `opencode/${m.plugin.name}/skills/${openCodeId(m.plugin.name, outName)}/${rel}`,
              ]
            : []),
        ];
        for (const out of outputs) {
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
  for (const outDir of ["plugins", "opencode", "codex"]) {
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
  // scanning both reported every leftover twice; the OpenCode tree speaks dotted `<plugin>.<name>`
  // IDs, so a surviving `namespace:name` there is a leak the linker reports (L4), not a missing
  // rewrite. The scan is
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
  const loadedModules = Object.create(null) as Record<string, { requiredModules: readonly string[] }>;
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
    loadedModules[manifest.module] = { requiredModules: manifest.requiredModules };
    const expected = expectedRequirements?.get(m.plugin.name);
    if (
      expected !== undefined &&
      (manifest.requiredModules.length !== expected.length ||
        manifest.requiredModules.some((name, index) => name !== expected[index]))
    ) {
      findings.push({
        level: "error",
        message: `opencode/${m.plugin.name}: required Modules do not match curation`,
      });
    }
    for (const finding of verifyModuleManifest(moduleRoot, manifest, { caseInsensitive: true })) {
      findings.push({
        level: "error",
        message: `opencode/${m.plugin.name}/${finding.path}: ${finding.code.replaceAll("_", " ")}: ${finding.message}`,
      });
    }
  }
  for (const missing of findMissingModuleRequirements(loadedModules)) {
    findings.push({
      level: "error",
      message: `${missing.module} requires ${missing.requiredModule}`,
    });
  }

  // 4b. reference linking (ADR-0008). plugins/ carries the canonical namespaced text; opencode/ is
  // derived from it, so facts are read once and each target's reach is checked in both harnesses.
  // OpenCode 2 gives every item one artifact at its ID whatever its invocation: the model reaches a
  // skill unless it carries the hide key, and a user can attach any skill or agent or run a command.
  const ocIndex = openCodeIndex(root, manifests, components);
  interface TargetState {
    modelReachClaude: boolean;
    userReachClaude: boolean;
    ocModel: boolean;
    ocUser: boolean;
    ocKind?: ComponentType;
  }
  const ocState = (plugin: string, name: string): Pick<TargetState, "ocModel" | "ocUser" | "ocKind"> => {
    const entry = ocIndex.get(openCodeId(plugin, name));
    return entry
      ? { ocModel: entry.kind === "skill" && !entry.hidden, ocUser: true, ocKind: entry.kind }
      : { ocModel: false, ocUser: false };
  };
  const targetState = new Map<string, TargetState>();
  for (const plugin of existsSync(pluginsDir) ? readdirSync(pluginsDir) : []) {
    const skillsDir = join(pluginsDir, plugin, "skills");
    for (const name of existsSync(skillsDir) ? readdirSync(skillsDir) : []) {
      const doc = parseDoc(readFileSync(join(skillsDir, name, "SKILL.md"), "utf8"));
      targetState.set(name, {
        modelReachClaude: doc.frontmatter["disable-model-invocation"] !== true,
        userReachClaude: doc.frontmatter["user-invocable"] !== false,
        ...ocState(plugin, name),
      });
    }
    for (const kind of ["commands", "agents"] as const) {
      const dir = join(pluginsDir, plugin, kind);
      for (const f of existsSync(dir) ? readdirSync(dir) : []) {
        const name = basename(f, ".md");
        targetState.set(name, {
          modelReachClaude: kind === "commands", // agents are dispatched, not skill-invoked
          userReachClaude: true,
          ...ocState(plugin, name),
        });
      }
    }
  }
  const ocModelCause = (t: TargetState): string =>
    !t.ocUser
      ? "no OpenCode artifact"
      : t.ocKind === "skill"
        ? `hidden from the OpenCode model (${OPENCODE_HIDE_KEY})`
        : `an OpenCode ${t.ocKind ?? "artifact"}, not a skill`;

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
            if (!t.modelReachClaude || !t.ocModel) {
              const cause = !t.modelReachClaude ? "disable-model-invocation in the Claude tree" : ocModelCause(t);
              findings.push({
                level: "error",
                message: `${rel}: model-edge to a target the model cannot reach: ${ref.name} (${cause}) — make the target auto/both, or spell the reference /${ref.address} if the human is the audience`,
              });
            }
          } else if (!t.userReachClaude || !t.ocUser) {
            const cause = !t.userReachClaude ? "user-invocable: false in the Claude tree" : "no OpenCode artifact";
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

  // L4: output namespaces must never reach the OpenCode tree — it addresses items by dotted ID.
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

  // O1, O2, O4–O6: the OpenCode tree addresses items by dotted ID, so every rendered ID, every
  // skill-tool handle, and every artifact's shape is checked against what was actually emitted.
  const kindLabel = { "": "item", "@": "skill or agent", "/": "command" } as const;
  const prefixFits = (prefix: OpenCodeIdToken["prefix"], kind: ComponentType): boolean =>
    prefix === "" || (prefix === "@" ? kind === "skill" || kind === "agent" : kind === "command");
  for (const file of existsSync(ocDir) ? [...walk(ocDir)].filter((f) => f.endsWith(".md")) : []) {
    const rel = relative(root, file).replaceAll("\\", "/");
    const text = readFileSync(file, "utf8");
    // O1: every rendered ID names an emitted artifact whose kind its prefix can address.
    for (const token of scanOpenCodeIds(text, moduleNames)) {
      const entry = ocIndex.get(token.id);
      if (!entry || !prefixFits(token.prefix, entry.kind)) {
        findings.push({
          level: "error",
          message: `${rel}:${token.line}: rendered OpenCode ID ${token.prefix}${token.id} does not name an emitted ${kindLabel[token.prefix]}`,
        });
      }
    }
    // O2: a skill-tool handle is only callable when it is an emitted OpenCode skill ID.
    for (const [index, line] of text.split("\n").entries()) {
      for (const handle of skillToolHandles(line)) {
        if (ocIndex.get(handle)?.kind !== "skill") {
          findings.push({
            level: "error",
            message: `${rel}:${index + 1}: skill-tool handle "${handle}" is not an emitted OpenCode skill ID — author it as a namespaced fact with a matching depends_on`,
          });
        }
      }
    }
  }
  for (const module of moduleNames) {
    const skillsDir = join(openCodeModuleRoot(root, module), "skills");
    if (existsSync(skillsDir) && statSync(skillsDir).isDirectory()) {
      for (const entry of readdirSync(skillsDir, { withFileTypes: true })) {
        const at = `opencode/${module}/skills/${entry.name}`;
        // O4: OpenCode discovers skills/*.md and every nested SKILL.md, so either is a phantom skill.
        if (!entry.isDirectory()) {
          if (entry.name.endsWith(".md")) {
            findings.push({
              level: "error",
              message: `${at}: phantom OpenCode skill: a Markdown file directly under skills/`,
            });
          }
          continue;
        }
        const folder = join(skillsDir, entry.name);
        for (const nested of walk(folder)) {
          const within = relative(folder, nested).replaceAll("\\", "/");
          if (basename(nested) === "SKILL.md" && within !== "SKILL.md") {
            findings.push({
              level: "error",
              message: `${at}/${within}: phantom OpenCode skill: a SKILL.md below the skill folder`,
            });
          }
        }
        // O5: the folder is an ID of its own Module, and the skill's name is that ID.
        const bare = entry.name.startsWith(`${module}.`) ? entry.name.slice(module.length + 1) : undefined;
        if (bare === undefined || !PORTABLE_NAME.test(bare)) {
          findings.push({
            level: "error",
            message: `${at}: skill folder ${entry.name} is not an OpenCode ID of Module ${module}`,
          });
        }
        const skillFile = join(folder, "SKILL.md");
        if (existsSync(skillFile)) {
          const name = parseDoc(readFileSync(skillFile, "utf8")).frontmatter.name;
          if (name !== entry.name) {
            findings.push({
              level: "error",
              message: `${at}/SKILL.md: skill name ${String(name)} does not equal its ID ${entry.name}`,
            });
          }
        }
      }
    }
    // O6: agents carry only native OpenCode 2 keys; any other key routes the file through the
    // OpenCode 1 migrator, and a non-hex color is rejected outright.
    const agentsDir = join(openCodeModuleRoot(root, module), "agents");
    for (const f of existsSync(agentsDir) ? readdirSync(agentsDir).filter((n) => n.endsWith(".md")) : []) {
      const at = `opencode/${module}/agents/${f}`;
      const frontmatter = parseDoc(readFileSync(join(agentsDir, f), "utf8")).frontmatter;
      for (const key of Object.keys(frontmatter)) {
        if (!OPENCODE_AGENT_KEYS.has(key)) {
          findings.push({ level: "error", message: `${at}: non-native OpenCode agent key ${key}` });
        }
      }
      const color = frontmatter.color;
      if (color !== undefined && (typeof color !== "string" || !OPENCODE_AGENT_COLOR.test(color))) {
        findings.push({ level: "error", message: `${at}: OpenCode agent color ${String(color)} is not #rrggbb` });
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
  }
  const emitted = new Map<string, EmittedItem>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { comp, outName } = resolveItem(root, m.plugin.name, item, components);
      if (comp) {
        emitted.set(outName, { upstream: upstreamBase(root, item, comp) });
      }
    }
  }
  // A skill is a directory and owns everything under it; a command or an agent is one file and
  // owns nothing, so it has no same-directory files for a link to name. OpenCode spells each folder
  // and file as `<plugin>.<name>`; the owner is always reported by its bare name.
  const bareName = (tree: string, segment: string): string =>
    tree === "opencode" ? (splitOpenCodeId(segment, moduleNames)?.name ?? segment) : segment;
  const owningItem = (tree: string, file: string): { name: string; dir: string | null } | null => {
    const p = relative(root, file).replaceAll("\\", "/").split("/");
    if (p[2] === "skills" && p[3]) {
      return { name: bareName(tree, p[3]), dir: join(root, ...p.slice(0, 4)) };
    }
    const leaf = p.at(-1);
    return leaf?.endsWith(".md") ? { name: bareName(tree, basename(leaf, ".md")), dir: null } : null;
  };
  // The installed OpenCode layout shares one skills/ directory across Modules, so a climb out of
  // opencode/<m>/skills/<id>/ into <m2>.<x>/ lands in Module m2's folder, not in m's.
  const reRootOpenCode = (abs: string): string => {
    const [module, skills, folder, ...rest] = relative(join(root, "opencode"), abs).replaceAll("\\", "/").split("/");
    if (!module || skills !== "skills" || !folder) {
      return abs;
    }
    const owner = splitOpenCodeId(folder, moduleNames)?.plugin;
    return owner && owner !== module ? join(openCodeModuleRoot(root, owner), "skills", folder, ...rest) : abs;
  };
  const ownerModule = new Map<string, string>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (!item.exclude) {
        ownerModule.set(resolveItem(root, m.plugin.name, item, components).outName, m.plugin.name);
      }
    }
  }
  const inside = (parent: string, child: string): boolean =>
    child === parent || !relative(parent, child).startsWith("..");

  for (const tree of ["plugins", "opencode", "codex"]) {
    const treeRoot = join(root, tree);
    if (!existsSync(treeRoot)) {
      continue;
    }
    const skillsOf = (name: string): string[] => {
      if (tree === "plugins") {
        return readdirSync(join(root, "plugins")).map((p) => join(root, "plugins", p, "skills", name));
      }
      if (tree === "opencode") {
        const owner = ownerModule.get(name);
        return owner ? [openCodeArtifact(root, owner, "skills", name)] : [];
      }
      return moduleNames.map((plugin) => join(root, "codex", plugin, "skills", name));
    };
    for (const file of [...walk(treeRoot)].filter((f) => f.endsWith(".md"))) {
      const own = owningItem(tree, file);
      if (!own) {
        continue;
      }
      const rel = relative(root, file).replaceAll("\\", "/");
      for (const link of linkTargets(readFileSync(file, "utf8"))) {
        const resolved = resolve(dirname(file), link);
        const abs = tree === "opencode" ? reRootOpenCode(resolved) : resolved;
        if (existsSync(abs)) {
          continue;
        }
        // R1 — a link that CLAIMS another shipped item: it either resolves into that item's
        // directory, or names it in a segment and lands nowhere (a conversion moved the body out
        // of the skill tree, so `../other/` no longer points at anything). Renaming, excluding or
        // omitting the target breaks these silently, and a merge does all three.
        // `../<item>/` is structurally "climb out of my directory into a sibling item's" — the
        // layout every tree has. Requiring the climb is what keeps ordinary words out: an item
        // called `research` matches `../research/x.md` and never `docs/research/x.md`. The target
        // is NOT required to exist first; a target that vanished entirely (excluded, or omitted)
        // is the loudest version of this failure, not an exemption from it. An OpenCode climb names
        // the ID folder, so its segment is read back to the bare name.
        const climbSegment = /^(?:\.\.\/)+([^/]+)\//.exec(link)?.[1];
        const climb = climbSegment === undefined ? undefined : bareName(tree, climbSegment);
        const claimed = climb && climb !== own.name && emitted.has(climb) ? climb : undefined;
        const landsInOther = [...emitted.keys()]
          .filter((n) => n !== own.name)
          .some((n) => skillsOf(n).some((d) => existsSync(d) && inside(d, abs)));
        if (claimed || landsInOther) {
          findings.push({
            level: "error",
            message: `${rel}: relative reference ${link} does not resolve in ${tree}/ — the target item was renamed, excluded, omitted, or is unreachable from this artifact's location`,
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
        // Every tree ships each skill's own SKILL.md (OpenCode 2 hides a manual skill through
        // frontmatter rather than withholding the file), so a dead own-folder link is always ours.
        findings.push({
          level: "error",
          message: `${rel}: links to ${link}, which this build dropped from the item though ${own.name} still ships it upstream — an omit or a conversion took a file the body names`,
        });
      }
    }
  }

  // Codex Plugin integrity. Unlike Claude's permissive marketplace projection, this is an owned
  // generated contract: curation, plugin roots, manifests, skills, policies, and marketplace must
  // agree exactly in both directions.
  const codexRoot = join(root, "codex");
  const expectedPlugins = new Set(moduleNames);
  const actualCodexPlugins = existsSync(codexRoot)
    ? readdirSync(codexRoot, { withFileTypes: true })
        .filter((entry) => !entry.isSymbolicLink() && entry.isDirectory())
        .map((entry) => entry.name)
    : [];
  for (const plugin of actualCodexPlugins) {
    if (!expectedPlugins.has(plugin)) {
      findings.push({ level: "error", message: `unexpected directory under codex/: ${plugin}` });
    }
  }
  for (const plugin of moduleNames) {
    if (!actualCodexPlugins.includes(plugin)) {
      findings.push({ level: "error", message: `missing Codex Plugin root codex/${plugin}` });
    }
  }

  let publisher: ReturnType<typeof loadCodexPublisherMetadata> | undefined;
  try {
    publisher = loadCodexPublisherMetadata(root);
  } catch (error) {
    findings.push({ level: "error", message: error instanceof Error ? error.message : String(error) });
  }
  const qualifiedCodexSkills = new Set<string>();
  for (const m of manifests) {
    const pluginRoot = join(codexRoot, m.plugin.name);
    if (!existsSync(pluginRoot)) {
      continue;
    }
    const pluginManifestPath = join(pluginRoot, ".codex-plugin", "plugin.json");
    if (!existsSync(pluginManifestPath)) {
      findings.push({ level: "error", message: `codex/${m.plugin.name}/.codex-plugin/plugin.json is missing` });
    } else {
      try {
        const actual = JSON.parse(readFileSync(pluginManifestPath, "utf8")) as unknown;
        if (publisher && !isDeepStrictEqual(actual, createCodexPluginManifest(m, publisher))) {
          findings.push({
            level: "error",
            message: `codex/${m.plugin.name}/.codex-plugin/plugin.json does not match curation and repository metadata`,
          });
        }
      } catch (error) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/.codex-plugin/plugin.json is invalid JSON: ${error instanceof Error ? error.message : String(error)}`,
        });
      }
    }

    const expectedSkills = new Map<string, { invocation: CurationManifest["items"][number]["invocation"] }>();
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const { outName } = resolveItem(root, m.plugin.name, item, components);
      const folded = outName.toLowerCase();
      if (expectedSkills.has(folded)) {
        findings.push({
          level: "error",
          message: `${m.plugin.name}: duplicate or case-colliding flattened Codex skill ${outName}`,
        });
      } else {
        expectedSkills.set(folded, { invocation: item.invocation });
      }
    }
    const ownDir = join(root, "skills", m.plugin.name);
    for (const name of existsSync(ownDir) ? readdirSync(ownDir) : []) {
      if (!statSync(join(ownDir, name)).isDirectory()) {
        continue;
      }
      const folded = name.toLowerCase();
      if (expectedSkills.has(folded)) {
        findings.push({
          level: "error",
          message: `${m.plugin.name}: duplicate or case-colliding flattened Codex skill ${name}`,
        });
      } else {
        expectedSkills.set(folded, { invocation: undefined });
      }
    }

    const skillsRoot = join(pluginRoot, "skills");
    const actualSkills = existsSync(skillsRoot)
      ? readdirSync(skillsRoot, { withFileTypes: true })
          .filter((entry) => !entry.isSymbolicLink() && entry.isDirectory())
          .map((entry) => entry.name)
      : [];
    const actualFolded = new Map<string, string>();
    for (const name of actualSkills) {
      const folded = name.toLowerCase();
      const alias = actualFolded.get(folded);
      if (alias) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/skills: case alias ${name} aliases ${alias}`,
        });
      } else {
        actualFolded.set(folded, name);
      }
      if (!/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(name) || name.length > 64) {
        findings.push({ level: "error", message: `codex/${m.plugin.name}/skills/${name}: invalid Codex skill name` });
      }
      if (!expectedSkills.has(folded)) {
        findings.push({ level: "error", message: `unexpected Codex skill codex/${m.plugin.name}/skills/${name}` });
      }
    }
    for (const [folded] of expectedSkills) {
      if (!actualFolded.has(folded)) {
        findings.push({ level: "error", message: `missing Codex skill codex/${m.plugin.name}/skills/${folded}` });
      }
    }

    for (const [folded, expected] of expectedSkills) {
      const name = actualFolded.get(folded);
      if (!name) {
        continue;
      }
      qualifiedCodexSkills.add(`${m.plugin.name}:${name}`);
      const skillRoot = join(skillsRoot, name);
      const skillPath = join(skillRoot, "SKILL.md");
      if (!existsSync(skillPath)) {
        findings.push({ level: "error", message: `codex/${m.plugin.name}/skills/${name}/SKILL.md is missing` });
        continue;
      }
      const doc = parseDoc(readFileSync(skillPath, "utf8"));
      if (
        doc.frontmatter.name !== name ||
        typeof doc.frontmatter.description !== "string" ||
        !doc.frontmatter.description
      ) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/skills/${name}/SKILL.md must have matching name and non-empty description`,
        });
      }
      if (
        typeof doc.frontmatter.description === "string" &&
        doc.frontmatter.description.length > MAX_CODEX_SKILL_DESCRIPTION_LENGTH
      ) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/skills/${name}/SKILL.md description exceeds ${MAX_CODEX_SKILL_DESCRIPTION_LENGTH} characters`,
        });
      }
      const unsupported = Object.keys(doc.frontmatter)
        .filter((key) => !CODEX_SKILL_KEYS.has(key))
        .sort();
      if (unsupported.length) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/skills/${name}/SKILL.md has unsupported frontmatter: ${unsupported.join(", ")}`,
        });
      }

      const policyPath = join(skillRoot, "agents", "openai.yaml");
      const expectedPolicy = createCodexSkillAgentManifest(
        m.plugin.name,
        name,
        String(doc.frontmatter.description ?? ""),
        expected.invocation,
      );
      if (!expectedPolicy && existsSync(policyPath)) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/skills/${name}/agents/openai.yaml must be absent for ${expected.invocation ?? "target-default"} invocation`,
        });
      } else if (expectedPolicy && !existsSync(policyPath)) {
        findings.push({
          level: "error",
          message: `codex/${m.plugin.name}/skills/${name}/agents/openai.yaml is required for manual invocation`,
        });
      } else if (expectedPolicy) {
        try {
          const actualPolicy = parseYaml(readFileSync(policyPath, "utf8")) as unknown;
          if (!isDeepStrictEqual(actualPolicy, expectedPolicy)) {
            findings.push({
              level: "error",
              message: `codex/${m.plugin.name}/skills/${name}/agents/openai.yaml does not match manual invocation policy`,
            });
          }
        } catch (error) {
          findings.push({
            level: "error",
            message: `codex/${m.plugin.name}/skills/${name}/agents/openai.yaml is invalid YAML: ${error instanceof Error ? error.message : String(error)}`,
          });
        }
      }
    }
  }

  // Codex reference validation happens on native `$` tokens. Semantic model/pointer kinds remain
  // owned by the neutral scan and ledger; both intentionally render to the same Codex spelling.
  const codexRef = /\$([a-z][a-z0-9-]*):([a-z][a-z0-9-]*)/g;
  for (const file of existsSync(codexRoot) ? [...walk(codexRoot)].filter((path) => path.endsWith(".md")) : []) {
    const content = readFileSync(file, "utf8");
    const rel = relative(root, file).replaceAll("\\", "/");
    if (/\/\$[a-z][a-z0-9-]*:[a-z][a-z0-9-]*/.test(content)) {
      findings.push({ level: "error", message: `${rel}: invalid Codex pointer spelling /$` });
    }
    for (const match of content.matchAll(codexRef)) {
      const address = `${match[1]}:${match[2]}`;
      if (!qualifiedCodexSkills.has(address)) {
        findings.push({ level: "error", message: `${rel}: dangling Codex reference $${address}` });
      }
    }
    for (const ref of scanRefs(content)) {
      if ((ownNs.has(ref.ns) || upstreamNs.has(ref.ns)) && content[ref.index - 1] !== "$") {
        findings.push({ level: "error", message: `${rel}: unrendered Codex reference ${ref.address}` });
      }
    }
  }

  for (const file of existsSync(codexRoot) ? walk(codexRoot) : []) {
    const rel = relative(root, file).replaceAll("\\", "/");
    if (/[<>:"|?*]/.test(basename(file))) {
      findings.push({ level: "error", message: `${rel}: invalid character for Windows` });
    }
    if (rel.length > 200) {
      findings.push({ level: "warn", message: `${rel}: path longer than 200 chars` });
    }
  }

  const codexMarketplacePath = join(root, ".agents", "plugins", "marketplace.json");
  if (!existsSync(codexMarketplacePath)) {
    findings.push({ level: "error", message: ".agents/plugins/marketplace.json missing — run npm run build" });
  } else {
    try {
      const marketplace = JSON.parse(readFileSync(codexMarketplacePath, "utf8")) as {
        plugins?: Array<{ name?: string; source?: { path?: string } }>;
      };
      if (!isDeepStrictEqual(marketplace, createCodexMarketplace(manifests))) {
        findings.push({
          level: "error",
          message: ".agents/plugins/marketplace.json does not match curation and Codex plugin roots",
        });
      }
      for (const entry of marketplace.plugins ?? []) {
        if (typeof entry.source?.path !== "string") {
          findings.push({
            level: "error",
            message: `Codex marketplace entry ${entry.name ?? "<unnamed>"} has no source.path`,
          });
          continue;
        }
        try {
          const destination = resolveCodexRepositoryPath(root, entry.source.path);
          if (!existsSync(destination)) {
            findings.push({
              level: "error",
              message: `Codex marketplace lists ${entry.name ?? "<unnamed>"} but ${entry.source.path} does not exist`,
            });
          }
        } catch (error) {
          findings.push({ level: "error", message: error instanceof Error ? error.message : String(error) });
        }
      }
    } catch (error) {
      findings.push({
        level: "error",
        message: `.agents/plugins/marketplace.json is invalid JSON: ${error instanceof Error ? error.message : String(error)}`,
      });
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
