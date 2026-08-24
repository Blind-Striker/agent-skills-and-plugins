import { cpSync, lstatSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import type { CurationManifest } from "./manifest.ts";

export interface Attribution {
  name: string;
  repository: string;
  license: string;
  copyright: string;
  licenseFile: string;
}

function isAttribution(value: unknown): value is Attribution {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    return false;
  }
  const entry = value as Record<string, unknown>;
  return ["name", "repository", "license", "copyright", "licenseFile"].every(
    (key) => typeof entry[key] === "string" && entry[key].length > 0,
  );
}

export function requireReadableRegularFile(path: string, label: string): void {
  try {
    const stat = lstatSync(path);
    if (!stat.isFile() || stat.isSymbolicLink()) {
      throw new Error("not a regular file");
    }
    readFileSync(path);
  } catch {
    throw new Error(`${label} must be an ordinary readable file`);
  }
}

export function loadAttributions(root: string): Map<string, Attribution> {
  const path = join(root, "curation", "attribution.json");
  const raw = JSON.parse(readFileSync(path, "utf8")) as unknown;
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) {
    throw new Error(`${path}: attribution registry must be an object`);
  }
  const attributions = new Map<string, Attribution>();
  for (const [source, value] of Object.entries(raw)) {
    if (!/^[a-z0-9-]+$/.test(source) || !isAttribution(value)) {
      throw new Error(`${path}: ${source} must define name, repository, license, copyright, and licenseFile`);
    }
    if (!/^[A-Za-z0-9._-]+$/.test(value.licenseFile)) {
      throw new Error(`${path}: ${source} licenseFile must be a root filename`);
    }
    const licensePath = join(root, "external", source, value.licenseFile);
    requireReadableRegularFile(licensePath, `${path}: ${source} license file external/${source}/${value.licenseFile}`);
    attributions.set(source, value);
  }
  return attributions;
}

function sourceName(address: string): string {
  return address.split("/", 1)[0] ?? address;
}

export function manifestAttributions(
  manifest: CurationManifest,
  attributions: Map<string, Attribution>,
): [string, Attribution][] {
  const sources = new Set<string>();
  for (const item of manifest.items) {
    if (item.exclude) {
      continue;
    }
    sources.add(sourceName(item.source));
    for (const merged of item.merged_from ?? []) {
      sources.add(sourceName(merged.source));
    }
  }
  return [...sources].sort().map((source) => {
    const attribution = attributions.get(source);
    if (attribution === undefined) {
      throw new Error(`${manifest.plugin.name}: source ${source} has no curation/attribution.json entry`);
    }
    return [source, attribution];
  });
}

function noticeText(entries: [string, Attribution][]): string {
  const lines = [
    "# Third-party notices",
    "",
    "This distribution includes transformed material from the projects below.",
    "The exact license text for each project is included at the listed path.",
  ];
  for (const [source, attribution] of entries) {
    lines.push(
      "",
      `## ${attribution.name}`,
      "",
      `- Source: <${attribution.repository}>`,
      `- License: ${attribution.license}`,
      `- Copyright: ${attribution.copyright}`,
      `- License text: \`third_party/${source}/LICENSE\``,
    );
  }
  return `${lines.join("\n")}\n`;
}

export function writeDistributionNotices(root: string, destination: string, entries: [string, Attribution][]): void {
  mkdirSync(destination, { recursive: true });
  cpSync(join(root, "LICENSE"), join(destination, "LICENSE"));
  writeFileSync(join(destination, "THIRD_PARTY_NOTICES.md"), noticeText(entries));
  for (const [source, attribution] of entries) {
    const target = join(destination, "third_party", source);
    mkdirSync(target, { recursive: true });
    cpSync(join(root, "external", source, attribution.licenseFile), join(target, "LICENSE"));
  }
}
