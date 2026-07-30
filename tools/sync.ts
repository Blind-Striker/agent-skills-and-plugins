import { execFileSync } from "node:child_process";
import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";
import { type CurationManifest, loadManifest } from "./lib/manifest.ts";
import { requireSubmodules } from "./lib/preflight.ts";

export function syncReport(sub: string, changed: string[], manifests: CurationManifest[]): string[] {
  const lines: string[] = [];
  for (const m of manifests) {
    for (const item of m.items) {
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
    const lines = syncReport(sub, changed, manifests);
    for (const l of lines) {
      console.log(`  ${l.replace("<old>", before.slice(0, 7)).replace("<new>", after.slice(0, 7))}`);
    }
    if (!lines.length) {
      console.log("  no curated items affected");
    }
  }
  console.log("Next: npm run build && npm run validate, review git diff, then commit.");
}
