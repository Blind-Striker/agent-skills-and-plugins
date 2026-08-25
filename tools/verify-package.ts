import { readFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { digestFileMap, hashBytes, type FileIdentity, type ModuleManifest } from "./lib/opencode-bundle.ts";
import { type PackageTarEntry, readPackageTar } from "./lib/package-tar.ts";

const ROOT_FILES = ["package.json", "README.md", "LICENSE", "THIRD_PARTY_NOTICES.md"];
const DIST_FILES = [
  "dist/install-opencode.js",
  "dist/lib/opencode-bundle.js",
  "dist/lib/opencode-install-state.js",
  "dist/lib/opencode-install-plan.js",
  "dist/lib/opencode-install-apply.js",
  "dist/lib/order.js",
];
const SHA256 = /^sha256:[a-f0-9]{64}$/;

function parseManifest(content: Buffer, path: string, findings: string[]): ModuleManifest | null {
  let value: unknown;
  try {
    value = JSON.parse(content.toString("utf8"));
  } catch {
    findings.push(`${path}: invalid JSON`);
    return null;
  }
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    findings.push(`${path}: manifest must be an object`);
    return null;
  }
  const manifest = value as Partial<ModuleManifest>;
  if (
    manifest.schemaVersion !== 1 ||
    typeof manifest.module !== "string" ||
    typeof manifest.version !== "string" ||
    typeof manifest.digest !== "string" ||
    typeof manifest.files !== "object" ||
    manifest.files === null ||
    Array.isArray(manifest.files)
  ) {
    findings.push(`${path}: invalid manifest shape`);
    return null;
  }
  for (const [file, identity] of Object.entries(manifest.files)) {
    if (
      typeof identity !== "object" ||
      identity === null ||
      !SHA256.test((identity as FileIdentity).sha256) ||
      !["100644", "100755"].includes((identity as FileIdentity).mode)
    ) {
      findings.push(`${path}: invalid identity for ${file}`);
      return null;
    }
  }
  return manifest as ModuleManifest;
}

export function verifyPackageEntries(entries: PackageTarEntry[]): string[] {
  const findings: string[] = [];
  const files = new Map<string, PackageTarEntry>();
  for (const entry of entries) {
    if (!entry.path.startsWith("package/")) {
      findings.push(`${entry.path}: entry is outside package/`);
      continue;
    }
    const path = entry.path.slice("package/".length);
    if (files.has(path)) {
      findings.push(`${path}: duplicate tar entry`);
      continue;
    }
    files.set(path, entry);
  }

  const expected = new Set([...ROOT_FILES, ...DIST_FILES]);
  for (const path of expected) {
    if (!files.has(path)) {
      findings.push(`${path}: required Package file is missing`);
    }
  }

  const packageEntry = files.get("package.json");
  if (packageEntry) {
    try {
      const packageJson = JSON.parse(packageEntry.content.toString("utf8")) as {
        dependencies?: unknown;
        bin?: string | Record<string, string>;
      };
      if (packageJson.dependencies !== undefined) {
        findings.push("package.json: compiled installer must have no runtime dependencies");
      }
      const bins =
        typeof packageJson.bin === "string"
          ? [packageJson.bin]
          : packageJson.bin && typeof packageJson.bin === "object"
            ? Object.values(packageJson.bin)
            : [];
      for (const bin of bins) {
        if (files.get(bin)?.mode !== "100755") {
          findings.push(`${bin}: Package bin must be executable`);
        }
      }
    } catch {
      findings.push("package.json: invalid JSON");
    }
  }

  const manifests = [...files.entries()].filter(([path]) => /^opencode\/[^/]+\/manifest\.json$/.test(path));
  if (manifests.length === 0) {
    findings.push("opencode/: Package contains no Module manifests");
  }
  for (const [manifestPath, entry] of manifests) {
    expected.add(manifestPath);
    const moduleName = manifestPath.split("/")[1];
    const manifest = parseManifest(entry.content, manifestPath, findings);
    if (!manifest) {
      continue;
    }
    if (manifest.module !== moduleName) {
      findings.push(`${manifestPath}: Module name does not match its path`);
    }
    if (manifest.digest !== digestFileMap(manifest.files)) {
      findings.push(`${manifestPath}: Module digest does not match its file map`);
    }
    for (const [relativePath, identity] of Object.entries(manifest.files)) {
      const path = `opencode/${moduleName}/${relativePath}`;
      expected.add(path);
      const packed = files.get(path);
      if (!packed) {
        findings.push(`${path}: manifest-listed file is missing`);
        continue;
      }
      if (hashBytes(packed.content) !== identity.sha256) {
        findings.push(`${path}: bytes do not match the manifest sha256`);
      }
      if (packed.mode !== identity.mode) {
        findings.push(`${path}: tar mode ${packed.mode} does not match manifest mode ${identity.mode}`);
      }
    }
  }

  for (const path of files.keys()) {
    if (!expected.has(path)) {
      findings.push(`${path}: unexpected Package file`);
    }
  }
  return findings.sort();
}

export function verifyPackageBytes(tgz: Uint8Array): string[] {
  return verifyPackageEntries(readPackageTar(tgz));
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const packagePath = process.argv[2];
  if (!packagePath) {
    throw new Error("usage: node tools/verify-package.ts <package.tgz>");
  }
  const findings = verifyPackageBytes(readFileSync(packagePath));
  for (const finding of findings) {
    console.error(finding);
  }
  if (findings.length > 0) {
    process.exitCode = 1;
  } else {
    console.log("Package payload, hashes, and tar modes verified.");
  }
}
