import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { AssembledItem } from "./assemble.ts";
import { codexDisplayName, type CodexPublisherMetadata } from "./codex-plugin.ts";
import { parseDoc, type ParsedDoc, serializeDoc } from "./frontmatter.ts";
import { mappedSources } from "./harness-files.ts";
import { type ComponentType, type CurationManifest, emitsTo, type SkillIntent } from "./manifest.ts";
import { listFiles } from "./overlay.ts";
import { PORTABLE_NAME } from "./resolve.ts";
import { localize, markdownCodeBlocks, type RewriteTarget } from "./rewrite.ts";

const SCHEMA = "https://antigravity.google/schemas/v1/plugin.json";
const INVOCATION_KEY = "disable-model-invocation";
const FORBIDDEN_TITLE_WORD = /\b(?:plugin|antigravity|gemini|claude|cursor|mcp)\b/gi;
export const MAX_ANTIGRAVITY_SKILL_BODY_BYTES = 24_000;
export const ANTIGRAVITY_CONTINUATION = "SKILL-CONTINUATION.md";
export const ANTIGRAVITY_CONTINUATION_LINK = `Read [the remaining instructions](${ANTIGRAVITY_CONTINUATION}) to continue.\n`;

export const ANTIGRAVITY_SKILL_KEYS: ReadonlySet<string> = new Set([
  "name",
  "description",
  INVOCATION_KEY,
  "license",
  "compatibility",
  "icon",
]);

export function createAntigravityPluginManifest(manifest: CurationManifest, publisher: CodexPublisherMetadata) {
  const { name, version, description } = manifest.plugin;
  const displayName = codexDisplayName(name, description).replace(FORBIDDEN_TITLE_WORD, "").replace(/\s+/g, " ").trim();
  return {
    $schema: SCHEMA,
    name,
    displayName: displayName || "Skills",
    version,
    description,
    author: publisher.author,
    ...(publisher.homepage ? { homepage: publisher.homepage } : {}),
    ...(publisher.repository ? { repository: publisher.repository } : {}),
    ...(publisher.license ? { license: publisher.license } : {}),
    keywords: ["antigravity", "skills"],
  };
}

export function antigravityArtifactPath(kind: ComponentType, name: string): string {
  return kind === "agent" ? `agents/${name}.md` : `skills/${name}/SKILL.md`;
}

export function antigravityDisablesModel(
  kind: ComponentType,
  invocation: SkillIntent["invocation"],
  upstream: unknown,
): boolean {
  return kind === "command" || invocation === "manual" || (invocation === undefined && upstream === true);
}

export function adaptAntigravityDocument(
  kind: ComponentType,
  name: string,
  doc: ParsedDoc,
  invocation: SkillIntent["invocation"],
): { document: ParsedDoc; dropped: string[]; transformations: string[] } {
  const frontmatter: Record<string, unknown> = { name, description: doc.frontmatter.description };
  const transformations: string[] = [];
  if (kind === "agent") {
    Object.assign(frontmatter, { mainAgent: false, subagent: true, model: "inherit" });
  } else {
    if (antigravityDisablesModel(kind, invocation, doc.frontmatter[INVOCATION_KEY])) {
      frontmatter[INVOCATION_KEY] = true;
    }
    for (const key of ["license", "compatibility", "icon"]) {
      if (key in doc.frontmatter) {
        frontmatter[key] = doc.frontmatter[key];
      }
    }
    if (kind === "command") {
      transformations.push("command -> manual skill");
    }
  }
  const dropped = Object.keys(doc.frontmatter)
    .filter((key) => (kind === "agent" ? key !== "name" && key !== "description" : !Object.hasOwn(frontmatter, key)))
    .sort();
  return { document: { frontmatter, body: doc.body }, dropped, transformations };
}

/** Antigravity caps the autoloaded body. Keep sections and the relative base for bundled links. */
export function splitAntigravitySkillBody(body: string): { body: string; continuation: string } | undefined {
  if (Buffer.byteLength(body) <= MAX_ANTIGRAVITY_SKILL_BODY_BYTES) {
    return undefined;
  }
  const code = markdownCodeBlocks(body);
  const headings = [...body.matchAll(/^ {0,3}(#{1,6})[ \t]+([^\r\n]+)$/gm)].filter(
    ({ index }) => !code.some(([start, end]) => index >= start && index < end),
  );
  const eligible = headings.filter(
    ({ index }) =>
      index > 0 &&
      /\n\r?\n$/.test(body.slice(0, index)) &&
      Buffer.byteLength(body.slice(0, index) + ANTIGRAVITY_CONTINUATION_LINK) <= MAX_ANTIGRAVITY_SKILL_BODY_BYTES,
  );
  const cut = eligible.findLast((heading) => (heading[1]?.length ?? 0) <= 2)?.index ?? eligible.at(-1)?.index;
  if (cut === undefined) {
    return undefined;
  }

  // Anchors in the old document keep working when their heading moves to the companion.
  const anchors = new Map<string, { moved: boolean; anchor: string }>();
  const used = new Set<string>();
  const tailUsed = new Set<string>();
  const unique = (slug: string, seen: Set<string>): string => {
    let anchor = slug;
    for (let suffix = 1; seen.has(anchor); suffix++) {
      anchor = `${slug}-${suffix}`;
    }
    seen.add(anchor);
    return anchor;
  };
  for (const heading of headings) {
    const slug = (heading[2] ?? "")
      .replace(/[ \t]+#+[ \t]*$/, "")
      .trim()
      .toLowerCase()
      .replace(/[^\p{L}\p{N}\p{M}_ -]/gu, "")
      .replaceAll(" ", "-");
    const anchor = unique(slug, used);
    const moved = heading.index >= cut;
    anchors.set(anchor, { moved, anchor: moved ? unique(slug, tailUsed) : anchor });
  }
  const rewriteAnchors = (text: string, moved: boolean): string =>
    text.replace(/\]\((?:SKILL\.md)?#([^\s)]+)\)/g, (whole, fragment: string) => {
      const target = anchors.get(fragment);
      if (!target) {
        return whole;
      }
      const file = target.moved === moved ? "" : target.moved ? ANTIGRAVITY_CONTINUATION : "SKILL.md";
      return `](${file}#${target.anchor})`;
    });
  // Retargeting a table of contents can increase the measured size.
  const leading = rewriteAnchors(body.slice(0, cut), false) + ANTIGRAVITY_CONTINUATION_LINK;
  if (Buffer.byteLength(leading) > MAX_ANTIGRAVITY_SKILL_BODY_BYTES) {
    return undefined;
  }
  return { body: leading, continuation: rewriteAnchors(body.slice(cut), true) };
}

function windowsSafeSegment(segment: string): boolean {
  return (
    !!segment &&
    !/[<>:"\\|?*]/.test(segment) &&
    !/[ .]$/.test(segment) &&
    ![...segment].some((character) => (character.codePointAt(0) ?? 0) < 32) &&
    !/^(?:con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)/i.test(segment)
  );
}

/** Check each path component, including case aliases in shared directories. */
export function collectAntigravityPathProblems(label: string, paths: Iterable<string>): string[] {
  const problems: string[] = [];
  const seen = new Map<string, string>();
  for (const path of paths) {
    const segments = path.split("/");
    if (segments.some((segment) => !windowsSafeSegment(segment))) {
      problems.push(`${label}/${path}: path is not portable on Windows`);
    }
    let prefix = "";
    for (const segment of segments) {
      prefix = prefix ? `${prefix}/${segment}` : segment;
      const folded = prefix.toLowerCase();
      const prior = seen.get(folded);
      if (prior && prior !== prefix) {
        problems.push(`${label}/${prefix}: case alias of ${prior}`);
      } else {
        seen.set(folded, prefix);
      }
    }
  }
  return [...new Set(problems)];
}

export function isAntigravityHttpsUrl(value: string): boolean {
  const url = URL.parse(value);
  return value.startsWith("https://") && !/\s/.test(value) && !!url && !!url.hostname && !url.username && !url.password;
}

export function collectAntigravityEmissionProblems(
  manifests: CurationManifest[],
  assembled: AssembledItem[],
  publisher: CodexPublisherMetadata,
  rewriteMap: Map<string, RewriteTarget>,
): string[] {
  const problems = collectAntigravityPathProblems(
    "antigravity",
    manifests.map((manifest) => manifest.plugin.name),
  );
  if (publisher.repository && !isAntigravityHttpsUrl(publisher.repository)) {
    problems.push("package.json: Antigravity plugin repository must be an HTTPS URL");
  }
  for (const manifest of manifests) {
    const plugin = manifest.plugin.name;
    const artifacts = new Map<string, AssembledItem>();
    const paths: string[] = [];
    for (const item of assembled.filter(
      (candidate) => candidate.plugin === plugin && emitsTo(candidate.intent, "antigravity"),
    )) {
      const { outName, outType } = item;
      const artifact = antigravityArtifactPath(outType, outName);
      const prior = artifacts.get(artifact.toLowerCase());
      if (prior) {
        problems.push(`${plugin}: Antigravity artifact collision at ${artifact} (${prior.source} and ${item.source})`);
      }
      artifacts.set(artifact.toLowerCase(), item);
      if (!PORTABLE_NAME.test(outName) || !windowsSafeSegment(outName)) {
        problems.push(`${plugin}/${outName}: invalid Antigravity ${outType} name`);
      }
      const doc = parseDoc(readFileSync(join(item.dir, "SKILL.md"), "utf8"));
      const { description } = doc.frontmatter;
      if (typeof description !== "string" || !description.trim()) {
        problems.push(`${plugin}/${outName}: Antigravity ${outType} requires a non-empty description`);
      }
      paths.push(artifact);
      if (outType !== "agent") {
        const files = item.intent.harness_files ?? {};
        const sources = new Set(mappedSources(files));
        const bundled = listFiles(item.dir).filter((file) => !sources.has(file));
        const placed = Object.keys(files).filter((path) => files[path]?.antigravity !== null);
        const adapted = adaptAntigravityDocument(outType, outName, doc, item.intent.invocation);
        const { body } = parseDoc(localize(serializeDoc(adapted.document), rewriteMap, "antigravity"));
        if (Buffer.byteLength(body) > MAX_ANTIGRAVITY_SKILL_BODY_BYTES) {
          if (!splitAntigravitySkillBody(body)) {
            problems.push(
              `${plugin}/${outName}: Antigravity skill body exceeds ${MAX_ANTIGRAVITY_SKILL_BODY_BYTES} bytes; move details into a linked file or add a section boundary before the limit`,
            );
          }
          if (
            [...bundled, ...placed].some(
              (path) => path.split("/")[0]?.toLowerCase() === ANTIGRAVITY_CONTINUATION.toLowerCase(),
            )
          ) {
            problems.push(
              `${plugin}/${outName}: ${ANTIGRAVITY_CONTINUATION} collides with the generated Antigravity continuation`,
            );
          }
        }
        paths.push(...[...bundled, ...placed].map((path) => `skills/${outName}/${path}`));
      }
    }
    problems.push(...collectAntigravityPathProblems(`antigravity/${plugin}`, paths));
  }
  return problems;
}
