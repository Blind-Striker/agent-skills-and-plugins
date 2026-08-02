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
