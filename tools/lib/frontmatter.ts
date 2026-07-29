import { parse as parseYaml, stringify as stringifyYaml } from "yaml";

export interface ParsedDoc {
  frontmatter: Record<string, unknown>;
  body: string;
}

export function parseDoc(content: string): ParsedDoc {
  const m = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(content);
  if (!m) {
    return { frontmatter: {}, body: content };
  }
  const frontmatter = (parseYaml(m[1]) as Record<string, unknown> | null) ?? {};
  return { frontmatter, body: content.slice(m[0].length).replace(/^\r?\n/, "") };
}

export function serializeDoc(doc: ParsedDoc): string {
  return `---\n${stringifyYaml(doc.frontmatter).trimEnd()}\n---\n\n${doc.body}`;
}
