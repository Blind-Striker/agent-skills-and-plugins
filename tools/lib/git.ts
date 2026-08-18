import { execFileSync } from "node:child_process";
import type { FileMode } from "./opencode-bundle.ts";

/**
 * Index modes keyed by repository-root-relative POSIX path. The index, not the filesystem: a
 * checkout with `core.filemode=false` reports 0644 for a file Git records as executable. Outside a
 * Git repository this returns an empty map so throwaway fixtures and consumer clones stay silent.
 */
export function indexModes(root: string, paths: string[] = []): Map<string, FileMode> {
  const modes = new Map<string, FileMode>();
  let output: string;
  try {
    output = execFileSync("git", ["-C", root, "ls-files", "-s", "--", ...paths], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
      maxBuffer: 64 * 1024 * 1024,
    });
  } catch {
    return modes;
  }

  for (const line of output.split("\n")) {
    const match = /^(\d{6}) [0-9a-f]+ \d+\t(.+)$/.exec(line);
    const mode = match?.[1];
    const path = match?.[2];
    if ((mode === "100644" || mode === "100755") && path) {
      modes.set(path.replaceAll("\\", "/"), mode);
    }
  }
  return modes;
}
