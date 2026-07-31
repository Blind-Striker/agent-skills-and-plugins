import { execFileSync } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { parseDoc } from "./lib/frontmatter.ts";
import { type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { requireSubmodules } from "./lib/preflight.ts";
import { candidateHits } from "./lib/refs.ts";

/**
 * Frontmatter that decides how the shipped skill fires and what makes the model reach for it.
 * An item stating no invocation hands these to upstream, so upstream moving one moves the output.
 */
const POSTURE_KEYS = ["disable-model-invocation", "user-invocable", "description"] as const;

/** Reading upstream at either pin — the only impure thing the report needs, so it comes in. */
export interface SyncIO {
  /** File content at the old/new pin, or null when the path is unreadable there. */
  readFile: (rev: "old" | "new", rel: string) => string | null;
  /** Output names in the current ledger — the candidate universe. */
  ledgerNames: string[];
}

function show(v: unknown): string {
  return v === undefined ? "undefined" : JSON.stringify(v);
}

export function syncReport(sub: string, changed: string[], manifests: CurationManifest[], io: SyncIO): string[] {
  const lines: string[] = [];
  for (const m of manifests) {
    for (const item of m.items) {
      // A merge source may live in a different submodule than the item's primary, so this runs
      // outside — and before — the primary-source guard: the pin that moved is often the other one.
      for (const { source: msrc } of item.merged_from ?? []) {
        if (!msrc.startsWith(`${sub}/`)) {
          continue;
        }
        const mrel = msrc.slice(sub.length + 1);
        if (changed.some((c) => c === mrel || c.startsWith(`${mrel}/`))) {
          lines.push(
            `${m.plugin.name}: ${item.source} MERGE SOURCE ${msrc} changed upstream — review, then re-bless: git -C external/${sub} diff <old> <new> -- ${mrel}`,
          );
        }
      }
      if (!item.source.startsWith(`${sub}/`)) {
        continue;
      }
      const rel = item.source.slice(sub.length + 1);
      const hit = changed.some((c) => c === rel || c.startsWith(`${rel}/`));
      if (!hit) {
        continue;
      }
      const review = `git -C external/${sub} diff <old> <new> -- ${rel}`;
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
      // Beyond the path: what the move did to the two things a diff hides — the posture upstream
      // still owns, and the names this body points at.
      const skillRel = `${rel}/SKILL.md`;
      if (!changed.includes(skillRel)) {
        continue;
      }
      const before = io.readFile("old", skillRel);
      const after = io.readFile("new", skillRel);
      if (before === null || after === null) {
        continue;
      }
      if (!item.invocation) {
        const fa = parseDoc(before).frontmatter;
        const fb = parseDoc(after).frontmatter;
        for (const k of POSTURE_KEYS) {
          if (JSON.stringify(fa[k]) !== JSON.stringify(fb[k])) {
            lines.push(
              `${m.plugin.name}: ${item.source} POSTURE ${k}: ${show(fa[k])} -> ${show(fb[k])} (passthrough item — this flows straight into output)`,
            );
          }
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
  // Before the first build there is no universe to match against, and no candidates is the
  // truthful answer for that state.
  const ledgerNames = existsSync(ledgerPath)
    ? Object.keys(JSON.parse(readFileSync(ledgerPath, "utf8")) as Record<string, unknown>).map(
        (k) => k.split("/")[1] as string,
      )
    : [];

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
