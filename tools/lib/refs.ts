/**
 * The one reference scanner (ADR-0008). Facts are namespaced spellings; the leading slash is the
 * kind: `ns:name` is a model-edge (the model invokes the target), `/ns:name` is a user-pointer
 * (the human is told what to open). Every reader goes through this scan — the rewrite replaces
 * what it returns, the linker resolves it, the ledger records it — so the grammar is decided here
 * and nowhere else.
 */
export type RefKind = "model" | "pointer";

export interface Ref {
  kind: RefKind;
  ns: string;
  name: string;
  /** `ns:name` as written, without the pointer slash. */
  address: string;
}

/** A reference with its position: `index` is where `address` starts, the pointer slash excluded. */
export interface ScannedRef extends Ref {
  index: number;
}

const REF = /([a-z][a-z0-9-]*):([a-z][a-z0-9-]*)/g;
/** What continues a ref token, so a hit hugged by one of these is a slice of something longer. */
const TOKEN = /[a-z0-9-]/;

/**
 * Every reference in `content`, in the order written and positioned — the form a rewrite needs to
 * replace one in place. Two shapes are rejected: a hit inside a longer token (`2fa:setup`, and the
 * tail of `x:a:b`), and a chain `a:b:c`, which addresses nothing. A colon followed by anything else
 * is prose, and the reference before it stands: "use superpowers:tdd: it gates the loop" names a
 * real target.
 */
export function scanRefs(content: string): ScannedRef[] {
  const out: ScannedRef[] = [];
  for (const m of content.matchAll(REF)) {
    const before = m.index > 0 ? (content[m.index - 1] as string) : "";
    const end = m.index + m[0].length;
    // The after-side needs only the colon check: REF already consumed every trailing [a-z0-9-].
    if (
      TOKEN.test(before) ||
      before === ":" ||
      /^[A-Z]$/.test(before) ||
      before === "_" ||
      (content[end] === ":" && TOKEN.test(content[end + 1] ?? ""))
    ) {
      continue;
    }
    out.push({
      kind: before === "/" ? "pointer" : "model",
      ns: m[1] as string,
      name: m[2] as string,
      address: m[0],
      index: m.index,
    });
  }
  return out;
}

/** The same scan with the position dropped: what a reader that only resolves references wants. */
export function extractRefs(content: string): Ref[] {
  return scanRefs(content).map(({ kind, ns, name, address }) => ({ kind, ns, name, address }));
}

function escapeRegExp(s: string): string {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Candidate tier: known names appearing as standalone words. Heuristic by design — upstream names
 * are ordinary words — so hits are surfaced for human reading and never become build state.
 * The colon in the boundary class keeps a fact spelling from double-counting as its own candidate.
 */
export function candidateHits(content: string, names: Iterable<string>): string[] {
  const hits: string[] = [];
  for (const n of names) {
    const re = new RegExp(`(^|[^a-z0-9-:])${escapeRegExp(n)}($|[^a-z0-9-:])`, "m");
    if (re.test(content)) {
      hits.push(n);
    }
  }
  return hits.sort();
}

/** A relative path the linker can attribute: a climb onto the shared skills directory, or (W0.7) an item root. */
export interface PathClaim {
  kind: "climb" | "item-root";
  /** Where the claim starts: the first `../` of a climb. */
  index: number;
  /** Where `segment` starts, so a rewrite can respell it in place. */
  segmentIndex: number;
  segment: string;
  path: string;
}

// A portable name, or the `<plugin>.<name>` ID an OpenCode segment carries after respelling, so the
// validator reads every tree with one grammar. The rewrite looks up bare names only.
const SEGMENT = String.raw`[a-z0-9]+(?:-[a-z0-9]+)*(?:\.[a-z0-9]+(?:-[a-z0-9]+)*)?`;
/** `../` runs followed by one segment and a `/`: a relative climb into a sibling folder. */
const CLIMB = new RegExp(String.raw`((?:\.\.\/)+)(${SEGMENT})(?=\/)`, "g");
const PATH_TAIL = /^\/[^\s"'`)\]]*/;
/** What continues a path, so a climb preceded by one of these starts inside a longer path. */
const CONTINUES_PATH = /[A-Za-z0-9._/-]/;

function claimPath(content: string, start: number, afterSegment: number): string {
  const path = content.slice(start, afterSegment) + (PATH_TAIL.exec(content.slice(afterSegment))?.[0] ?? "");
  return path.endsWith(".") ? path.slice(0, -1) : path;
}

/**
 * Every relative path in `content` the linker can attribute, in the order written. A climb is a
 * claim only when its `../` count lands exactly on the shared `skills/` directory
 * (`depthBelowSkillFolder + 1`) and it does not start inside a longer path; the path runs to the
 * first space, quote, backtick, or closing bracket, minus one sentence-ending dot.
 */
export function scanPathClaims(content: string, depthBelowSkillFolder: number): PathClaim[] {
  const out: PathClaim[] = [];
  for (const m of content.matchAll(CLIMB)) {
    const before = m.index > 0 ? (content[m.index - 1] as string) : "";
    const climb = m[1] as string;
    const segment = m[2] as string;
    if ((before && CONTINUES_PATH.test(before)) || climb.length / 3 !== depthBelowSkillFolder + 1) {
      continue;
    }
    const segmentIndex = m.index + climb.length;
    out.push({
      kind: "climb",
      index: m.index,
      segmentIndex,
      segment,
      path: claimPath(content, m.index, segmentIndex + segment.length),
    });
  }
  return out;
}

export interface SkillToolCall {
  /** Start of the verb. */
  index: number;
  /** One past the span. */
  end: number;
  verb: "Call" | "call" | "calls" | "calling";
  form: "with" | "twice" | "generic";
  /** Quoted handles, quotes excluded; `index` is the payload's first character. */
  payloads: { index: number; text: string }[];
}

// The skill-tool call template (references-and-linking.md "Handoff templates"). Closed on purpose:
// a sentence outside these three forms is a stray mention, which validate rejects.
const SKILL_TOOL_CALL =
  /\b(Call|call|calls|calling) the Skill tool(?: with "([^"\n]*)"| twice, for "([^"\n]*)" and "([^"\n]*)"| for(?![A-Za-z]| "))/g;
const SKILL_TOOL_MENTION = /\bskill tool/gi;

/** Every skill-tool call span in `content`, in the order written. */
export function scanSkillToolCalls(content: string): SkillToolCall[] {
  const out: SkillToolCall[] = [];
  for (const m of content.matchAll(SKILL_TOOL_CALL)) {
    const texts = m[2] !== undefined ? [m[2]] : m[3] !== undefined ? [m[3], m[4] as string] : [];
    let from = m.index;
    const payloads = texts.map((text) => {
      const index = content.indexOf(`"${text}"`, from) + 1;
      from = index + text.length + 1;
      return { index, text };
    });
    out.push({
      index: m.index,
      end: m.index + m[0].length,
      verb: m[1] as SkillToolCall["verb"],
      form: m[2] !== undefined ? "with" : m[3] !== undefined ? "twice" : "generic",
      payloads,
    });
  }
  return out;
}

/** Indexes of every case-insensitive `skill tool` that no call span covers. */
export function straySkillToolMentions(content: string): number[] {
  const spans = scanSkillToolCalls(content);
  return [...content.matchAll(SKILL_TOOL_MENTION)]
    .map((m) => m.index)
    .filter((index) => !spans.some((s) => index >= s.index && index < s.end));
}

/** A bare backticked name in a load-bearing handoff template (references-and-linking.md "Handoff templates"). */
export interface Handoff {
  template: "imperative" | "load" | "route";
  name: string;
  /** The name's opening backtick. */
  index: number;
}

// A backticked portable name: a fact carries a colon, so it never matches.
const NAME = String.raw`\x60([a-z][a-z0-9]*(?:-[a-z0-9]+)*)\x60`;
// A sentence stops at . ; : ! ? |, a blank line, or a newline that opens a list, table, heading, or quote.
const SENTENCE = String.raw`(?:[^.;:!?|\n]|\n(?![ \t]*(?:\n|[-*+>#|]|\d+\.)))*?`;
const IMPERATIVE = new RegExp(
  String.raw`\b(?:Load|load|Use|use|Follow|follow|Invoke|invoke|Call|call)\b${SENTENCE}${NAME}\s+skill\b`,
  "g",
);
const LOAD = new RegExp(String.raw`\b(?:Load|load|Invoke|invoke)\s+${NAME}`, "g");
const LIST_TAIL = new RegExp(String.raw`^(?:,\s*|\s+and\s+|\s+or\s+)${NAME}`);
const ROUTE = new RegExp(String.raw`→\s*\*{0,2}${NAME}`, "g");

/** Bare backticked names in the load-bearing handoff templates, in the order written, one hit per name. */
export function scanHandoffs(content: string): Handoff[] {
  const out: Handoff[] = [];
  const at = (start: number, matched: string, name: string): number => start + matched.lastIndexOf(`\`${name}\``);
  for (const m of content.matchAll(IMPERATIVE)) {
    out.push({ template: "imperative", name: m[1] as string, index: at(m.index, m[0], m[1] as string) });
  }
  for (const m of content.matchAll(LOAD)) {
    out.push({ template: "load", name: m[1] as string, index: at(m.index, m[0], m[1] as string) });
    let cursor = m.index + m[0].length;
    for (let t = LIST_TAIL.exec(content.slice(cursor)); t; t = LIST_TAIL.exec(content.slice(cursor))) {
      out.push({ template: "load", name: t[1] as string, index: at(cursor, t[0], t[1] as string) });
      cursor += t[0].length;
    }
  }
  for (const m of content.matchAll(ROUTE)) {
    out.push({ template: "route", name: m[1] as string, index: at(m.index, m[0], m[1] as string) });
  }
  // Two templates can claim one name ("Load `x` skill"): keep the first pushed, which sorts stably first.
  return out
    .sort((a, b) => a.index - b.index)
    .filter((hit, i, all) => all.findIndex((other) => other.index === hit.index) === i);
}
