import { parse as parseYaml, stringify as stringifyYaml } from "yaml";

export interface ParsedDoc {
  frontmatter: Record<string, unknown>;
  body: string;
}

/**
 * Splits a Markdown document into frontmatter and body. Upstream frontmatter can leave a top-level
 * plain value holding ": " unquoted, which strict YAML reads as a nested mapping; unless `strict`
 * is set, such a document is parsed again once with each of those values quoted. Original skills
 * are authored here, so their check passes `strict`.
 */
export function parseDoc(content: string, options: { strict?: boolean } = {}): ParsedDoc {
  const m = /^---\r?\n([\s\S]*?)\r?\n---\r?\n?/.exec(content);
  const raw = m?.[1];
  if (!m || raw === undefined) {
    return { frontmatter: {}, body: content };
  }
  const frontmatter = parseFrontmatter(raw, options.strict === true);
  return { frontmatter, body: content.slice(m[0].length).replace(/^\r?\n/, "") };
}

/** A top-level `key: value` line whose plain value holds a further ": ". */
const UNQUOTED_COLON_VALUE = /^([A-Za-z0-9_-]+):[ \t]+(?![\s"'|>[{&*!%@`])(.*?:[ \t].*?)[ \t]*$/gm;

function parseFrontmatter(raw: string, strict: boolean): Record<string, unknown> {
  try {
    return (parseYaml(raw) as Record<string, unknown> | null) ?? {};
  } catch (error) {
    const quoted = raw.replace(
      UNQUOTED_COLON_VALUE,
      (_, key: string, value: string) => `${key}: ${JSON.stringify(value)}`,
    );
    if (strict || quoted === raw) {
      throw error;
    }
    return (parseYaml(quoted) as Record<string, unknown> | null) ?? {};
  }
}

export function serializeDoc(doc: ParsedDoc): string {
  return `---\n${stringifyYaml(doc.frontmatter).trimEnd()}\n---\n\n${doc.body}`;
}
