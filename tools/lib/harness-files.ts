import { cpSync, existsSync, lstatSync, mkdirSync, readdirSync, rmSync } from "node:fs";
import { dirname, join, relative, sep } from "node:path";
import type { AssembledItem } from "./assemble.ts";
import { type Harness, type HarnessFiles, HARNESSES } from "./manifest.ts";

/**
 * Every file a `harness_files` mapping reads (curation/SCHEMA.md). A mapped source ships in no tree
 * under its own name: each harness gets only its own source, at the mapping's output path.
 */
export function mappedSources(files: HarnessFiles): string[] {
  const sources = Object.values(files).flatMap((byHarness) =>
    HARNESSES.map((harness) => byHarness[harness]).filter((source): source is string => source !== null),
  );
  return [...new Set(sources)].sort();
}

function isFile(path: string): boolean {
  return existsSync(path) && lstatSync(path).isFile();
}

/**
 * What a declaration cannot do once its item is assembled — after omission, the patch, and the
 * overlay — found before any generated output is deleted: place a file in a non-skill artifact,
 * shadow a file the item already ships, or read a source that is not there.
 */
export function collectHarnessFileProblems(assembled: AssembledItem[]): string[] {
  const problems: string[] = [];
  for (const item of assembled) {
    const files = item.item?.harness_files;
    if (!files) {
      continue;
    }
    const id = `${item.plugin}/${item.outName}`;
    if (item.outType !== "skill") {
      problems.push(`${id}: harness_files applies to skill output only — a ${item.outType} ships one file`);
      continue;
    }
    for (const [output, byHarness] of Object.entries(files)) {
      if (existsSync(join(item.dir, output))) {
        problems.push(
          `${id}: harness_files ${output} is already a file of the assembled item — choose an output path nothing else ships`,
        );
      }
      for (const harness of HARNESSES) {
        const source = byHarness[harness];
        if (source !== null && !isFile(join(item.dir, source))) {
          problems.push(
            `${id}: harness_files ${output}: the ${harness} source ${source} is not in the assembled item — an omit, the patch, or upstream removed it`,
          );
        }
      }
    }
  }
  return problems;
}

/** Removes the directories a removal emptied, up to (not including) the skill folder. */
function pruneEmpty(skillFolder: string, dir: string): void {
  let current = dir;
  while (current !== skillFolder && !relative(skillFolder, current).startsWith("..")) {
    if (!existsSync(current) || readdirSync(current).length) {
      return;
    }
    rmSync(current, { recursive: true, force: true });
    current = dirname(current);
  }
}

/**
 * Lays one harness's files into a skill folder the emitter copied from the assembly: every mapped
 * source leaves, and that harness's source, if it has one, lands at the output path. Localization
 * runs later on each tree, so it reads only that tree's own file.
 */
export function placeHarnessFiles(
  assembledDir: string,
  skillFolder: string,
  files: HarnessFiles | undefined,
  harness: Harness,
): void {
  if (!files) {
    return;
  }
  const sources = mappedSources(files);
  for (const source of sources) {
    rmSync(join(skillFolder, ...source.split("/")), { force: true });
  }
  for (const [output, byHarness] of Object.entries(files)) {
    const source = byHarness[harness];
    if (source === null) {
      continue;
    }
    const target = join(skillFolder, ...output.split("/"));
    mkdirSync(dirname(target), { recursive: true });
    cpSync(join(assembledDir, ...source.split("/")), target);
  }
  for (const source of sources) {
    pruneEmpty(skillFolder, dirname(join(skillFolder, ...source.split("/"))));
  }
}

/** POSIX item-relative path of a file inside a skill folder. */
export function skillRelative(skillFolder: string, file: string): string {
  return relative(skillFolder, file).split(sep).join("/");
}
