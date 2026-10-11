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

/** The convention's name for an interpreter, as a wrong-interpreter message states it. */
function conventionName(interpreter: Interpreter): string {
  return interpreter === "python" ? "python3" : interpreter;
}

/**
 * An interpreter word right before a script path, with the flags between them; a flag may carry
 * one plain value, as `-ExecutionPolicy Bypass` does. The words are the convention's own and every
 * other spelling this check knows, so a wrong one is named instead of passing unseen.
 */
const INTERPRETER =
  /(?:^|[\s;|&(])((bash|sh|zsh|dash|ksh|node|python[\d.]*|py|pwsh|powershell|cmd)(?:\.exe)?)((?:\s+[-/][\w.:-]*(?:\s+[A-Za-z]\w*)?)*)$/i;
/** A run in the current shell: PowerShell's call operator, or dot-sourcing, or bash's `source`. */
const CURRENT_SHELL = /(?:^|[\s;(])(&|\.|source)$/;
/** A command position: line start, a shell prompt, or after a separator or command substitution. */
const COMMAND_POSITION = /(?:^|(?:^|\s)(?:\$|>|PS>)|\$\(|\(|&&|\|\||;|\||`)$/;
const POWERSHELL_FENCES = new Set(["powershell", "pwsh", "ps1", "ps"]);
const BASH_FENCES = new Set(["bash"]);
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
 * position. An interpreter word before the path names the interpreter, and only the convention's
 * words name it rightly. A run in the current shell needs no exec bit, so its fence names the
 * interpreter: `source` or `.` in a bash fence, and `&`, `.`, or the bare path in a PowerShell
 * fence. `resolve` maps a path token to the script it names, or undefined.
 */
export function scriptInvocationProblems(
  markdown: string,
  resolve: (token: string) => BundledScript | undefined,
): ScriptInvocationProblem[] {
  const problems: ScriptInvocationProblem[] = [];
  const python = new Map<string, Usage>();
  const wrong = (script: BundledScript, index: number, used: string): void => {
    problems.push({
      index,
      message: `runs bundled script ${script.path} with ${used} — its interpreter is ${conventionName(script.interpreter)}: write ${spelled(script)}`,
    });
  };
  const bare = (script: BundledScript, index: number): void => {
    problems.push({ index, message: `runs bundled script ${script.path} by bare path — write ${spelled(script)}` });
  };
  /** Judges the interpreter word before a script path; false when no interpreter word stands there. */
  const named = (trimmed: string, script: BundledScript, index: number): boolean => {
    const hit = INTERPRETER.exec(trimmed);
    if (!hit) {
      return false;
    }
    const word = hit[1] ?? "";
    const flags = (hit[3] ?? "").trim().split(/\s+/).filter(Boolean);
    const used = [word, ...flags].join(" ");
    let interpreter: Interpreter | undefined;
    let py3 = false;
    if (word === "bash" || word === "node") {
      interpreter = word;
    } else if (word === "python3") {
      interpreter = "python";
    } else if (word === "py" && flags[0] === "-3") {
      interpreter = "python";
      py3 = true;
    } else if (word === "pwsh") {
      if (flags.at(-1) !== "-File") {
        problems.push({
          index,
          message: `runs bundled script ${script.path} with pwsh but no -File — write \`pwsh -File ${script.path}\``,
        });
        return true;
      }
      interpreter = "pwsh";
    }
    if (interpreter !== script.interpreter) {
      wrong(script, index, used);
      return true;
    }
    if (interpreter === "python") {
      const usage = python.get(script.path) ?? { python3: false, py3: false, index, script };
      usage[py3 ? "py3" : "python3"] = true;
      python.set(script.path, usage);
    }
    return true;
  };
  /** A run in the current shell, judged by the fence that names that shell. */
  const currentShell = (op: string, fence: string, script: BundledScript, index: number): void => {
    if (POWERSHELL_FENCES.has(fence) && op !== "source") {
      if (script.interpreter !== "pwsh") {
        bare(script, index);
      }
    } else if (BASH_FENCES.has(fence) && op !== "&") {
      if (script.interpreter !== "bash") {
        wrong(script, index, `${op} in a ${fence} fence`);
      }
    } else {
      bare(script, index);
    }
  };
  /**
   * One command line: a fenced line, or an inline code span (`fence` undefined), whose first token
   * is a run only with arguments after it or a run verb before the span (`runVerb`).
   */
  const judgeLine = (line: string, base: number, fence: string | undefined, runVerb: boolean): void => {
    for (const m of line.matchAll(TOKEN)) {
      const token = m[1] ?? m[2] ?? m[3] ?? "";
      const script = resolve(token);
      if (!script) {
        continue;
      }
      const index = base + m.index + (m[3] === undefined ? 1 : 0);
      const trimmed = line.slice(0, m.index).trimEnd();
      if (named(trimmed, script, index)) {
        continue;
      }
      const op = CURRENT_SHELL.exec(trimmed)?.[1];
      if (op !== undefined) {
        currentShell(op, fence ?? "", script, index);
      } else if (COMMAND_POSITION.test(trimmed)) {
        if (fence === undefined && trimmed === "") {
          if (runVerb || line.slice(m.index + m[0].length).trim() !== "") {
            bare(script, index);
          }
        } else if (!(POWERSHELL_FENCES.has(fence ?? "") && script.interpreter === "pwsh" && /[\\/]/.test(token))) {
          // PowerShell runs a .ps1 path itself; any other shell needs the exec bit and a shebang.
          bare(script, index);
        }
      }
    }
  };

  let offset = 0;
  let fence: string | undefined;
  for (const line of markdown.split("\n")) {
    const opener = /^\s*(?:```|~~~)\s*([A-Za-z0-9_-]*)/.exec(line);
    if (opener) {
      fence = fence === undefined ? (opener[1] ?? "").toLowerCase() : undefined;
    } else if (fence !== undefined) {
      judgeLine(line, offset, fence, false);
    } else {
      for (const m of line.matchAll(/`([^`]+)`/g)) {
        judgeLine(m[1] ?? "", offset + m.index + 1, undefined, RUN_VERB.test(line.slice(0, m.index)));
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
