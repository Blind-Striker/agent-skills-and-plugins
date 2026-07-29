import { readdirSync, statSync } from "node:fs";
import { join } from "node:path";

/**
 * Fails while `external/` still holds uninitialised submodules. Git creates the mount points on
 * clone but leaves them empty, and an empty dir scans as "this upstream has no components" — so
 * without this every tool would report each curated source as unknown instead of saying `git`.
 */
export function requireSubmodules(root: string): void {
  const externalDir = join(root, "external");
  const empty = readdirSync(externalDir).filter(
    (s) => statSync(join(externalDir, s)).isDirectory() && readdirSync(join(externalDir, s)).length === 0,
  );
  if (empty.length) {
    throw new Error(
      `${empty.map((s) => `external/${s}`).join(", ")} are empty — run: git submodule update --init --recursive`,
    );
  }
}
