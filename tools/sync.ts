import { execFileSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc } from "./lib/frontmatter.ts";
import { type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { type OwnSkillIdentity, ownSkillIdentities } from "./lib/own-skills.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { candidateHits } from "./lib/refs.ts";

/**
 * Frontmatter that decides how the shipped skill fires and what makes the model reach for it.
 * An item stating no invocation hands these to upstream, so upstream moving one moves the output.
 */
const POSTURE_KEYS = ["disable-model-invocation", "user-invocable", "description"] as const;

/** The two POSTURE_KEYS a stated `invocation:` overwrites; `description` is not one of them. */
const CLAUDE_INVOCATION_KEYS: readonly string[] = ["disable-model-invocation", "user-invocable"];

/** Reading upstream at either pin — the only impure thing the report needs, so it comes in. */
export interface SyncIO {
  /** File content at the old/new pin, or null when the path is unreadable there. */
  readFile: (rev: "old" | "new", rel: string) => string | null;
  /**
   * Whether a curated address — a skill directory as readily as a file — resolves at that pin.
   * Separate from `readFile` because a missing DIRECTORY is the case that matters here, and
   * because the answer is needed for every item, not only the ones a diff happened to list.
   */
  exists: (rev: "old" | "new", rel: string) => boolean;
  /** Where git says a path moved to at the new pin, when it detected the rename. */
  movedTo?: (rel: string) => string | null;
  /** Output names in the current ledger — the candidate universe. */
  ledgerNames: string[];
}

function show(v: unknown): string {
  return v === undefined ? "undefined" : JSON.stringify(v);
}

/**
 * Frontmatter, or null when it no longer parses.
 *
 * Defensive because this runs for every changed item, not only the passthrough ones: a single
 * upstream file with broken YAML would otherwise throw out of the whole report — and it would do it
 * *after* `git submodule update --remote` moved the pin, leaving a moved pin and no report at all.
 */
function frontmatterOf(text: string): Record<string, unknown> | null {
  try {
    return parseDoc(text).frontmatter;
  } catch {
    return null;
  }
}

/**
 * Output names out of ledger keys shaped `<plugin>/<kind>/<name>`.
 *
 * The name is everything past the kind, not segment 1: segment 1 IS the kind, so reading it made
 * the candidate universe the two words "skill" and "agent" and the report could only ever say a
 * body had gained or lost the word "skill".
 */
export function ledgerCandidateNames(ledger: Record<string, unknown>): string[] {
  const names = Object.keys(ledger)
    .map((k) => k.split("/").slice(2).join("/"))
    .filter(Boolean);
  return [...new Set(names)].sort();
}

export function syncCandidateNames(ledger: Record<string, unknown>, ownSkills: OwnSkillIdentity[]): string[] {
  return [...new Set([...ledgerCandidateNames(ledger), ...ownSkills.map((skill) => skill.name)])].sort();
}

/**
 * What a vanished address costs, in the words of the thing that will actually stop.
 *
 * A taken item is a hard build stop, and `collectProblems` says so before anything is deleted. An
 * excluded item is not — nothing resolves it — so its entry is merely a record pointing at an
 * address that no longer exists. Saying "changed upstream (auto-updated on next build)" for either,
 * which is what a path-list match alone produces, is the opposite of both.
 */
function goneLine(plugin: string, item: { source: string; exclude?: boolean }, where: string): string {
  return item.exclude
    ? `${plugin}: ${item.source} SOURCE GONE${where} — nothing breaks (excluded), but the exclusion record now names an address upstream does not have: repoint or drop it`
    : `${plugin}: ${item.source} SOURCE GONE${where} — nothing auto-updates: the next build stops with "source not found in external/"`;
}

export function syncReport(sub: string, changed: string[], manifests: CurationManifest[], io: SyncIO): string[] {
  const lines: string[] = [];
  const movedSuffix = (rel: string): string => {
    const dest = io.movedTo?.(rel);
    return dest ? ` (upstream moved it to ${dest})` : "";
  };
  for (const m of manifests) {
    for (const item of m.items) {
      // A merge source may live in a different submodule than the item's primary, so this runs
      // outside — and before — the primary-source guard: the pin that moved is often the other one.
      for (const { source: msrc } of item.merged_from ?? []) {
        if (msrc !== sub && !msrc.startsWith(`${sub}/`)) {
          continue;
        }
        const mrel = msrc === sub ? "" : msrc.slice(sub.length + 1);
        // A merge source that vanished cannot be re-blessed: `--bless` stamps upstream files by
        // hashing them, and there is nothing left to hash. Printing the re-bless command would be
        // handing over a command that cannot succeed.
        if (!io.exists("new", mrel)) {
          if (io.exists("old", mrel)) {
            lines.push(
              `${m.plugin.name}: ${item.source} MERGE SOURCE GONE ${msrc}${movedSuffix(mrel)} — it cannot be re-blessed; repoint merged_from or drop it`,
            );
          }
          continue;
        }
        if (mrel === "" ? changed.length > 0 : changed.some((c) => c === mrel || c.startsWith(`${mrel}/`))) {
          lines.push(
            `${m.plugin.name}: ${item.source} MERGE SOURCE ${msrc} changed upstream — review, then re-bless: git -C external/${sub} diff <old> <new> -- ${mrel || "."}`,
          );
        }
      }
      if (item.source !== sub && !item.source.startsWith(`${sub}/`)) {
        continue;
      }
      const rel = item.source === sub ? "" : item.source.slice(sub.length + 1);
      // Existence is asked BEFORE the changed-path list, and for every item rather than the ones
      // that list happens to name. `git diff --name-only` prints only the DESTINATION of a rename it
      // detected, so a curated source that upstream moved never appears there at all: the item would
      // be silent here and the build would stop later with no warning from this report.
      if (!io.exists("new", rel)) {
        if (io.exists("old", rel)) {
          lines.push(goneLine(m.plugin.name, item, movedSuffix(rel)));
        }
        continue;
      }
      const hit = rel === "" ? changed.length > 0 : changed.some((c) => c === rel || c.startsWith(`${rel}/`));
      if (!hit) {
        continue;
      }
      const documentRel = rel === "" ? "SKILL.md" : rel.endsWith(".md") ? rel : `${rel}/SKILL.md`;
      if (changed.includes(documentRel)) {
        const before = io.readFile("old", documentRel);
        const after = io.readFile("new", documentRel);
        if (before !== null && after === null) {
          const consequence = item.exclude
            ? "nothing breaks (excluded), but the exclusion record no longer names a scanner-visible component"
            : "the next build stops because the scanner no longer finds this component";
          lines.push(`${m.plugin.name}: ${item.source} COMPONENT DOCUMENT GONE at ${documentRel} — ${consequence}`);
          continue;
        }
        if (after !== null && !frontmatterOf(after)) {
          lines.push(
            `${m.plugin.name}: ${item.source} FRONTMATTER NO LONGER PARSES at the new pin — the next build stops during the upstream scan before it emits anything`,
          );
          continue;
        }
      }
      const review = `git -C external/${sub} diff <old> <new> -- ${rel || "."}`;
      let tag = "auto-updated on next build";
      if (item.exclude) {
        tag = "excluded — no action";
      } else if (item.body === "overlay") {
        // Nothing arrives on its own: the copy shadows upstream until it is re-blessed.
        tag = `OVERLAY — review, then re-bless: ${review}`;
      } else if (item.body === "patch") {
        // Either the change lands outside the patched region and is absorbed silently, or the build
        // stops. Both deserve a look, and neither is the "auto-updated" the default tag promises.
        tag = `PATCH — absorbed if it misses the patched region, otherwise the build stops: ${review}`;
      }
      lines.push(`${m.plugin.name}: ${item.source} changed upstream (${tag})`);
      // An excluded item emits nothing, so everything below — posture that "flows straight into
      // output", edges in a shipped body, an override that decides what the model reads — describes
      // an artifact that does not exist. The tag above already said the whole truth for it.
      if (item.exclude) {
        continue;
      }
      // Beyond the path: what the move did to the three things a diff hides — the posture upstream
      // still owns, the names this body points at, and the override standing in front of it.
      const skillRel = rel === "" ? "SKILL.md" : `${rel}/SKILL.md`;
      if (!changed.includes(skillRel)) {
        continue;
      }
      const before = io.readFile("old", skillRel);
      const after = io.readFile("new", skillRel);
      if (before === null || after === null) {
        continue;
      }
      const fa = frontmatterOf(before);
      const fb = frontmatterOf(after);
      if (!fb) {
        // The build parses this file too, so it stops on the same input. Saying that is more use
        // than the raw parser error this report used to die on.
        lines.push(
          `${m.plugin.name}: ${item.source} FRONTMATTER NO LONGER PARSES at the new pin — the build stops on this item before it emits anything`,
        );
        continue;
      }
      if (fa) {
        // Reported whether or not the item states an invocation. A stated one wins, so the flip does
        // not reach output — but upstream changing its mind about who triggers a skill is the
        // evidence the stated intent was weighed against, and it is the one thing a curator has to
        // re-ask on. Reporting only passthrough items made exactly that silent.
        for (const k of POSTURE_KEYS) {
          if (JSON.stringify(fa[k]) === JSON.stringify(fb[k])) {
            continue;
          }
          let consequence = "this flows straight into output";
          if (item.body === "overlay") {
            consequence = "the owned overlay replaces this key, so it does not reach output";
          } else if (CLAUDE_INVOCATION_KEYS.includes(k) && item.invocation) {
            consequence = `invocation: ${item.invocation} replaces this key, so it does not reach output — but upstream changed its mind about who triggers this item, so re-ask whether the stated intent still holds`;
          } else if (k === "description" && item.frontmatter?.[k] !== undefined) {
            consequence = "a frontmatter override replaces this key, so it does not reach output";
          }
          lines.push(
            `${m.plugin.name}: ${item.source} POSTURE ${k}: ${show(fa[k])} -> ${show(fb[k])} (${consequence})`,
          );
        }
        // An override merges in AFTER body assembly and carries no upstream stamp, so an upstream
        // rewrite never makes it drift: it goes on describing the body it was written against, and
        // the build cannot tell. This report is the only place that can say so.
        //
        // The trigger is the BODY moving, not the overridden key moving. Upstream can restructure a
        // skill end to end and leave its description untouched — which is exactly how an override
        // ends up describing one branch of a body that now has three, with every key still equal.
        //
        // An owned body is exempt: the overlay shadows upstream until it is re-blessed, so the
        // shipped body did not move, and the OVERLAY tag above already asked for that review. A
        // patched body is not exempt — upstream still flows everywhere the patch does not reach.
        const overridden = Object.keys(item.frontmatter ?? {});
        if (overridden.length && item.body !== "overlay") {
          const moved = overridden.filter((k) => JSON.stringify(fa[k]) !== JSON.stringify(fb[k]));
          const detail = moved.length
            ? `upstream moved it too — ${moved.map((k) => `${k}: ${show(fa[k])} -> ${show(fb[k])}`).join("; ")}`
            : "upstream's own value is unchanged, so the body moved underneath it instead";
          lines.push(
            `${m.plugin.name}: ${item.source} OVERRIDE ${overridden.join(", ")} — upstream's SKILL.md moved under a frontmatter override and nothing checks it, so reread the new body (${detail})`,
          );
        }
      }
      const na = new Set(candidateHits(before, io.ledgerNames));
      const nb = new Set(candidateHits(after, io.ledgerNames));
      const appeared = [...nb].filter((x) => !na.has(x)).map((x) => `+${x}`);
      const vanished = [...na].filter((x) => !nb.has(x)).map((x) => `-${x}`);
      if (appeared.length || vanished.length) {
        lines.push(
          `${m.plugin.name}: ${item.source} CANDIDATE EDGES ${[...appeared, ...vanished].join(" ")} — read the hits before believing them (they over-report)`,
        );
      }
    }
  }
  return lines;
}

function git(args: string[], cwd?: string): string {
  return execFileSync("git", args, { cwd, encoding: "utf8" }).trim();
}

/**
 * One tree listing per pin, then existence is a lookup. A curated address is usually a directory,
 * which is why a prefix counts: `git ls-tree -r` lists blobs, never the directories holding them.
 */
export function treeExists(files: Iterable<string>): (rel: string) => boolean {
  const set = new Set(files);
  return (rel) => (rel === "" ? set.size > 0 : set.has(rel) || [...set].some((f) => f.startsWith(`${rel}/`)));
}

function treeLookup(dir: string, rev: string): (rel: string) => boolean {
  return treeExists(git(["-C", dir, "ls-tree", "-r", "--name-only", rev]).split("\n").filter(Boolean));
}

/**
 * Old path -> new path for the renames git detected, collapsed from files back to their directory.
 * `pairs` arrives already parsed so the collapse rule can be tested without a repository.
 */
export function renameDestination(pairs: [string, string][]): (rel: string) => string | null {
  return (rel) => {
    for (const [from, to] of pairs) {
      // The curated address can BE the renamed file — every bare command or agent `.md` source is
      // one — and then there is no directory tail to strip and no prefix to match.
      if (from === rel) {
        return to;
      }
      if (!from.startsWith(`${rel}/`)) {
        continue;
      }
      // Otherwise the address is a directory and git renamed a file inside it. Strip the shared
      // tail to name the directory the curator has to repoint to, not one file within it. When the
      // tail does not survive the move, the destination file is still the most useful thing to
      // print, and the first match wins because one destination is the answer either way.
      const tail = from.slice(rel.length);
      return to.endsWith(tail) ? to.slice(0, to.length - tail.length) : to;
    }
    return null;
  };
}

function renameLookup(dir: string, before: string, after: string): (rel: string) => string | null {
  const pairs: [string, string][] = [];
  for (const line of git(["-C", dir, "diff", "--name-status", "-M", before, after]).split("\n")) {
    const [status, from, to] = line.split("\t");
    if (status?.startsWith("R") && from && to) {
      pairs.push([from, to]);
    }
  }
  return renameDestination(pairs);
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const root = process.cwd();
  // `git submodule update --remote` cannot bootstrap an uninitialised submodule, and the rev-parse
  // below would fail first with a bare git error, so say the init command instead.
  requireSubmodules(root);
  const only = process.argv[2];
  const subs = only ? [only] : readdirSync(join(root, "external"));
  const manifests = readdirSync(join(root, "curation"))
    .filter((f) => f.endsWith(".yaml"))
    .map((f) => loadManifest(join(root, "curation", f)));
  const ledgerPath = join(root, "docs", "ledger.json");
  const ledger = existsSync(ledgerPath)
    ? (JSON.parse(readFileSync(ledgerPath, "utf8")) as Record<string, unknown>)
    : {};
  const ledgerNames = syncCandidateNames(ledger, ownSkillIdentities(root, manifests));

  for (const sub of subs) {
    const dir = join(root, "external", sub);
    if (!existsSync(dir)) {
      console.error(`No such submodule: ${sub}`);
      process.exit(1);
    }
    const before = git(["rev-parse", "HEAD"], dir);
    execFileSync("git", ["submodule", "update", "--remote", "--", `external/${sub}`], { cwd: root, stdio: "inherit" });
    const after = git(["rev-parse", "HEAD"], dir);
    if (before === after) {
      console.log(`${sub}: up to date`);
      continue;
    }
    console.log(`${sub}: ${before.slice(0, 7)} -> ${after.slice(0, 7)}`);
    const changed = git(["diff", "--name-only", before, after], dir).split("\n").filter(Boolean);
    const atOld = treeLookup(dir, before);
    const atNew = treeLookup(dir, after);
    const movedTo = renameLookup(dir, before, after);
    const lines = syncReport(sub, changed, manifests, {
      // A path missing at one pin (added, deleted, renamed) is a failure here, not an exception:
      // stderr stays closed so git's "does not exist" does not drown the report.
      readFile: (rev, rel) => {
        try {
          return execFileSync("git", ["-C", dir, "show", `${rev === "old" ? before : after}:${rel}`], {
            encoding: "utf8",
            stdio: ["ignore", "pipe", "ignore"],
          });
        } catch {
          return null;
        }
      },
      exists: (rev, rel) => (rev === "old" ? atOld(rel) : atNew(rel)),
      movedTo,
      ledgerNames,
    });
    for (const l of lines) {
      console.log(`  ${l.replace("<old>", before.slice(0, 7)).replace("<new>", after.slice(0, 7))}`);
    }
    if (!lines.length) {
      console.log("  no curated items affected");
    }
  }
  console.log("Next: npm run build && npm run validate, review git diff, then commit.");
}
