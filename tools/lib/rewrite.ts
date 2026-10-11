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
/** One blockquote marker: up to three spaces, `>`, and the one space that may follow it. */
const QUOTE_MARKER = /^ {0,3}> ?/;
/** A list item marker, followed by a space, a tab, or the end of the line. */
const LIST_MARKER = /^(?:[-*+]|\d{1,9}[.)])(?=[ \t]|$)/;
/** A code fence line: three or more backticks or tildes, then the rest of the line. */
const FENCE = /^(`{3,}|~{3,})(.*)$/;
/** An HTML code element that closes before the next blank line. */
const HTML_CODE = /<code(?:\s[^<>]*)?>(?:(?!\n[ \t]*\n)[\s\S])*?<\/code\s*>/gi;

/** Strips up to `limit` blockquote markers from the start of a line. */
function unquote(line: string, limit = Number.POSITIVE_INFINITY): { quotes: number; rest: string } {
  let quotes = 0;
  let rest = line;
  for (let marker = QUOTE_MARKER.exec(rest); marker && quotes < limit; marker = QUOTE_MARKER.exec(rest)) {
    rest = rest.slice(marker[0].length);
    quotes += 1;
  }
  return { quotes, rest };
}

/** The column a line's text starts at, a tab advancing to the next multiple of four, and the text. */
function indentOf(line: string): { column: number; text: string } {
  let column = 0;
  let at = 0;
  for (; at < line.length && (line[at] === " " || line[at] === "\t"); at += 1) {
    column += line[at] === "\t" ? 4 - (column % 4) : 1;
  }
  return { column, text: line.slice(at) };
}

/** The run of a fence opener; a backtick fence's info string holds no backtick. */
function fenceOpener(text: string): string | undefined {
  const fence = FENCE.exec(text);
  const run = fence?.[1];
  return run !== undefined && !(run.startsWith("`") && (fence?.[2] ?? "").includes("`")) ? run : undefined;
}

/**
 * The code of Markdown text, as `[start, end)` offsets, read the way CommonMark reads it. Inline
 * spans include their backticks: a run of backticks opens a span that the next run of the same
 * length closes within its block, and a run with no closer is literal. Code blocks are whole
 * lines: a fenced block with its fence lines, and an indented block, four columns past its
 * container and not continuing a paragraph. A fence or indented block may sit in a list item or a
 * blockquote; it ends when its container ends, and a fence that never closes runs to the end of
 * the text. A code block holds no span. HTML code is a `<code>` element closed before the next
 * blank line, outside the other code.
 */
function markdownCode(content: string): {
  inline: [number, number][];
  blocks: [number, number][];
  html: [number, number][];
} {
  const inline: [number, number][] = [];
  const blocks: [number, number][] = [];
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
  // The inline block being read, the open fence, and the open indented block.
  let block: number | undefined;
  let fence: { char: string; length: number; start: number; quotes: number; column: number } | undefined;
  let codeBlock: [number, number] | undefined;
  // The content columns of the open list items, innermost last, and the quote depth they sit at.
  let items: number[] = [];
  let itemQuotes = 0;
  // Whether the line before continues a paragraph: an indented line then continues it too.
  let paragraph = false;
  const endBlock = (end: number): void => {
    if (block !== undefined) {
      scan(block, end);
    }
    block = undefined;
  };
  const endCodeBlock = (): void => {
    if (codeBlock) {
      blocks.push(codeBlock);
    }
    codeBlock = undefined;
  };
  for (const line of content.split("\n")) {
    const end = offset + line.length;
    if (fence) {
      const inner = unquote(line, fence.quotes);
      const { column, text } = indentOf(inner.rest);
      if (inner.quotes === fence.quotes && (text.trim() === "" || column >= fence.column)) {
        const close = FENCE.exec(text);
        const run = close?.[1] ?? "";
        if (
          column - fence.column <= 3 &&
          run.startsWith(fence.char) &&
          run.length >= fence.length &&
          (close?.[2] ?? "").trim() === ""
        ) {
          blocks.push([fence.start, end]);
          fence = undefined;
        }
        offset = end + 1;
        continue;
      }
      // The blockquote or list item that held the fence ended on the line before, and the fence with it.
      blocks.push([fence.start, offset - 1]);
      fence = undefined;
    }
    const { quotes, rest } = unquote(line);
    if (quotes !== itemQuotes) {
      items = [];
      itemQuotes = quotes;
    }
    let { column, text } = indentOf(rest);
    if (text.trim() === "") {
      endBlock(offset);
      paragraph = false;
      offset = end + 1;
      continue;
    }
    // A less-indented line ends each list item it is not inside, unless it lazily continues a paragraph.
    const lazy = paragraph && fenceOpener(text) === undefined && !BLOCK_START.test(text);
    while (!lazy && items.length > 0 && column < (items.at(-1) ?? 0)) {
      items.pop();
    }
    let container = items.at(-1) ?? 0;
    const marker = !lazy && column - container <= 3 ? LIST_MARKER.exec(text)?.[0] : undefined;
    if (marker !== undefined) {
      // The item's content starts after the marker and its spaces; past four of them, one space
      // starts it and the rest indents code.
      const after = indentOf(text.slice(marker.length));
      const gap = after.text === "" || after.column > 4 ? 1 : after.column;
      container = column + marker.length + gap;
      items.push(container);
      column = container + after.column - gap;
      text = after.text;
      paragraph = false;
    }
    if (!paragraph && text !== "" && column - container >= 4) {
      endBlock(offset);
      codeBlock = codeBlock ? [codeBlock[0], end] : [offset, end];
      offset = end + 1;
      continue;
    }
    endCodeBlock();
    const run = column - container <= 3 ? fenceOpener(text) : undefined;
    if (run !== undefined) {
      endBlock(offset);
      fence = { char: run.charAt(0), length: run.length, start: offset, quotes, column: container };
      paragraph = false;
      offset = end + 1;
      continue;
    }
    if (BLOCK_START.test(line)) {
      endBlock(offset);
      block = offset;
    } else {
      block ??= offset;
    }
    paragraph = text !== "" && !/^#{1,6}(?:[ \t]|$)/.test(text);
    offset = end + 1;
  }
  endBlock(content.length);
  endCodeBlock();
  if (fence) {
    blocks.push([fence.start, content.length]);
  }
  const code = [...inline, ...blocks];
  const html = [...content.matchAll(HTML_CODE)]
    .filter((m) => !code.some(([start, end]) => m.index >= start && m.index < end))
    .map((m): [number, number] => [m.index, m.index + m[0].length]);
  return { inline, blocks, html };
}

/** The inline code spans of Markdown text, as `[start, end)` offsets that include their backticks. */
export function inlineCodeSpans(content: string): [number, number][] {
  return markdownCode(content).inline;
}

// Closed dispatch table (references-and-linking.md "Harness phrasing"). The Codex rendering names no
// subagent tool or agent type by choice, although Codex CLI ships `spawn_agent`: its companion tools
// differ between multi-agent versions, and using-superpowers' Codex tool notes name the tools. A row
// marked `outsideCode` would put a backtick inside a code span and break it, so it skips a match
// there in every harness and leaves the words for the vocabulary check to reject.
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

/**
 * `todo` or `todos`, joined or with any hyphen or dash between `to` and `do`, or OpenCode 1's
 * removed `todowrite` and `todoread` tools, in any letter case and not inside a longer word. The
 * two words "to do" are ordinary English and are not one.
 */
const TODO_WORD = /(?<![A-Za-z0-9])to\p{Pd}?do(?:s|write|read)?(?![A-Za-z0-9])/giu;

/** A URL with a scheme, which ends at whitespace, a bracket, a quote, or a backtick. */
const SCHEME_URL = /\b[A-Za-z][A-Za-z0-9+.-]*:\/\/[^\s<>()[\]"'`]+/g;

/**
 * The one OpenCode file that may say todo, below `opencode/`: using-superpowers' OpenCode tool
 * notes, which must name the todo tool OpenCode 2 removed.
 */
export const OPENCODE_TODO_NOTES = "deniz-process/skills/deniz-process.using-superpowers/references/harness-tools.md";

/**
 * Every todo word outside code and outside a URL. OpenCode 2 has no todo tool, so prose that names
 * a todo, a `TODO` included, asks for something the harness lacks; a marker written as code is code,
 * and a URL is an address.
 */
export function openCodeTodoWords(content: string): { index: number; match: string }[] {
  const { inline, blocks, html } = markdownCode(content);
  const urls = [...content.matchAll(SCHEME_URL)].map((m): [number, number] => [m.index, m.index + m[0].length]);
  const skipped = [...inline, ...blocks, ...html, ...urls];
  return [...content.matchAll(TODO_WORD)]
    .filter((m) => !skipped.some(([start, end]) => m.index >= start && m.index < end))
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
