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
