import { readFileSync } from "node:fs";
import { isAbsolute, resolve, sep } from "node:path";
import type { AssembledItem } from "./assemble.ts";
import { parseDoc, type ParsedDoc } from "./frontmatter.ts";
import { type CurationManifest, emitsTo, type SkillIntent } from "./manifest.ts";
import { PORTABLE_NAME } from "./resolve.ts";
import { inlineCodeSpans } from "./rewrite.ts";

export const CODEX_MARKETPLACE_NAME = "deniz-skills";
export const CODEX_MARKETPLACE_DISPLAY_NAME = "Deniz Skills";
export const CODEX_PLUGIN_CATEGORY = "Productivity";
export const CODEX_PLUGIN_CAPABILITIES = ["Read", "Write"] as const;
export const MAX_CODEX_SKILL_NAME_LENGTH = 64;
export const CODEX_SKILL_KEYS = new Set(["name", "description", "license", "allowed-tools", "metadata"]);

export interface CodexInvocationResolution {
  implicit: "enabled" | "disabled" | "target-default";
  explicit: "available";
  agentManifest: boolean;
}

export interface CodexSkillAgentManifest {
  interface: { display_name: string; short_description: string; default_prompt: string };
  policy: { allow_implicit_invocation: false };
}

export interface CodexPublisherMetadata {
  author: { name: string; email?: string; url?: string };
  homepage?: string;
  repository?: string;
  license?: string;
}

export interface CodexPluginManifest {
  name: string;
  version: string;
  description: string;
  author: CodexPublisherMetadata["author"];
  homepage?: string;
  repository?: string;
  license?: string;
  keywords: string[];
  skills: "./skills/";
  interface: {
    displayName: string;
    shortDescription: string;
    longDescription: string;
    developerName: string;
    category: string;
    capabilities: string[];
    websiteURL?: string;
    defaultPrompt: string[];
  };
}

export interface CodexMarketplace {
  name: string;
  interface: { displayName: string };
  plugins: Array<{
    name: string;
    source: { source: "local"; path: string };
    policy: { installation: "AVAILABLE"; authentication: "ON_INSTALL" };
    category: string;
  }>;
}

interface PackageJson {
  author?: string | { name?: string; email?: string; url?: string };
  homepage?: string;
  repository?: string | { url?: string };
  license?: string;
}

/** Canonical generated JSON: stable property insertion order, two spaces, and one final newline. */
export function serializeCodexJson(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}

export function resolveCodexInvocation(invocation: SkillIntent["invocation"]): CodexInvocationResolution {
  switch (invocation) {
    case "manual":
      return { implicit: "disabled", explicit: "available", agentManifest: true };
    case "auto":
    case "both":
      return { implicit: "enabled", explicit: "available", agentManifest: false };
    default:
      return { implicit: "target-default", explicit: "available", agentManifest: false };
  }
}

/**
 * Keep only frontmatter Codex loads; target-specific passengers are reported by the caller. The
 * description passes through whole: the shared length rule (skill-description.ts) fails the build
 * instead of letting one harness carry different trigger text.
 */
export function adaptCodexSkillDocument(name: string, doc: ParsedDoc): { document: ParsedDoc; dropped: string[] } {
  const frontmatter: Record<string, unknown> = {
    name,
    description: String(doc.frontmatter.description ?? ""),
  };
  for (const key of ["license", "allowed-tools", "metadata"] as const) {
    if (key in doc.frontmatter) {
      frontmatter[key] = doc.frontmatter[key];
    }
  }
  return {
    document: { frontmatter, body: doc.body },
    dropped: Object.keys(doc.frontmatter)
      .filter((key) => !CODEX_SKILL_KEYS.has(key))
      .sort(),
  };
}

/** The longest `interface.short_description` the emitter writes, the Codex guideline. */
const SHORT_DESCRIPTION_LIMIT = 64;
/** A cut ends with an ellipsis, which takes one character, so the text before it keeps one less. */
const CUT_LIMIT = SHORT_DESCRIPTION_LIMIT - 1;
/** The shortest text a clause cut may leave, so a label still says something. */
const MEANINGFUL_LABEL = 25;
/** A sentence ends at `.`, `!`, or `?` before the end of the text or before a capital. */
const SENTENCE_END = /[.!?](?=$|\s+[A-Z])/g;
/**
 * Markdown emphasis, which a plain-text menu label would show literally: `**x**`, `__x__`, `*x*`, and
 * `_x_`. A delimiter touches no letter or digit outside it and no whitespace inside it, so the `_`
 * of `snake_case` and the `*` of `a*b*c` are not emphasis.
 */
const EMPHASIS = [
  /(?<![\p{L}\p{N}*])\*\*(?![\s*])(.+?)(?<![\s*])\*\*(?![\p{L}\p{N}*])/gu,
  /(?<![\p{L}\p{N}_])__(?![\s_])(.+?)(?<![\s_])__(?![\p{L}\p{N}_])/gu,
  /(?<![\p{L}\p{N}*])\*(?![\s*])(.+?)(?<![\s*])\*(?![\p{L}\p{N}*])/gu,
  /(?<![\p{L}\p{N}_])_(?![\s_])(.+?)(?<![\s_])_(?![\p{L}\p{N}_])/gu,
];
/** Stands in for an inline code span while emphasis is unwrapped around it. */
const CODE_SLOT = /(\d+)/g;
const CLAUSE_BOUNDARIES = new Set([",", ";", ":", "—"]);
/** Words a cut label must not end on, because they promise a continuation the label lacks. */
const FUNCTION_WORDS = new Set([
  "by",
  "to",
  "and",
  "or",
  "for",
  "of",
  "the",
  "a",
  "an",
  "with",
  "in",
  "on",
  "from",
  "through",
  "when",
  "that",
  "which",
  "into",
  "as",
  "at",
  "via",
]);
const TRAILING_PUNCTUATION = /[\s,;:—–([-]+$/;

/** Bracket depth before each index: `depth[i]` is how many `(` or `[` are open before `text[i]`. */
function bracketDepths(text: string): number[] {
  const depths: number[] = [];
  let depth = 0;
  for (let i = 0; i < text.length; i++) {
    const ch = text.charAt(i);
    depths.push(depth);
    if (ch === "(" || ch === "[") {
      depth++;
    } else if ((ch === ")" || ch === "]") && depth > 0) {
      depth--;
    }
  }
  depths.push(depth);
  return depths;
}

/**
 * The text before the last clause boundary outside brackets, with trailing function words dropped,
 * that fits and still says something.
 */
function clauseCut(text: string): string | undefined {
  const depths = bracketDepths(text);
  let cut: string | undefined;
  for (let i = 0; i < text.length; i++) {
    if (!CLAUSE_BOUNDARIES.has(text.charAt(i)) || depths[i] !== 0) {
      continue;
    }
    const lead = trimCut(text.slice(0, i));
    if (lead.length > CUT_LIMIT) {
      break;
    }
    if (lead.length >= MEANINGFUL_LABEL) {
      cut = lead;
    }
  }
  return cut;
}

/** Drop trailing punctuation and function words until the cut ends on a word that carries meaning. */
function trimCut(words: string): string {
  let text = words.replace(TRAILING_PUNCTUATION, "");
  for (;;) {
    const space = text.lastIndexOf(" ");
    if (space <= 0 || !FUNCTION_WORDS.has(text.slice(space + 1).toLowerCase())) {
      return text;
    }
    text = text.slice(0, space).replace(TRAILING_PUNCTUATION, "");
  }
}

/**
 * Whole words that fit, cut before the bracket the cut would leave open however little precedes
 * it; empty when that bracket opens the text.
 */
function wordCut(text: string): string {
  const wordEnd = text.lastIndexOf(" ", CUT_LIMIT);
  const words = wordEnd > 0 ? text.slice(0, wordEnd) : text.slice(0, CUT_LIMIT);
  const depths = bracketDepths(words);
  if (depths.at(-1) !== 0) {
    // The bracket still open at the cut is the last one opened from depth zero.
    const open = depths.findLastIndex((depth, i) => depth === 0 && depths[i + 1] !== 0);
    return trimCut(words.slice(0, open));
  }
  return trimCut(words);
}

/**
 * A description as a plain-text label shows it: an inline code span loses its backticks and keeps
 * its content whole, and emphasis outside code loses its delimiters.
 */
function plainText(description: string): string {
  const code: string[] = [];
  let text = "";
  let cut = 0;
  for (const [start, end] of inlineCodeSpans(description)) {
    const ticks = /^`+/.exec(description.slice(start, end))?.[0].length ?? 1;
    text += `${description.slice(cut, start)}${code.length}`;
    code.push(description.slice(start + ticks, end - ticks));
    cut = end;
  }
  text = EMPHASIS.reduce((out, re) => out.replace(re, "$1"), text + description.slice(cut));
  return text.replace(CODE_SLOT, (_slot, index: string) => code[Number(index)] ?? "");
}

/**
 * The menu label of a manual skill, measured without Markdown markers: the whole description when
 * it fits, else as many whole sentences as fit, else the text before the last clause boundary that
 * fits outside brackets, else whole words, both with trailing function words dropped. A cut that
 * does not end a sentence ends with an ellipsis, so no label ends inside a word, on a dangling `by`,
 * or inside an open bracket. A description too short to say anything, or a cut left empty, takes
 * the generic label.
 */
function shortSkillDescription(name: string, description: string): string {
  const generic = `Use ${codexDisplayName(name, description)} for relevant Codex tasks.`;
  const normalized = plainText(description).replace(/\s+/g, " ").trim();
  const candidate = normalized.length >= 25 ? normalized : generic;
  if (candidate.length <= SHORT_DESCRIPTION_LIMIT) {
    return candidate;
  }
  const ends = [...candidate.matchAll(SENTENCE_END)]
    .map((m) => m.index + 1)
    .filter((end) => end <= SHORT_DESCRIPTION_LIMIT);
  const sentenceEnd = ends.at(-1);
  if (sentenceEnd !== undefined) {
    return candidate.slice(0, sentenceEnd);
  }
  const cut = clauseCut(candidate) ?? wordCut(candidate);
  return cut ? `${cut}…` : generic;
}

export function createCodexSkillAgentManifest(
  plugin: string,
  skill: string,
  description: string,
  invocation: SkillIntent["invocation"],
): CodexSkillAgentManifest | undefined {
  if (!resolveCodexInvocation(invocation).agentManifest) {
    return undefined;
  }
  return {
    interface: {
      display_name: codexDisplayName(skill, description),
      short_description: shortSkillDescription(skill, description),
      default_prompt: `Use $${plugin}:${skill} for this task.`,
    },
    policy: { allow_implicit_invocation: false },
  };
}

/** Quote every string as required by the Codex skill agent metadata contract. */
export function serializeCodexSkillAgentManifest(manifest: CodexSkillAgentManifest): string {
  return [
    "interface:",
    `  display_name: ${JSON.stringify(manifest.interface.display_name)}`,
    `  short_description: ${JSON.stringify(manifest.interface.short_description)}`,
    `  default_prompt: ${JSON.stringify(manifest.interface.default_prompt)}`,
    "policy:",
    "  allow_implicit_invocation: false",
    "",
  ].join("\n");
}

function repositoryUrl(repository: PackageJson["repository"]): string | undefined {
  const raw = typeof repository === "string" ? repository : repository?.url;
  return raw?.replace(/^git\+/, "").replace(/\.git$/, "");
}

/** Load publisher facts from repository-owned package metadata, never from a target manifest. */
export function loadCodexPublisherMetadata(root: string): CodexPublisherMetadata {
  const pkg = JSON.parse(readFileSync(resolve(root, "package.json"), "utf8")) as PackageJson;
  const author = typeof pkg.author === "string" ? { name: pkg.author } : pkg.author;
  const repository = repositoryUrl(pkg.repository);
  if (!author?.name?.trim()) {
    throw new Error("package.json: author.name is required for Codex plugin manifests");
  }
  return {
    author: {
      name: author.name,
      ...(author.email ? { email: author.email } : {}),
      ...(author.url ? { url: author.url } : {}),
    },
    ...(pkg.homepage ? { homepage: pkg.homepage } : {}),
    ...(repository ? { repository } : {}),
    ...(pkg.license ? { license: pkg.license } : {}),
  };
}

/**
 * Human-facing label only; stable machine identity remains the curation plugin name. Each name part
 * takes the spelling the description writes it in when that spelling has a capital past its first
 * letter, as `CPM`, `AOT`, `TUnit`, or `MSBuild` do; `dotnet` reads `.NET`; any other part is
 * title-cased. A whole word has no letter, digit, or `.` before it and no letter, digit, or
 * `.`-then-letter-or-digit after it, so a dotted name such as `.NET`, `Akka.NET`, or `AGENTS.md`
 * lends no spelling, while a sentence's closing period still ends a word.
 */
export function codexDisplayName(name: string, description = ""): string {
  return name
    .split("-")
    .filter(Boolean)
    .map((part) => {
      if (part === "dotnet") {
        return ".NET";
      }
      const escaped = part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      const word = new RegExp(`(?<![\\p{L}\\p{N}.])${escaped}(?![\\p{L}\\p{N}]|\\.[\\p{L}\\p{N}])`, "giu");
      const written = [...description.matchAll(word)].map((m) => m[0]).find((spelling) => /^.+\p{Lu}/u.test(spelling));
      return written ?? `${part[0]?.toUpperCase() ?? ""}${part.slice(1)}`;
    })
    .join(" ");
}

export function createCodexPluginManifest(
  manifest: CurationManifest,
  publisher: CodexPublisherMetadata,
): CodexPluginManifest {
  const displayName = codexDisplayName(manifest.plugin.name, manifest.plugin.description);
  const websiteURL = publisher.homepage ?? publisher.repository;
  return {
    name: manifest.plugin.name,
    version: manifest.plugin.version,
    description: manifest.plugin.description,
    author: publisher.author,
    ...(publisher.homepage ? { homepage: publisher.homepage } : {}),
    ...(publisher.repository ? { repository: publisher.repository } : {}),
    ...(publisher.license ? { license: publisher.license } : {}),
    keywords: ["codex", "skills"],
    skills: "./skills/",
    interface: {
      displayName,
      shortDescription: manifest.plugin.description,
      longDescription: manifest.plugin.description,
      developerName: publisher.author.name,
      category: CODEX_PLUGIN_CATEGORY,
      capabilities: [...CODEX_PLUGIN_CAPABILITIES],
      ...(websiteURL ? { websiteURL } : {}),
      defaultPrompt: [`Use ${displayName} skills for this task.`],
    },
  };
}

export function codexMarketplaceSourcePath(pluginName: string): string {
  return `./codex/${pluginName}`;
}

export function createCodexMarketplace(manifests: CurationManifest[]): CodexMarketplace {
  return {
    name: CODEX_MARKETPLACE_NAME,
    interface: { displayName: CODEX_MARKETPLACE_DISPLAY_NAME },
    plugins: [...manifests]
      .sort((left, right) => left.plugin.name.localeCompare(right.plugin.name))
      .map((manifest) => ({
        name: manifest.plugin.name,
        source: { source: "local", path: codexMarketplaceSourcePath(manifest.plugin.name) },
        policy: { installation: "AVAILABLE", authentication: "ON_INSTALL" },
        category: CODEX_PLUGIN_CATEGORY,
      })),
  };
}

/** Resolve a marketplace path only if it is an explicit, portable child of the repository root. */
export function resolveCodexRepositoryPath(root: string, value: string): string {
  if (!value.startsWith("./") || value.includes("\\") || value.includes("\0") || isAbsolute(value.slice(2))) {
    throw new Error(`Codex marketplace path must be a portable repository-relative path beginning with ./: ${value}`);
  }
  const repositoryRoot = resolve(root);
  const destination = resolve(repositoryRoot, value.slice(2));
  if (destination === repositoryRoot || !destination.startsWith(`${repositoryRoot}${sep}`)) {
    throw new Error(`Codex marketplace path escapes the repository root: ${value}`);
  }
  return destination;
}

const WINDOWS_RESERVED_NAME = /^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i;

function nameProblems(kind: "plugin" | "skill", name: string, label: string): string[] {
  const problems: string[] = [];
  if (!PORTABLE_NAME.test(name)) {
    problems.push(`${label}: Codex ${kind} name must use lowercase letters, digits, and single hyphens: ${name}`);
  }
  const hasControlCharacter = [...name].some((character) => (character.codePointAt(0) ?? 0) < 32);
  if (/[<>:"/\\|?*]/.test(name) || hasControlCharacter || /[ .]$/.test(name) || WINDOWS_RESERVED_NAME.test(name)) {
    problems.push(`${label}: Codex ${kind} name is not portable on Windows: ${name}`);
  }
  if (kind === "skill" && name.length > MAX_CODEX_SKILL_NAME_LENGTH) {
    problems.push(`${label}: Codex skill name exceeds ${MAX_CODEX_SKILL_NAME_LENGTH} characters: ${name}`);
  }
  return problems;
}

/**
 * Validate the target's flattened namespace before any generated output is removed. This catches
 * collisions across source kinds (skill/command/agent), which the other target namespaces permit.
 */
export function collectCodexEmissionProblems(
  root: string,
  manifests: CurationManifest[],
  assembled: AssembledItem[],
): string[] {
  const problems: string[] = [];
  const pluginNames = new Map<string, string>();
  for (const manifest of manifests) {
    const plugin = manifest.plugin.name;
    problems.push(...nameProblems("plugin", plugin, plugin));
    const folded = plugin.toLowerCase();
    const prior = pluginNames.get(folded);
    if (prior) {
      problems.push(`Codex plugin namespace collision: ${prior} and ${plugin}`);
    } else {
      pluginNames.set(folded, plugin);
    }
    try {
      resolveCodexRepositoryPath(root, codexMarketplaceSourcePath(plugin));
    } catch (error) {
      problems.push(error instanceof Error ? error.message : String(error));
    }
  }

  for (const manifest of manifests) {
    const items = assembled
      .filter((item) => item.plugin === manifest.plugin.name && emitsTo(item.intent, "codex"))
      .sort((left, right) => left.outName.localeCompare(right.outName) || left.source.localeCompare(right.source));
    const names = new Map<string, AssembledItem>();
    for (const item of items) {
      const label = `${item.plugin}/${item.outName}`;
      problems.push(...nameProblems("skill", item.outName, label));
      const prior = names.get(item.outName.toLowerCase());
      if (prior) {
        problems.push(
          `${item.plugin}: flattened Codex skill collision ${prior.outName} (${prior.outType} from ${prior.source}) and ${item.outName} (${item.outType} from ${item.source})`,
        );
      } else {
        names.set(item.outName.toLowerCase(), item);
      }
      const doc = parseDoc(readFileSync(resolve(item.dir, "SKILL.md"), "utf8"));
      if (typeof doc.frontmatter.description !== "string" || !doc.frontmatter.description.trim()) {
        problems.push(`${label}: Codex skill requires a non-empty description`);
      }
    }
  }
  return problems;
}
