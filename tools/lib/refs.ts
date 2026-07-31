/**
 * The one reference scanner (ADR-0008). Facts are namespaced spellings; the leading slash is the
 * kind: `ns:name` is a model-edge (the model invokes the target), `/ns:name` is a user-pointer
 * (the human is told what to open). Boundaries mirror rewriteRefs, so everything the rewrite
 * would touch is exactly what this extracts.
 */
export type RefKind = "model" | "pointer";

export interface Ref {
  kind: RefKind;
  ns: string;
  name: string;
  /** `ns:name` as written, without the pointer slash. */
  address: string;
}

const REF = /([a-z][a-z0-9-]*):([a-z][a-z0-9-]*)/g;

export function extractRefs(content: string): Ref[] {
  const out: Ref[] = [];
  for (const m of content.matchAll(REF)) {
    const before = m.index > 0 ? (content[m.index - 1] as string) : "";
    const after = content[m.index + m[0].length] ?? "";
    // Inside a longer token, or part of an a:b:c chain — not a reference. The after-side needs
    // only the colon check: REF already consumed every trailing [a-z0-9-].
    if (/[a-z0-9-]/.test(before) || before === ":" || after === ":") {
      continue;
    }
    out.push({
      kind: before === "/" ? "pointer" : "model",
      ns: m[1] as string,
      name: m[2] as string,
      address: m[0],
    });
  }
  return out;
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
