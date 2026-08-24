import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

interface TextFile {
  path: string;
  text: string;
}

export interface PublicSafetyFinding {
  path: string;
  line: number;
  kind: "gmail-identity" | "machine-path";
}

const MACHINE_PATH_FIXTURE = "experiments/harness-invocation/tests/fixtures/has-machine-path.txt";
const MACHINE_PATHS = [
  /[A-Za-z]:[\\/](?:Users|Documents and Settings)[\\/][^\s"'`<>|]+/g,
  /[A-Za-z]:[\\/][^\r\n"'`<>|]*[\\/]my-projects[\\/][^\s"'`<>|]+/g,
  /[A-Za-z]:[\\/]harness-probe-lab\b/g,
  /\/(?:Users|home)\/[A-Za-z0-9._-]+(?:\/[^\s"'`<>]+)*/g,
];
const GMAIL_IDENTITY = /[A-Za-z0-9._%+-]+@gmail\.com/gi;

function lineNumber(text: string, offset: number): number {
  let line = 1;
  for (let index = 0; index < offset; index += 1) {
    if (text.charCodeAt(index) === 10) {
      line += 1;
    }
  }
  return line;
}

export function findPublicContentFindings(files: TextFile[]): PublicSafetyFinding[] {
  const findings: PublicSafetyFinding[] = [];
  for (const file of files) {
    const path = file.path.replaceAll("\\", "/");
    if (path !== MACHINE_PATH_FIXTURE) {
      const lines = new Set<number>();
      for (const pattern of MACHINE_PATHS) {
        pattern.lastIndex = 0;
        for (let match = pattern.exec(file.text); match !== null; match = pattern.exec(file.text)) {
          lines.add(lineNumber(file.text, match.index));
        }
      }
      for (const line of [...lines].sort((left, right) => left - right)) {
        findings.push({ path, line, kind: "machine-path" });
      }
    }
    GMAIL_IDENTITY.lastIndex = 0;
    for (let match = GMAIL_IDENTITY.exec(file.text); match !== null; match = GMAIL_IDENTITY.exec(file.text)) {
      findings.push({ path, line: lineNumber(file.text, match.index), kind: "gmail-identity" });
    }
  }
  return findings.sort(
    (left, right) =>
      left.path.localeCompare(right.path) || left.line - right.line || left.kind.localeCompare(right.kind),
  );
}

function trackedTextFiles(root: string): TextFile[] {
  const paths = execFileSync("git", ["ls-files", "--cached", "--others", "--exclude-standard", "-z"], { cwd: root })
    .toString("utf8")
    .split("\0")
    .filter(Boolean);
  const files: TextFile[] = [];
  for (const path of paths) {
    const absolute = join(root, path);
    if (!existsSync(absolute) || !statSync(absolute).isFile()) {
      continue;
    }
    const bytes = readFileSync(absolute);
    if (bytes.includes(0)) {
      continue;
    }
    files.push({ path, text: bytes.toString("utf8") });
  }
  return files;
}

export function findRepositoryCurrentFindings(root: string): PublicSafetyFinding[] {
  return findPublicContentFindings(trackedTextFiles(root));
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  const currentFindings = findRepositoryCurrentFindings(process.cwd());
  for (const finding of currentFindings) {
    console.error(`${finding.path}:${finding.line}: ${finding.kind}`);
  }
  if (currentFindings.length > 0) {
    process.exitCode = 1;
  } else {
    console.log("Public-safety current-tree scan passed.");
  }
}
