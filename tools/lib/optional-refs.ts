import { readFileSync } from "node:fs";
import { join } from "node:path";
import type { AssembledItem } from "./assemble.ts";
import { mappedSources, treeMarkdown } from "./harness-files.ts";
import { emitsTo, HARNESSES, type Harness, type SkillIntent } from "./manifest.ts";
import { listFiles } from "./overlay.ts";
import { scanRefs } from "./refs.ts";
import { markdownCodeBlocks, rewriteRefs, type RewriteTarget } from "./rewrite.ts";

export interface ReferenceFile {
  path: string;
  text: string;
  harnesses: readonly Harness[];
}

/** Prospective shipped Markdown, in canonical Claude spelling, never read from old output. */
export function assembledReferenceFiles(
  item: AssembledItem,
  map: Map<string, RewriteTarget>,
  primaryDocument: (harness: Harness) => string,
): ReferenceFile[] {
  const files = item.intent.harness_files;
  const sources = new Set(files ? mappedSources(files) : []);
  const readSource = (source: string): string => readFileSync(join(item.dir, source), "utf8");
  const shared = listFiles(item.dir)
    .filter((path) => path.endsWith(".md") && !sources.has(path))
    .map((path) => ({ path, text: rewriteRefs(readSource(path), map, "claude") }));
  const claude = [
    ...shared,
    ...Object.entries(files ?? {}).flatMap(([path, mapping]) =>
      mapping.claude !== null && path.endsWith(".md")
        ? [{ path, text: rewriteRefs(readSource(mapping.claude), map, "claude") }]
        : [],
    ),
  ];
  return HARNESSES.filter((harness) => emitsTo(item.intent, harness)).flatMap((harness) =>
    treeMarkdown(claude, files, harness, readSource, map)
      .filter((file) => item.outType === "skill" || harness === "codex" || file.path === "SKILL.md")
      .map((file) => ({
        path: `${harness}/${item.plugin}/${item.outName}/${file.path}`,
        text: file.path === "SKILL.md" ? rewriteRefs(primaryDocument(harness), map, "claude") : file.text,
        harnesses: [harness],
      })),
  );
}

/** Installed-Module scope at each real ATX heading; code and quote imitations establish no scope. */
function guardScopes(text: string): { start: number; end: number; module: string | undefined }[] {
  const code = markdownCodeBlocks(text);
  const lines = [...text.matchAll(/[^\n]*(?:\n|$)/g)].filter((line) => line[0] !== "");
  const headings: { start: number; level: number; module: string | undefined }[] = [];
  const frontmatterEnd = /^(?:\uFEFF)?---\r?\n[\s\S]*?\r?\n---(?:\r?\n|$)/.exec(text)?.[0].length ?? 0;
  for (const [i, line] of lines.entries()) {
    if (line.index < frontmatterEnd || code.some(([start, end]) => line.index >= start && line.index < end)) {
      continue;
    }
    const heading = /^ {0,3}(#{1,6})(?:[ \t]+|$)/.exec(line[0].replace(/\r?\n$/, ""));
    if (!heading) {
      continue;
    }
    const first = lines.slice(i + 1).find((next) => next[0].trim() !== "");
    const guard =
      first && !code.some(([start, end]) => first.index >= start && first.index < end)
        ? /^Applies only when the `([a-z0-9]+(?:-[a-z0-9]+)*)` Module is installed\.$/.exec(
            first[0].replace(/\r?\n$/, ""),
          )
        : null;
    headings.push({ start: line.index, level: heading[1]?.length ?? 1, module: guard?.[1] });
  }
  const stack: typeof headings = [];
  return headings.map((heading, i) => {
    while (stack.length && (stack.at(-1)?.level ?? 0) >= heading.level) {
      stack.pop();
    }
    stack.push(heading);
    return {
      start: heading.start,
      end: headings[i + 1]?.start ?? text.length,
      module: stack.findLast((entry) => entry.module)?.module,
    };
  });
}

/** Check optional occurrences without changing their fact kind, native spelling, or audience. */
export function collectOptionalGuardProblems(
  file: Pick<ReferenceFile, "path" | "text">,
  optional: ReadonlySet<string>,
  owners: Map<string, string>,
): string[] {
  const scopes = guardScopes(file.text);
  return scanRefs(file.text).flatMap((ref) => {
    if (!optional.has(ref.name)) {
      return [];
    }
    const module = owners.get(ref.name);
    const guard = scopes.find((scope) => ref.index >= scope.start && ref.index < scope.end)?.module;
    return guard === module && module !== undefined
      ? []
      : [
          `${file.path}: optional reference ${ref.address} requires a section guard for ${module ?? "its target Module"}`,
        ];
  });
}

/** Guard-only proof on current rendered bytes; never re-derive audience or dependencies from Codex. */
export function collectRenderedOptionalGuardProblems(
  file: Pick<ReferenceFile, "path" | "text">,
  harness: Exclude<Harness, "claude">,
  intent: SkillIntent,
  owners: Map<string, string>,
): string[] {
  const optional = new Set(intent.optional_refs ?? []);
  const text =
    harness === "opencode"
      ? file.text.replace(
          /(?<![A-Za-z0-9._/@-])([@/]?)([a-z0-9]+(?:-[a-z0-9]+)*)\.([a-z0-9]+(?:-[a-z0-9]+)*)(?![A-Za-z0-9_/-])/g,
          (whole, prefix: string, ns: string, name: string) =>
            optional.has(name) && owners.get(name) === ns ? `${prefix === "" ? "" : "/"}${ns}:${name}` : whole,
        )
      : file.text;
  return collectOptionalGuardProblems({ path: file.path, text }, optional, owners);
}

/** Two-way optional declarations over canonical shipped facts, independent of mandatory edges. */
export function collectOptionalReferenceProblems(
  id: string,
  intent: SkillIntent,
  files: ReferenceFile[],
  owners: Map<string, string>,
  requiredModules: readonly string[] = [],
): string[] {
  const problems: string[] = [];
  const optional = new Set(intent.optional_refs ?? []);
  const plugin = id.split("/")[0];
  const seen = new Set<string>();
  for (const name of optional) {
    const module = owners.get(name);
    if (module === undefined) {
      problems.push(`${id}: unknown optional_refs target ${name}`);
    } else if (module === plugin) {
      problems.push(`${id}: optional_refs target ${name} belongs to the same Module ${plugin}`);
    }
  }
  for (const file of files) {
    problems.push(...collectOptionalGuardProblems(file, optional, owners));
    for (const ref of scanRefs(file.text)) {
      const module = owners.get(ref.name);
      if (!optional.has(ref.name)) {
        if (ref.kind === "pointer" && ref.ns === module && module !== plugin && !requiredModules.includes(module)) {
          problems.push(
            `${id}: ${file.path}: undeclared optional reference ${ref.address} — add ${ref.name} to optional_refs and guard its section`,
          );
        }
        continue;
      }
      if (ref.ns !== module) {
        problems.push(
          `${id}: ${file.path}: optional reference namespace does not own ${ref.address} (target Module ${module ?? "unknown"})`,
        );
        continue;
      }
      seen.add(ref.name);
    }
  }
  for (const name of optional) {
    if (!seen.has(name)) {
      problems.push(`${id}: stale optional_refs: ${name} — no fact in any tree's shipped Markdown references it`);
    }
  }
  return problems;
}
