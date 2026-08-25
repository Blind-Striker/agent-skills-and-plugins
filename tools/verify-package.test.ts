import assert from "node:assert/strict";
import { gzipSync } from "node:zlib";
import { test } from "node:test";
import { digestFileMap, hashBytes, type ModuleManifest } from "./lib/opencode-bundle.ts";
import { type PackageTarEntry, readPackageTar } from "./lib/package-tar.ts";
import { verifyPackageEntries } from "./verify-package.ts";

const DIST_FILES = [
  "dist/install-opencode.js",
  "dist/lib/opencode-bundle.js",
  "dist/lib/opencode-install-state.js",
  "dist/lib/opencode-install-plan.js",
  "dist/lib/opencode-install-apply.js",
  "dist/lib/order.js",
];

function entry(path: string, content = "", mode: PackageTarEntry["mode"] = "100644"): PackageTarEntry {
  return { path: `package/${path}`, content: Buffer.from(content), mode };
}

function validEntries(): PackageTarEntry[] {
  const script = Buffer.from("#!/bin/sh\n");
  const files = { "skills/alpha/run.sh": { sha256: hashBytes(script), mode: "100755" as const } };
  const manifest: ModuleManifest = {
    schemaVersion: 1,
    module: "deniz-process",
    version: "1.0.0",
    digest: digestFileMap(files),
    files,
  };
  return [
    entry("package.json", `${JSON.stringify({ bin: { "deniz-skills": "dist/install-opencode.js" } })}\n`),
    entry("README.md"),
    entry("LICENSE"),
    entry("THIRD_PARTY_NOTICES.md"),
    ...DIST_FILES.map((path) => entry(path, "", path === "dist/install-opencode.js" ? "100755" : "100644")),
    entry("opencode/deniz-process/manifest.json", `${JSON.stringify(manifest)}\n`),
    entry("opencode/deniz-process/skills/alpha/run.sh", script.toString("utf8"), "100755"),
  ];
}

test("Package verifier accepts an exact manifest-backed payload", () => {
  assert.deepEqual(verifyPackageEntries(validEntries()), []);
});

test("Package verifier rejects executable-mode loss in the tar transport", () => {
  const entries = validEntries();
  const script = entries.find((item) => item.path.endsWith("skills/alpha/run.sh"));
  assert.ok(script);
  script.mode = "100644";

  assert.deepEqual(verifyPackageEntries(entries), [
    "opencode/deniz-process/skills/alpha/run.sh: tar mode 100644 does not match manifest mode 100755",
  ]);
});

function writeTarField(header: Buffer, offset: number, length: number, value: string): void {
  header.write(value, offset, Math.min(length, Buffer.byteLength(value)), "ascii");
}

test("Package tar reader preserves executable identity from tar headers", () => {
  const content = Buffer.from("run\n");
  const header = Buffer.alloc(512);
  writeTarField(header, 0, 100, "package/run.sh");
  writeTarField(header, 100, 8, "0000755\0");
  writeTarField(header, 108, 8, "0000000\0");
  writeTarField(header, 116, 8, "0000000\0");
  writeTarField(header, 124, 12, `${content.length.toString(8).padStart(11, "0")}\0`);
  writeTarField(header, 136, 12, "00000000000\0");
  header.fill(0x20, 148, 156);
  header[156] = "0".charCodeAt(0);
  writeTarField(header, 257, 6, "ustar\0");
  writeTarField(header, 263, 2, "00");
  const checksum = header.reduce((sum, byte) => sum + byte, 0);
  writeTarField(header, 148, 8, `${checksum.toString(8).padStart(6, "0")}\0 `);
  const padding = Buffer.alloc(512 - content.length);
  const tgz = gzipSync(Buffer.concat([header, content, padding, Buffer.alloc(1024)]));

  assert.deepEqual(readPackageTar(tgz), [{ path: "package/run.sh", content, mode: "100755" }]);
});
