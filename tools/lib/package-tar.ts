import { gunzipSync } from "node:zlib";
import type { FileMode } from "./opencode-bundle.ts";

export interface PackageTarEntry {
  path: string;
  content: Buffer;
  mode: FileMode;
}

const TAR_BLOCK = 512;

function tarString(field: Buffer): string {
  const end = field.indexOf(0);
  return field.subarray(0, end === -1 ? field.length : end).toString("utf8");
}

export function readPackageTar(tgz: Uint8Array): PackageTarEntry[] {
  const raw = gunzipSync(tgz);
  const entries: PackageTarEntry[] = [];
  let offset = 0;
  let pendingName: string | null = null;

  while (offset + TAR_BLOCK <= raw.length) {
    const header = raw.subarray(offset, offset + TAR_BLOCK);
    offset += TAR_BLOCK;
    if (header.every((byte) => byte === 0)) {
      break;
    }

    const name = tarString(header.subarray(0, 100));
    const prefix = tarString(header.subarray(345, 500));
    const size = Number.parseInt(tarString(header.subarray(124, 136)) || "0", 8);
    const rawMode = Number.parseInt(tarString(header.subarray(100, 108)) || "0", 8);
    if (!Number.isSafeInteger(size) || size < 0 || !Number.isSafeInteger(rawMode) || rawMode < 0) {
      throw new Error("Package tar contains an invalid size or mode field");
    }
    const typeflag = String.fromCharCode(header[156] ?? 0);
    const content = raw.subarray(offset, offset + size);
    if (content.length !== size) {
      throw new Error("Package tar entry extends beyond the archive");
    }
    offset += Math.ceil(size / TAR_BLOCK) * TAR_BLOCK;

    if (typeflag === "L") {
      pendingName = tarString(content);
      continue;
    }
    if (typeflag === "K" || typeflag === "x" || typeflag === "g") {
      continue;
    }

    const path = pendingName ?? (prefix.length > 0 ? `${prefix}/${name}` : name);
    pendingName = null;
    if (typeflag === "5" || path.endsWith("/")) {
      continue;
    }
    entries.push({
      path,
      content: Buffer.from(content),
      mode: (rawMode & 0o111) === 0 ? "100644" : "100755",
    });
  }

  return entries;
}
