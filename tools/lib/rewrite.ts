import { basename } from "node:path";
import type { ComponentType, CurationManifest } from "./manifest.ts";
import { openCodeId } from "./opencode-target.ts";
import type { OwnSkillIdentity } from "./own-skills.ts";
import { type SkillToolCall, scanPathClaims, scanRefs, scanSkillToolCalls } from "./refs.ts";
import type { ComponentInfo } from "./scan.ts";

// How a harness ADDRESSES the component upstream, which is what its references spell out: a skill by
// its directory name, a command or agent by its file name. The frontmatter `name` is not the address
// and diverges from it in 32 of the 223 upstream components, so keying on it missed the real refs.
// `<name>.agent.md` is a double extension, not part of the address — references spell the bare name.
export function addressOf(c: ComponentInfo): string {
  return c.type === "skill" ? basename(c.sourcePath) : basename(c.sourcePath, ".md").replace(/\.agent$/, "");
}

/**
 * How the target harness spells a reference to one of our own components. One map serves every
 * output tree: its value is the resolved target, and each style below renders that target.
 *
 * Claude Code addresses a plugin skill as `<plugin>:<name>`; Codex as `$<plugin>:<name>`; OpenCode 2
 * as the dotted ID `<plugin>.<name>`, which contains no `:` and so cannot be re-detected by the scan.
 */
export type RefStyle = "claude" | "opencode" | "codex";

/** The resolved output a reference points at: owning Plugin, output name, and output kind. */
export interface RewriteTarget {
  plugin: string;
  name: string;
  kind: ComponentType;
}

export function buildRewriteMap(
  manifests: CurationManifest[],
  components: ComponentInfo[],
  ownSkills: OwnSkillIdentity[] = [],
): Map<string, RewriteTarget> {
  const bySource = new Map(components.map((c) => [c.sourcePath, c]));
  const map = new Map<string, RewriteTarget>();
  for (const m of manifests) {
    for (const item of m.items) {
      if (item.exclude) {
        continue;
      }
      const c = bySource.get(item.source);
      if (!c) {
        continue;
      }
      // The name is our own output name, which build.ts forces onto the emitted dir/file name.
      map.set(`${c.namespace}:${addressOf(c)}`, {
        plugin: m.plugin.name,
        name: item.name ?? c.name,
        kind: item.as ?? c.type,
      });
    }
  }
  for (const own of ownSkills) {
    const value: RewriteTarget = { plugin: own.plugin, name: own.name, kind: "skill" };
    const existing = map.get(own.address);
    if (existing && (existing.plugin !== value.plugin || existing.name !== value.name)) {
      throw new Error(
        `reference identity ${own.address} resolves to both ${existing.plugin}:${existing.name} and ${own.address}`,
      );
    }
    map.set(own.address, value);
  }
  return map;
}

function render(target: RewriteTarget, style: RefStyle): string {
  switch (style) {
    case "claude":
      return `${target.plugin}:${target.name}`;
    case "codex":
      return `$${target.plugin}:${target.name}`;
    case "opencode":
      return openCodeId(target.plugin, target.name);
  }
}

/** OpenCode 2's user pointer: `/<id>` runs a command; `@<id>` mentions a skill or an agent. */
function renderPointer(target: RewriteTarget, style: RefStyle): string {
  if (style === "opencode") {
    return `${target.kind === "command" ? "/" : "@"}${render(target, style)}`;
  }
  return render(target, style);
}

/**
 * One pass over the shared scan (ADR-0008), so the rewrite touches exactly what every other reader
 * calls a reference. The lookup is exact because the scan already returns the maximal address: a
 * curated `sp:foo` cannot eat the prefix of an uncurated `sp:foo-bar`, which is what the old
 * longest-key-first ordering existed to prevent. Everything outside a replaced address is copied
 * byte-for-byte — a pointer's leading slash included, since it sits outside `address`.
 */
export function rewriteRefs(content: string, map: Map<string, RewriteTarget>, style: RefStyle = "claude"): string {
  let out = "";
  let cut = 0;
  for (const ref of scanRefs(content)) {
    const target = map.get(ref.address);
    if (target === undefined) {
      continue;
    }
    // The scanner positions a pointer after its leading slash. Codex renders both semantic edge
    // kinds with `$`, so consume that slash instead of producing the invalid `/$plugin:skill`;
    // OpenCode consumes it too and renders its own prefix, which depends on the target kind.
    const pointer = ref.kind === "pointer";
    const start = pointer && style !== "claude" ? ref.index - 1 : ref.index;
    const rendered = pointer && style !== "claude" ? renderPointer(target, style) : render(target, style);
    out += content.slice(cut, start) + rendered;
    cut = ref.index + ref.address.length;
  }
  return out + content.slice(cut);
}

const CODEX_VERB = { Call: "Invoke", call: "invoke", calls: "invokes", calling: "invoking" } as const;

/** A line that opens a new Markdown block: a list item, quote, heading, or table row. */
const BLOCK_START = /^[ \t]*(?:[-*+][ \t]|>|#|\||\d+[.)][ \t])/;

/**
 * The code of Markdown text, as `[start, end)` offsets. Inline spans include their backticks: as in
 * CommonMark, a run of backticks opens a span that the next run of the same length closes within
 * its block, and a run with no closer is literal. Fenced blocks include their fence lines, and a
 * fence that never closes runs to the end of the text; a fenced block holds no span.
 */
function markdownCode(content: string): { inline: [number, number][]; fenced: [number, number][] } {
  const inline: [number, number][] = [];
  const fenced: [number, number][] = [];
  const scan = (start: number, end: number): void => {
    const runs = [...content.slice(start, end).matchAll(/`+/g)].map((m) => ({
      at: start + m.index,
      length: m[0].length,
    }));
    for (let i = 0; i < runs.length; i += 1) {
      const open = runs[i] as { at: number; length: number };
      const close = runs.findIndex((run, k) => k > i && run.length === open.length);
      if (close !== -1) {
        inline.push([open.at, (runs[close]?.at ?? 0) + open.length]);
        i = close;
      }
    }
  };
  let offset = 0;
  let block: number | undefined;
  let fence: { char: string; length: number; start: number } | undefined;
  const endBlock = (end: number): void => {
    if (block !== undefined) {
      scan(block, end);
    }
    block = undefined;
  };
  for (const line of content.split("\n")) {
    const marker = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(line);
    const run = marker?.[1] ?? "";
    const rest = marker?.[2] ?? "";
    if (fence) {
      if (run.startsWith(fence.char) && run.length >= fence.length && rest.trim() === "") {
        fenced.push([fence.start, offset + line.length]);
        fence = undefined;
      }
    } else if (marker && !(run.startsWith("`") && rest.includes("`"))) {
      endBlock(offset);
      fence = { char: run.charAt(0), length: run.length, start: offset };
    } else if (line.trim() === "" || BLOCK_START.test(line)) {
      endBlock(offset);
      block = line.trim() === "" ? undefined : offset;
    } else {
      block ??= offset;
    }
    offset += line.length + 1;
  }
  endBlock(content.length);
  if (fence) {
    fenced.push([fence.start, content.length]);
  }
  return { inline, fenced };
}

/** The inline code spans of Markdown text, as `[start, end)` offsets that include their backticks. */
export function inlineCodeSpans(content: string): [number, number][] {
  return markdownCode(content).inline;
}

// Closed dispatch table (references-and-linking.md "Harness phrasing"). Codex names no subagent tool
// or agent type: neither is recorded in this repository's research. A row marked `outsideCode` would
// put a backtick inside a code span and break it, so it skips a match there in every harness and
// leaves the words for the vocabulary check to reject.
const DISPATCH: { pattern: RegExp; opencode: string; codex: string; outsideCode?: true }[] = [
  { pattern: /Subagent \(general-purpose\)/g, opencode: "Subagent (general)", codex: "Subagent" },
  { pattern: /`general-purpose`(\s+)subagent/g, opencode: "`general`$1subagent", codex: "subagent" },
  // The same words unbackticked, or with the type emphasized as `**general-purpose**` or
  // `_general-purpose_`. An underscore is a word character, so the emphasis is matched explicitly.
  {
    pattern: /(?:(\*{1,2}|_{1,2})|(?<!\w))general-purpose\1(\s+)subagent\b/g,
    opencode: "$1`general`$1$2subagent",
    codex: "subagent",
    outsideCode: true,
  },
  { pattern: /`Agent`( calls?)\b/g, opencode: "`subagent`$1", codex: "subagent$1" },
];

type Replacement = string | ((match: string, ...groups: string[]) => string);

// Closed OpenCode todo table (references-and-linking.md "Harness phrasing"), matched exactly.
// OpenCode 2 has no todo tool, so each upstream phrase that creates, marks, or names a todo renders
// as Markdown-checklist wording: the plan executors keep the checklist in the ledger they already
// keep, and every other skill keeps a visible checklist ticked line by line. Claude Code and Codex
// keep upstream's words. A match inside an inline code span is left whole, as the check skips it.
const OPENCODE_TODO: { pattern: RegExp; opencode: Replacement }[] = [
  // executing-plans and subagent-driven-development: the ledger holds the checklist. Setup runs again
  // on every resume and the ledger survives it, so a resumed run keeps the checklist it has. A
  // Markdown checkbox is ticked or not, so a task in progress is one whose line is not yet ticked.
  {
    pattern: /\bcreate a(\s+)todo per task\b/g,
    opencode: "add a$1Markdown checklist with one line per task to the ledger, unless the ledger already has one",
  },
  { pattern: /\[Create todos for all tasks\]/g, opencode: "[Add a checklist of all tasks to the ledger]" },
  {
    pattern: /\bMark the task's todo in_progress\b/g,
    opencode: "Leave the task's checklist line unticked until task-done records it",
  },
  { pattern: /\bmark (?:the )?todo complete\b/g, opencode: "tick the task's checklist line" },
  { pattern: /\bledger file, not only in todos\./g, opencode: "ledger file." },
  {
    pattern: /\bHarness todos are a(\s+)live view; the ledger is the record\./g,
    opencode: "The ledger is both the$1live view and the record.",
  },
  // diagnosing-superpowers, using-superpowers, writing-skills: a visible checklist.
  {
    pattern: /\b([Cc])reate a todo per (step|item)\./g,
    opencode: (_match, c, unit) =>
      `${c === "C" ? "K" : "k"}eep a visible Markdown checklist with one line per ${unit}, and tick each line as it is done.`,
  },
  {
    pattern: /\bCreate a todo for EACH checklist item below\./g,
    opencode: "Copy EACH checklist item below into a visible Markdown checklist, and tick each line as it is done.",
  },
  { pattern: /\btodos for checklists\b/g, opencode: "a visible Markdown checklist, ticked line by line" },
  { pattern: /\bwithout todo tracking\b/g, opencode: "without line-by-line ticking" },
  { pattern: /\bfind a todo list helpful\b/g, opencode: "find ticking lines off helpful" },
  // diagnosing-superpowers' plan-adherence prompt: what an agreed plan looks like in a session.
  { pattern: /\ba todo-list record\b/g, opencode: "a Markdown checklist" },
];

/** `todo`, `todos`, `to-do`, or `to-dos` in any letter case, not inside a longer word. */
const TODO_WORD = /(?<![A-Za-z0-9])to-?dos?(?![A-Za-z0-9])/gi;

/**
 * The one OpenCode file that may say todo, below `opencode/`: using-superpowers' OpenCode tool
 * notes, which must name the todo tool OpenCode 2 removed.
 */
export const OPENCODE_TODO_NOTES = "deniz-process/skills/deniz-process.using-superpowers/references/harness-tools.md";

/**
 * Every todo word outside inline code and fenced code. OpenCode 2 has no todo tool, so prose that
 * names a todo, a `TODO` included, asks for something the harness lacks; a marker written as code
 * is code.
 */
export function openCodeTodoWords(content: string): { index: number; match: string }[] {
  const { inline, fenced } = markdownCode(content);
  const code = [...inline, ...fenced];
  return [...content.matchAll(TODO_WORD)]
    .filter((m) => !code.some(([start, end]) => m.index >= start && m.index < end))
    .map((m) => ({ index: m.index, match: m[0] }));
}

/** Replaces each match of a row that starts outside every inline code span. */
function replaceOutsideCode(text: string, pattern: RegExp, replacement: Replacement): string {
  const spans = inlineCodeSpans(text);
  const single = new RegExp(pattern.source, pattern.flags.replace("g", ""));
  return text.replace(pattern, (match: string, ...args: unknown[]) => {
    const at = args.find((arg) => typeof arg === "number") as number;
    if (spans.some(([start, end]) => at >= start && at < end)) {
      return match;
    }
    return typeof replacement === "string" ? match.replace(single, replacement) : match.replace(single, replacement);
  });
}

/** Claude Code tool words that must not survive in an OpenCode or Codex tree. */
export const CLAUDE_ONLY_VOCABULARY: readonly RegExp[] = [
  /\bskill tool/gi,
  /Subagent \(general-purpose\)/g,
  /`general-purpose`/g,
  /(?<![A-Za-z0-9])general-purpose[*_]*\s+(?:sub)?agent\b/g,
  /`Agent`/g,
  /\bAgent tool\b/g,
  /\bTask tool\b/g,
  /\bsubagent_type\b/g,
  // Claude Code's todo tool. OpenCode 2 has none, and this repository's research names no Codex one.
  /\bTodoWrite\b/g,
];

export function claudeOnlyVocabulary(content: string): { index: number; match: string }[] {
  return CLAUDE_ONLY_VOCABULARY.flatMap((re) =>
    [...content.matchAll(re)].map((m) => ({ index: m.index, match: m[0] })),
  ).sort((a, b) => a.index - b.index);
}

function renderCall(content: string, call: SkillToolCall, style: "opencode" | "codex"): string {
  const span = content.slice(call.index, call.end);
  if (style === "opencode") {
    return span.replace("the Skill tool", "the `skill` tool");
  }
  const verb = CODEX_VERB[call.verb];
  return call.payloads.length ? `${verb} ${call.payloads.map((p) => `\`${p.text}\``).join(" and ")}` : verb;
}

/** Renders the skill-tool call and dispatch words for one harness; facts stay neutral. */
export function renderHarnessPhrasing(content: string, style: RefStyle): string {
  if (style === "claude") {
    return content;
  }
  let out = "";
  let cut = 0;
  for (const call of scanSkillToolCalls(content)) {
    out += content.slice(cut, call.index) + renderCall(content, call, style);
    cut = call.end;
  }
  out += content.slice(cut);
  for (const row of DISPATCH) {
    out = row.outsideCode ? replaceOutsideCode(out, row.pattern, row[style]) : out.replace(row.pattern, row[style]);
  }
  if (style === "opencode") {
    for (const row of OPENCODE_TODO) {
      out = replaceOutsideCode(out, row.pattern, row.opencode);
    }
  }
  return out;
}

/** Harness phrasing first, then the facts it left neutral. */
export function localize(content: string, map: Map<string, RewriteTarget>, style: RefStyle): string {
  return rewriteRefs(renderHarnessPhrasing(content, style), map, style);
}

/**
 * OpenCode 2 installs every skill folder under its ID, so a path that names a sibling by its bare
 * folder name would land on nothing. A path is respelled only when `scanPathClaims` returns it — a
 * climb whose `../` count lands exactly on the shared `skills/` directory, or an item-root
 * `skills/<name>/` path, neither starting inside a longer path other than a script-directory
 * anchor such as `$(dirname "$0")/` — and its segment is an emitted
 * skill's bare name; anything else is left for the linker to judge. A path into the item's own
 * folder is respelled too: that folder is renamed. The text may be any bundled file, not only
 * Markdown.
 */
export function rewriteOpenCodePaths(
  content: string,
  depthBelowSkillFolder: number,
  skillIds: Map<string, string>,
): string {
  let out = "";
  let cut = 0;
  for (const claim of scanPathClaims(content, depthBelowSkillFolder)) {
    const id = skillIds.get(claim.segment);
    if (id === undefined) {
      continue;
    }
    out += content.slice(cut, claim.segmentIndex) + id;
    cut = claim.segmentIndex + claim.segment.length;
  }
  return out + content.slice(cut);
}

/** A file the path respelling may read: no NUL byte, and bytes that survive a UTF-8 round trip. */
export function isBundledText(bytes: Buffer): boolean {
  return !bytes.includes(0) && Buffer.from(bytes.toString("utf8"), "utf8").equals(bytes);
}
