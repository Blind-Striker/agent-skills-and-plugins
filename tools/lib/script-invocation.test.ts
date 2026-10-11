import assert from "node:assert/strict";
import { test } from "node:test";
import { type BundledScript, scriptInterpreter, scriptInvocationProblems } from "./script-invocation.ts";

const SCRIPTS: Record<string, BundledScript> = {
  "scripts/tool.sh": { path: "scripts/tool.sh", interpreter: "bash" },
  "scripts/task-start": { path: "scripts/task-start", interpreter: "bash" },
  "scripts/Report.ps1": { path: "scripts/Report.ps1", interpreter: "pwsh" },
  "scripts/lint.py": { path: "scripts/lint.py", interpreter: "python" },
  "render.js": { path: "render.js", interpreter: "node" },
  "../other/scripts/pack": { path: "../other/scripts/pack", interpreter: "bash" },
};
const resolve = (token: string): BundledScript | undefined =>
  SCRIPTS[token.replace(/^<[^>/]+>\//, "").replace(/^\.\//, "")];
const problems = (markdown: string): string[] => scriptInvocationProblems(markdown, resolve).map((p) => p.message);

test("an interpreter comes from the extension, or else from the shebang", () => {
  assert.equal(scriptInterpreter("tool.sh", ""), "bash");
  assert.equal(scriptInterpreter("Report.ps1", "param("), "pwsh");
  assert.equal(scriptInterpreter("lint.py", ""), "python");
  assert.equal(scriptInterpreter("server.cjs", ""), "node");
  assert.equal(scriptInterpreter("task-start", "#!/usr/bin/env bash"), "bash");
  assert.equal(scriptInterpreter("render-graphs", "#!/usr/bin/env node"), "node");
  assert.equal(scriptInterpreter("tool", "#!/usr/bin/env python3"), "python");
  assert.equal(scriptInterpreter("notes.md", "#!/usr/bin/env bash"), undefined, "Markdown is never a script");
  assert.equal(scriptInterpreter("data.json", "{"), undefined);
});

test("an inline command that runs a bundled script by bare path is a problem", () => {
  assert.deepEqual(problems("Run `scripts/tool.sh --all` first."), [
    "runs bundled script scripts/tool.sh by bare path — write `bash scripts/tool.sh`",
  ]);
  assert.deepEqual(problems("Run `scripts/Report.ps1` with `-Filter all`."), [
    "runs bundled script scripts/Report.ps1 by bare path — write `pwsh -File scripts/Report.ps1`",
  ]);
  assert.deepEqual(problems("Re-run `scripts/lint.py` to recount."), [
    "runs bundled script scripts/lint.py by bare path — write `python3 scripts/lint.py`, with the Windows form `py -3 scripts/lint.py` beside it",
  ]);
  assert.deepEqual(problems("Run `../other/scripts/pack PLAN BASE HEAD`."), [
    "runs bundled script ../other/scripts/pack by bare path — write `bash ../other/scripts/pack`",
  ]);
});

test("inline code is read as Markdown finds its spans, a wrapped or double-backtick span included", () => {
  assert.deepEqual(problems("Run `scripts/tool.sh\n--all` first."), [
    "runs bundled script scripts/tool.sh by bare path — write `bash scripts/tool.sh`",
  ]);
  assert.deepEqual(
    problems("Run ``echo `x` `` then `scripts/tool.sh`."),
    ["runs bundled script scripts/tool.sh by bare path — write `bash scripts/tool.sh`"],
    "a double-backtick span closes only at a double backtick, so the next span is the run",
  );
});

test("a mention of a bundled script that runs nothing is not a problem", () => {
  assert.deepEqual(problems("- Helper script (client-side): `scripts/tool.sh`"), []);
  assert.deepEqual(problems("Use `render.js` in this directory to render flowcharts."), []);
  assert.deepEqual(
    problems("- **`scripts/lint.py`** — a linter. Exit 1 on violations; advisory findings never fail the run."),
    [],
  );
  assert.deepEqual(problems("Copy `scripts/tool.sh` to the target path."), []);
});

test("the named interpreter must be the script's own", () => {
  assert.deepEqual(problems("Run `bash scripts/tool.sh` and `node render.js`."), []);
  assert.deepEqual(
    problems("Run `pwsh -File scripts/Report.ps1 -Path x` or `pwsh -NoProfile -File scripts/Report.ps1`."),
    [],
  );
  assert.deepEqual(problems("Run `node scripts/tool.sh`."), [
    "runs bundled script scripts/tool.sh with node — its interpreter is bash: write `bash scripts/tool.sh`",
  ]);
  assert.deepEqual(problems("Run `pwsh scripts/Report.ps1`."), [
    "runs bundled script scripts/Report.ps1 with pwsh but no -File — write `pwsh -File scripts/Report.ps1`",
  ]);
});

test("an interpreter word outside the convention is a wrong interpreter, in prose and in a fence", () => {
  const fence = (lang: string, body: string): string => `\`\`\`${lang}\n${body}\n\`\`\`\n`;
  const python = "write `python3 scripts/lint.py`, with the Windows form `py -3 scripts/lint.py` beside it";
  const cases: [string, string][] = [
    [
      "python scripts/lint.py",
      `runs bundled script scripts/lint.py with python — its interpreter is python3: ${python}`,
    ],
    [
      "python2 scripts/lint.py",
      `runs bundled script scripts/lint.py with python2 — its interpreter is python3: ${python}`,
    ],
    [
      "python3.12 scripts/lint.py",
      `runs bundled script scripts/lint.py with python3.12 — its interpreter is python3: ${python}`,
    ],
    [
      "python -u scripts/lint.py",
      `runs bundled script scripts/lint.py with python -u — its interpreter is python3: ${python}`,
    ],
    ["py scripts/lint.py", `runs bundled script scripts/lint.py with py — its interpreter is python3: ${python}`],
    ["py -2 scripts/lint.py", `runs bundled script scripts/lint.py with py -2 — its interpreter is python3: ${python}`],
    [
      "sh scripts/tool.sh",
      "runs bundled script scripts/tool.sh with sh — its interpreter is bash: write `bash scripts/tool.sh`",
    ],
    [
      "zsh scripts/tool.sh",
      "runs bundled script scripts/tool.sh with zsh — its interpreter is bash: write `bash scripts/tool.sh`",
    ],
    [
      "dash scripts/tool.sh",
      "runs bundled script scripts/tool.sh with dash — its interpreter is bash: write `bash scripts/tool.sh`",
    ],
    [
      "powershell -File scripts/Report.ps1",
      "runs bundled script scripts/Report.ps1 with powershell -File — its interpreter is pwsh: write `pwsh -File scripts/Report.ps1`",
    ],
    [
      "powershell.exe -ExecutionPolicy Bypass -File scripts/Report.ps1",
      "runs bundled script scripts/Report.ps1 with powershell.exe -ExecutionPolicy Bypass -File — its interpreter is pwsh: write `pwsh -File scripts/Report.ps1`",
    ],
    [
      "cmd /c scripts/Report.ps1",
      "runs bundled script scripts/Report.ps1 with cmd /c — its interpreter is pwsh: write `pwsh -File scripts/Report.ps1`",
    ],
  ];
  for (const [command, message] of cases) {
    assert.deepEqual(problems(`Run \`${command}\`.`), [message], `prose: ${command}`);
    assert.deepEqual(problems(fence("bash", command)), [message], `fence: ${command}`);
  }
});

test("the convention's own words still pass with flags of their own", () => {
  assert.deepEqual(problems("Run `bash -e scripts/tool.sh` and `node --no-warnings render.js`."), []);
  assert.deepEqual(problems("Run `python3 -u scripts/lint.py` (on Windows, `py -3 -u scripts/lint.py`)."), []);
  assert.deepEqual(problems("Run `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/Report.ps1`."), []);
});

test("a run in the current shell is named by its fence", () => {
  const fence = (lang: string, body: string): string => `\`\`\`${lang}\n${body}\n\`\`\`\n`;
  // Sourcing runs the script in the bash the fence names, so it needs no exec bit and no `bash`.
  assert.deepEqual(problems(fence("bash", "source scripts/tool.sh\n. ./scripts/tool.sh")), []);
  // PowerShell runs a .ps1 path itself: the call operator, dot-sourcing, and the bare path agree.
  assert.deepEqual(
    problems(
      fence("powershell", "& ./scripts/Report.ps1 -Path x\n. ./scripts/Report.ps1\n./scripts/Report.ps1 -Path x"),
    ),
    [],
  );
  assert.deepEqual(problems(fence("pwsh", "scripts/Report.ps1 -Path x")), []);
});

test("sourcing where no fence names the shell names no interpreter; & outside PowerShell is a bare run", () => {
  const fence = (lang: string, body: string): string => `\`\`\`${lang}\n${body}\n\`\`\`\n`;
  const unnamed = (op: string, where: string): string =>
    `runs bundled script scripts/tool.sh with ${op} ${where}, which names no interpreter — write \`bash scripts/tool.sh\``;
  assert.deepEqual(problems(fence("", "source scripts/tool.sh")), [unnamed("source", "in an unlabelled fence")]);
  assert.deepEqual(problems(fence("sh", "source scripts/tool.sh")), [unnamed("source", "in a sh fence")]);
  assert.deepEqual(problems(fence("shell", ". scripts/tool.sh")), [unnamed(".", "in a shell fence")]);
  assert.deepEqual(problems(fence("powershell", "source scripts/tool.sh")), [
    unnamed("source", "in a powershell fence"),
  ]);
  assert.deepEqual(problems("Run `source scripts/tool.sh`, `. scripts/tool.sh`, or `& scripts/Report.ps1`."), [
    unnamed("source", "in inline code"),
    unnamed(".", "in inline code"),
    "runs bundled script scripts/Report.ps1 by bare path — write `pwsh -File scripts/Report.ps1`",
  ]);
});

test("a current-shell run that hands a script to its exec bit is a bare run", () => {
  const fence = (lang: string, body: string): string => `\`\`\`${lang}\n${body}\n\`\`\`\n`;
  const bareSh = "runs bundled script scripts/tool.sh by bare path — write `bash scripts/tool.sh`";
  const barePs = "runs bundled script scripts/Report.ps1 by bare path — write `pwsh -File scripts/Report.ps1`";
  assert.deepEqual(problems(fence("powershell", ". scripts/tool.sh")), [bareSh]);
  assert.deepEqual(problems(fence("powershell", "& ./scripts/tool.sh")), [bareSh]);
  assert.deepEqual(problems(fence("bash", "./scripts/Report.ps1")), [barePs], "bash runs it through the exec bit");
  assert.deepEqual(problems(fence("bash", ". scripts/Report.ps1")), [
    "runs bundled script scripts/Report.ps1 with . in a bash fence — its interpreter is pwsh: write `pwsh -File scripts/Report.ps1`",
  ]);
  assert.deepEqual(problems(fence("bash", "source scripts/lint.py")), [
    "runs bundled script scripts/lint.py with source in a bash fence — its interpreter is python3: write `python3 scripts/lint.py`, with the Windows form `py -3 scripts/lint.py` beside it",
  ]);
});

test("a Python script names python3 and the Windows form py -3 together", () => {
  assert.deepEqual(problems("Run `python3 scripts/lint.py` (on Windows, `py -3 scripts/lint.py`)."), []);
  assert.deepEqual(problems("Run `python3 scripts/lint.py`."), [
    "runs bundled script scripts/lint.py with python3 but never shows its Windows form `py -3 scripts/lint.py`",
  ]);
  assert.deepEqual(problems("Run `py -3 scripts/lint.py`."), [
    "runs bundled script scripts/lint.py with py -3 but never shows `python3 scripts/lint.py` beside it",
  ]);
});

test("a fenced command line is judged at command position only", () => {
  const fence = (lang: string, body: string): string => `\`\`\`${lang}\n${body}\n\`\`\`\n`;
  assert.deepEqual(problems(fence("bash", "scripts/tool.sh --open")), [
    "runs bundled script scripts/tool.sh by bare path — write `bash scripts/tool.sh`",
  ]);
  assert.deepEqual(problems(fence("bash", "./render.js ../some-skill")), [
    "runs bundled script render.js by bare path — write `node render.js`",
  ]);
  assert.deepEqual(problems(fence("bash", "BASE=$(scripts/task-start plan.md 1)")), [
    "runs bundled script scripts/task-start by bare path — write `bash scripts/task-start`",
  ]);
  assert.deepEqual(problems(fence("bash", "bash scripts/tool.sh --open\nnode ./render.js x")), []);
  assert.deepEqual(problems(fence("bash", "cp scripts/tool.sh /tmp/x")), [], "an argument is not a command");
  assert.deepEqual(
    problems(fence("powershell", '& "<skill-directory>/scripts/Report.ps1" `\n    -Path x')),
    [],
    "PowerShell's call operator inside a PowerShell fence names its interpreter",
  );
  assert.deepEqual(problems(fence("bash", '& "<skill-directory>/scripts/Report.ps1"')), [
    "runs bundled script scripts/Report.ps1 by bare path — write `pwsh -File scripts/Report.ps1`",
  ]);
  assert.deepEqual(problems(`Run \`scripts/tool.sh\` later.\n${fence("", "[Resolve workspace: tool.sh plan.md]")}`), [
    "runs bundled script scripts/tool.sh by bare path — write `bash scripts/tool.sh`",
  ]);
});

test("each problem carries the offset of the path it names", () => {
  const text = "line one\nRun `scripts/tool.sh` now.\n";
  const [problem] = scriptInvocationProblems(text, resolve);
  assert.equal(problem?.index, text.indexOf("scripts/tool.sh"));
});
