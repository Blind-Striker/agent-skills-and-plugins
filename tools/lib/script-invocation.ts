import { extname } from "node:path";

/**
 * The bundled-script interpreter convention (curation/SCHEMA.md "Bundled scripts"): a body that
 * tells the model to run a script shipped in a skill folder names the interpreter, because a
 * packager can strip the executable bit and a Windows shell runs no shebang. Upstream's own
 * writing-skills rule asks the same.
 */
export type Interpreter = "bash" | "pwsh" | "node" | "python";

/** A script a skill folder ships, as an invocation names it. */
export interface BundledScript {
  /** The path as the body should spell it, without a `./` or placeholder prefix. */
  path: string;
  interpreter: Interpreter;
}

export interface ScriptInvocationProblem {
  /** Offset of the script path the problem names. */
  index: number;
  message: string;
}

const BY_EXTENSION: Record<string, Interpreter> = {
  ".sh": "bash",
  ".ps1": "pwsh",
  ".js": "node",
  ".cjs": "node",
  ".mjs": "node",
  ".py": "python",
};
const BY_SHEBANG: Record<string, Interpreter> = {
  bash: "bash",
  sh: "bash",
  pwsh: "pwsh",
  node: "node",
  python: "python",
  python3: "python",
};

/** The interpreter a bundled file needs: its extension decides, or else its `#!` line; Markdown is never a script. */
export function scriptInterpreter(name: string, firstLine: string): Interpreter | undefined {
  const extension = extname(name).toLowerCase();
  if (extension === ".md") {
    return undefined;
  }
  if (extension in BY_EXTENSION) {
    return BY_EXTENSION[extension];
  }
  const shebang = /^#!\s*(\S+)(?:\s+(\S+))?/.exec(firstLine);
  if (!shebang) {
    return undefined;
  }
  const program = (shebang[1]?.endsWith("/env") ? shebang[2] : shebang[1])?.split("/").pop() ?? "";
  return BY_SHEBANG[program];
}

/** How the convention spells a run of `path`. */
function spelled(script: BundledScript): string {
  switch (script.interpreter) {
    case "bash":
      return `\`bash ${script.path}\``;
    case "node":
      return `\`node ${script.path}\``;
    case "pwsh":
      return `\`pwsh -File ${script.path}\``;
    case "python":
      return `\`python3 ${script.path}\`, with the Windows form \`py -3 ${script.path}\` beside it`;
  }
}

/** What names an interpreter right before a script path, and which one it names. */
const NAMED: [RegExp, Interpreter | "pwsh-without-file" | "py3"][] = [
  [/(?:^|[\s;|&(])bash$/, "bash"],
  [/(?:^|[\s;|&(])node$/, "node"],
  [/(?:^|[\s;|&(])python3$/, "python"],
  [/(?:^|[\s;|&(])py\s+-3$/, "py3"],
  [/(?:^|[\s;|&(])pwsh(?:\s+-\w+)*\s+-File$/, "pwsh"],
  [/(?:^|[\s;|&(])pwsh(?:\s+-\w+)*$/, "pwsh-without-file"],
];
/** A command position: line start, a shell prompt, or after a separator or command substitution. */
const COMMAND_POSITION = /(?:^|(?:^|\s)(?:\$|>|PS>)|\$\(|\(|&&|\|\||;|\||`)$/;
/** PowerShell's call or dot-source operator, which runs a script in the current PowerShell. */
const POWERSHELL_CALL = /(?:^|[\s;(])[&.]$/;
const POWERSHELL_FENCES = new Set(["powershell", "pwsh", "ps1", "ps"]);
/** A verb that makes a lone path in an inline code span an instruction to run it. */
const RUN_VERB = /\b(?:[Rr]e-?run|[Rr]un|[Ee]xecute)\b/;
/** A token as a shell line spells it: quoted, or up to the next separator. */
const TOKEN = /"([^"\s]+)"|'([^'\s]+)'|([^\s;|&()`'"=]+)/g;

interface Usage {
  python3: boolean;
  py3: boolean;
  index: number;
  script: BundledScript;
}

/**
 * Every run of a bundled script that does not name its interpreter, or names the wrong one, in one
 * Markdown file. Inline code is a run when it starts with the script path and carries arguments, or
 * when a run verb precedes it on its line; a fenced line is a run when the path sits at command
 * position. `resolve` maps a path token to the script it names, or undefined.
 */
export function scriptInvocationProblems(
  markdown: string,
  resolve: (token: string) => BundledScript | undefined,
): ScriptInvocationProblem[] {
  const problems: ScriptInvocationProblem[] = [];
  const python = new Map<string, Usage>();
  const named = (prefix: string, script: BundledScript, index: number): boolean => {
    const trimmed = prefix.trimEnd();
    const hit = NAMED.find(([pattern]) => pattern.test(trimmed))?.[1];
    if (hit === undefined) {
      return false;
    }
    if (hit === "pwsh-without-file") {
      problems.push({
        index,
        message: `runs bundled script ${script.path} with pwsh but no -File — write \`pwsh -File ${script.path}\``,
      });
      return true;
    }
    const interpreter: Interpreter = hit === "py3" ? "python" : hit;
    if (interpreter !== script.interpreter) {
      const used = hit === "py3" ? "py -3" : hit === "python" ? "python3" : hit;
      problems.push({
        index,
        message: `runs bundled script ${script.path} with ${used} — its interpreter is ${script.interpreter === "python" ? "python3" : script.interpreter}: write ${spelled(script)}`,
      });
      return true;
    }
    if (interpreter === "python") {
      const usage = python.get(script.path) ?? { python3: false, py3: false, index, script };
      usage[hit === "py3" ? "py3" : "python3"] = true;
      python.set(script.path, usage);
    }
    return true;
  };
  const bare = (script: BundledScript, index: number): void => {
    problems.push({ index, message: `runs bundled script ${script.path} by bare path — write ${spelled(script)}` });
  };

  let offset = 0;
  let fence: string | undefined;
  for (const line of markdown.split("\n")) {
    const opener = /^\s*(?:```|~~~)\s*([A-Za-z0-9_-]*)/.exec(line);
    if (opener) {
      fence = fence === undefined ? (opener[1] ?? "").toLowerCase() : undefined;
    } else if (fence !== undefined) {
      for (const m of line.matchAll(TOKEN)) {
        const token = m[1] ?? m[2] ?? m[3] ?? "";
        const script = resolve(token);
        if (!script) {
          continue;
        }
        const index = offset + m.index + (m[3] === undefined ? 1 : 0);
        const prefix = line.slice(0, m.index);
        if (named(prefix, script, index)) {
          continue;
        }
        const trimmed = prefix.trimEnd();
        if (POWERSHELL_CALL.test(trimmed)) {
          if (!(POWERSHELL_FENCES.has(fence) && script.interpreter === "pwsh")) {
            bare(script, index);
          }
        } else if (COMMAND_POSITION.test(trimmed)) {
          bare(script, index);
        }
      }
    } else {
      for (const m of line.matchAll(/`([^`]+)`/g)) {
        const content = m[1] ?? "";
        const words = content.trim().split(/\s+/);
        const first = words[0] ?? "";
        const spanStart = m.index + 1 + content.indexOf(first);
        const firstScript = resolve(first);
        if (firstScript) {
          if (words.length > 1 || RUN_VERB.test(line.slice(0, m.index))) {
            bare(firstScript, offset + spanStart);
          }
          continue;
        }
        // An interpreter first: find the script after its flags, then judge the whole prefix.
        for (let at = 1; at < words.length; at += 1) {
          const word = words[at] ?? "";
          if (word.startsWith("-")) {
            continue;
          }
          const script = resolve(word);
          if (script) {
            const within = content.indexOf(word, content.indexOf(first) + first.length);
            named(content.slice(0, within), script, offset + m.index + 1 + within);
          }
          break;
        }
      }
    }
    offset += line.length + 1;
  }
  for (const { python3, py3, index, script } of python.values()) {
    if (python3 && !py3) {
      problems.push({
        index,
        message: `runs bundled script ${script.path} with python3 but never shows its Windows form \`py -3 ${script.path}\``,
      });
    } else if (py3 && !python3) {
      problems.push({
        index,
        message: `runs bundled script ${script.path} with py -3 but never shows \`python3 ${script.path}\` beside it`,
      });
    }
  }
  return problems.sort((a, b) => a.index - b.index);
}
