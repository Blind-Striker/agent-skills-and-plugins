import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationManifest } from "./manifest.ts";
import {
  buildRewriteMap,
  claudeOnlyVocabulary,
  isBundledText,
  localize,
  openCodeTodoWords,
  type RewriteTarget,
  renderHarnessPhrasing,
  rewriteOpenCodePaths,
  rewriteRefs,
} from "./rewrite.ts";
import type { ComponentInfo } from "./scan.ts";

const comp = (over: Partial<ComponentInfo>): ComponentInfo => ({
  submodule: "sp",
  namespace: "superpowers",
  type: "skill",
  name: "x",
  description: "",
  sourcePath: "sp/skills/x",
  files: 1,
  bytes: 1,
  ...over,
});
const manifest: CurationManifest = {
  plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
  items: [
    { source: "sp/skills/brainstorming" },
    { source: "sp/skills/tdd", name: "deniz-tdd" },
    { source: "sp/skills/dropped", exclude: true },
  ],
};
const components = [
  comp({ name: "brainstorming", sourcePath: "sp/skills/brainstorming" }),
  comp({ name: "tdd", sourcePath: "sp/skills/tdd" }),
  comp({ name: "dropped", sourcePath: "sp/skills/dropped" }),
];
const own = [{ plugin: "deniz-process", name: "my-own", address: "deniz-process:my-own" }];

test("map covers included items with renames, skips excluded", () => {
  const map = buildRewriteMap([manifest], components);
  assert.deepEqual(map.get("superpowers:brainstorming"), {
    plugin: "deniz-process",
    name: "brainstorming",
    kind: "skill",
  });
  assert.deepEqual(map.get("superpowers:tdd"), { plugin: "deniz-process", name: "deniz-tdd", kind: "skill" });
  assert.equal(map.has("superpowers:dropped"), false);
});

test("map records the resolved target kind", () => {
  const m: CurationManifest = {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [{ source: "sp/skills/brainstorming", as: "command" }],
  };
  assert.equal(buildRewriteMap([m], components).get("superpowers:brainstorming")?.kind, "command");
});

test("one map renders all three styles", () => {
  const map = buildRewriteMap([manifest], components, own);
  const text = "Use superpowers:brainstorming, then /deniz-process:my-own.";
  assert.equal(rewriteRefs(text, map, "claude"), "Use deniz-process:brainstorming, then /deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "codex"), "Use $deniz-process:brainstorming, then $deniz-process:my-own.");
  assert.equal(rewriteRefs(text, map, "opencode"), "Use deniz-process.brainstorming, then @deniz-process.my-own.");
});

test("OpenCode renders dotted IDs and picks the pointer prefix by target kind", () => {
  const m: CurationManifest = {
    plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
    items: [{ source: "sp/skills/brainstorming" }, { source: "sp/skills/tdd", as: "command", name: "handoff" }],
  };
  const map = buildRewriteMap([m], components, own);
  assert.equal(
    rewriteRefs(
      "Use superpowers:brainstorming. Open /superpowers:brainstorming or /superpowers:tdd or /deniz-process:my-own.",
      map,
      "opencode",
    ),
    "Use deniz-process.brainstorming. Open @deniz-process.brainstorming or /deniz-process.handoff or @deniz-process.my-own.",
  );
});

test("sibling climbs are respelled only when they land on the shared skills directory", () => {
  const ids = new Map([
    ["aspireify", "deniz-dotnet-aspire.aspireify"],
    ["requesting-code-review", "deniz-process.requesting-code-review"],
  ]);
  assert.equal(
    rewriteOpenCodePaths("[a](../aspireify/SKILL.md)", 0, ids),
    "[a](../deniz-dotnet-aspire.aspireify/SKILL.md)",
  );
  assert.equal(
    rewriteOpenCodePaths("[a](../../aspireify/SKILL.md)", 1, ids),
    "[a](../../deniz-dotnet-aspire.aspireify/SKILL.md)",
  );
  assert.equal(
    rewriteOpenCodePaths("[a](../../aspireify/SKILL.md)", 0, ids),
    "[a](../../aspireify/SKILL.md)",
    "wrong depth",
  );
  assert.equal(
    rewriteOpenCodePaths('"Dispatch (../requesting-code-review/code-reviewer.md)"', 0, ids),
    '"Dispatch (../deniz-process.requesting-code-review/code-reviewer.md)"',
    "prose climb",
  );
  assert.equal(
    rewriteOpenCodePaths('AddCSharpApp("api", "../src/Api")', 1, ids),
    'AddCSharpApp("api", "../src/Api")',
    "not an emitted skill",
  );
  assert.equal(rewriteOpenCodePaths("x/../aspireify/y", 0, ids), "x/../aspireify/y", "inside a longer path");
});

test("OpenCode path respelling covers item-root paths and climbs in any text", () => {
  const ids = new Map([
    ["brainstorming", "deniz-process.brainstorming"],
    ["beta", "deniz-process.beta"],
  ]);
  assert.equal(
    rewriteOpenCodePaths("`skills/brainstorming/visual-companion.md`", 0, ids),
    "`skills/deniz-process.brainstorming/visual-companion.md`",
  );
  assert.equal(rewriteOpenCodePaths("cat ../../beta/notes.md", 1, ids), "cat ../../deniz-process.beta/notes.md");
  assert.equal(
    rewriteOpenCodePaths('cat "$(dirname "$0")/../../beta/notes.md"', 1, ids),
    'cat "$(dirname "$0")/../../deniz-process.beta/notes.md"',
    "a climb anchored at the script's own directory",
  );
  assert.equal(
    rewriteOpenCodePaths('cat "$HOME/../../beta/notes.md"', 1, ids),
    'cat "$HOME/../../beta/notes.md"',
    "inside a longer path",
  );
  assert.equal(rewriteOpenCodePaths("skills/testing/x.md", 0, ids), "skills/testing/x.md", "not an emitted skill");
  const respelled = "`skills/deniz-process.beta/x.md` and ../deniz-process.beta/y.md";
  assert.equal(rewriteOpenCodePaths(respelled, 0, ids), respelled, "an ID segment is already respelled");
});

test("bundled text has no NUL byte and survives a UTF-8 round trip", () => {
  assert.equal(isBundledText(Buffer.from("#!/bin/sh\necho ok\n")), true);
  assert.equal(isBundledText(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x00])), false);
  assert.equal(isBundledText(Buffer.from([0x63, 0xff, 0x0a])), false);
});

// Claude Code addresses a plugin skill by its DIRECTORY name, not by the frontmatter name — and
// those diverge for 32 of the 223 upstream components, so keying the map on the frontmatter name
// rewrote references nobody writes and left the real ones (`dotnet-skills:akka-best-practices`)
// pointing at upstream.
test("map keys on the upstream address, not the frontmatter name", () => {
  const map = buildRewriteMap(
    [
      {
        plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
        items: [{ source: "sp/skills/dir-name" }],
      },
    ],
    [comp({ name: "fancy-name", sourcePath: "sp/skills/dir-name" })],
  );
  assert.deepEqual(map.get("superpowers:dir-name"), { plugin: "deniz-process", name: "fancy-name", kind: "skill" });
  assert.equal(map.has("superpowers:fancy-name"), false);
});

test("commands and agents are addressed by file name without the extension", () => {
  const map = buildRewriteMap(
    [
      {
        plugin: { name: "deniz-process", description: "d", version: "0.1.0" },
        items: [{ source: "sp/commands/do-it.md" }],
      },
    ],
    [comp({ type: "command", name: "fancy-name", sourcePath: "sp/commands/do-it.md" })],
  );
  assert.deepEqual(map.get("superpowers:do-it"), { plugin: "deniz-process", name: "fancy-name", kind: "command" });
});

test("original skill targets resolve to their Plugin and output name", () => {
  assert.deepEqual(buildRewriteMap([manifest], components, own).get("deniz-process:my-own"), {
    plugin: "deniz-process",
    name: "my-own",
    kind: "skill",
  });
});

test("an original skill rewrite key cannot overwrite another target", () => {
  const colliding = [comp({ namespace: "deniz-process", name: "upstream-name", sourcePath: "sp/skills/my-own" })];
  const collisionManifest: CurationManifest = {
    plugin: manifest.plugin,
    items: [{ source: "sp/skills/my-own", name: "other-target" }],
  };
  assert.throws(
    () => buildRewriteMap([collisionManifest], colliding, own),
    /reference identity deniz-process:my-own.*deniz-process:other-target.*deniz-process:my-own/,
  );
});

test("rewriteRefs replaces longest keys first", () => {
  const map = new Map<string, RewriteTarget>([
    ["sp:foo", { plugin: "p", name: "foo", kind: "skill" }],
    ["sp:foo-bar", { plugin: "p", name: "foo-bar", kind: "skill" }],
  ]);
  assert.equal(rewriteRefs("use sp:foo-bar then sp:foo", map), "use p:foo-bar then p:foo");
});

// `sp:foo-bar` is a different component that happens to start with a curated one's name. Rewriting
// its prefix produced `p:foo-bar`, a reference to a skill that was never curated.
test("rewriteRefs stops at ref-token boundaries", () => {
  const map = new Map<string, RewriteTarget>([["sp:foo", { plugin: "p", name: "foo", kind: "skill" }]]);
  assert.equal(rewriteRefs("sp:foo-bar and sp:foobar and sp:foo", map), "sp:foo-bar and sp:foobar and p:foo");
  assert.equal(rewriteRefs("xsp:foo", map), "xsp:foo");
  assert.equal(rewriteRefs("(sp:foo).", map), "(p:foo).");
});

test("Codex renders model edges and user pointers as valid namespaced dollar references", () => {
  const map = new Map<string, RewriteTarget>([
    ["superpowers:tdd", { plugin: "deniz-process", name: "test-driven-development", kind: "skill" }],
    ["superpowers:brainstorming", { plugin: "deniz-process", name: "brainstorming", kind: "skill" }],
  ]);
  assert.equal(
    rewriteRefs("Use superpowers:tdd, or tell the user to open /superpowers:brainstorming.", map, "codex"),
    "Use $deniz-process:test-driven-development, or tell the user to open $deniz-process:brainstorming.",
  );
});

test("Codex rewriting keeps token boundaries and literal dollar amounts", () => {
  const map = new Map<string, RewriteTarget>([["sp:foo", { plugin: "plugin", name: "renamed", kind: "skill" }]]);
  assert.equal(
    rewriteRefs("$25, sp:foo-bar, xsp:foo, and sp:foo.", map, "codex"),
    "$25, sp:foo-bar, xsp:foo, and $plugin:renamed.",
  );
});

test("Codex map preserves the owning plugin across cross-plugin targets and renames", () => {
  const other: CurationManifest = {
    plugin: { name: "deniz-other", description: "Other", version: "0.1.0" },
    items: [{ source: "sp/skills/tdd", name: "renamed-tdd" }],
  };
  const map = buildRewriteMap([other], components);
  assert.equal(rewriteRefs("Use superpowers:tdd.", map, "codex"), "Use $deniz-other:renamed-tdd.");
});

test("Codex leaves a dangling authored address visible for validation", () => {
  assert.equal(rewriteRefs("Use upstream:missing.", new Map(), "codex"), "Use upstream:missing.");
});

const PHRASES = [
  'Call the Skill tool with "sp:a".',
  'Always call the Skill tool twice, for "sp:a" and "sp:b".',
  'a subagent that calls the Skill tool with "sp:a". By calling the Skill tool with "sp:b".',
  "naming which skills the next agent should call the Skill tool for.",
  "Subagent (general-purpose):",
  "dispatch a `general-purpose`\nsubagent. Then send one message with two `Agent` calls, one `Agent` call",
  "Dispatch a fresh general-purpose subagent with this prompt.",
].join("\n");

test("harness phrasing: Claude Code is identity", () => {
  assert.equal(renderHarnessPhrasing(PHRASES, "claude"), PHRASES);
});

test("harness phrasing: OpenCode names its skill and subagent tools", () => {
  assert.equal(
    renderHarnessPhrasing(PHRASES, "opencode"),
    [
      'Call the `skill` tool with "sp:a".',
      'Always call the `skill` tool twice, for "sp:a" and "sp:b".',
      'a subagent that calls the `skill` tool with "sp:a". By calling the `skill` tool with "sp:b".',
      "naming which skills the next agent should call the `skill` tool for.",
      "Subagent (general):",
      "dispatch a `general`\nsubagent. Then send one message with two `subagent` calls, one `subagent` call",
      "Dispatch a fresh `general` subagent with this prompt.",
    ].join("\n"),
  );
});

test("harness phrasing: Codex invokes and names no subagent tool or type", () => {
  assert.equal(
    renderHarnessPhrasing(PHRASES, "codex"),
    [
      "Invoke `sp:a`.",
      "Always invoke `sp:a` and `sp:b`.",
      "a subagent that invokes `sp:a`. By invoking `sp:b`.",
      "naming which skills the next agent should invoke.",
      "Subagent:",
      "dispatch a subagent. Then send one message with two subagent calls, one subagent call",
      "Dispatch a fresh subagent with this prompt.",
    ].join("\n"),
  );
});

test("localize renders the facts after the phrasing", () => {
  const map = buildRewriteMap([manifest], components);
  const line = 'Call the Skill tool with "superpowers:brainstorming".';
  assert.equal(localize(line, map, "claude"), 'Call the Skill tool with "deniz-process:brainstorming".');
  assert.equal(localize(line, map, "opencode"), 'Call the `skill` tool with "deniz-process.brainstorming".');
  assert.equal(localize(line, map, "codex"), "Invoke `$deniz-process:brainstorming`.");
});

test("every non-Claude rendering is free of Claude-only vocabulary", () => {
  assert.deepEqual(
    claudeOnlyVocabulary(PHRASES)
      .map((hit) => hit.match)
      .sort(),
    [
      "Skill tool",
      "Skill tool",
      "Skill tool",
      "Skill tool",
      "Skill tool",
      "Subagent (general-purpose)",
      "`Agent`",
      "`Agent`",
      "`general-purpose`",
      "general-purpose subagent",
    ].sort(),
  );
  for (const style of ["opencode", "codex"] as const) {
    assert.deepEqual(claudeOnlyVocabulary(renderHarnessPhrasing(PHRASES, style)), [], style);
  }
});

test("an unbackticked general-purpose subagent inside a code span is left for the vocabulary check", () => {
  const span = "Run `general-purpose subagent` here.";
  for (const style of ["opencode", "codex"] as const) {
    const rendered = renderHarnessPhrasing(span, style);
    assert.equal(rendered, span, style);
    assert.deepEqual(
      claudeOnlyVocabulary(rendered).map((hit) => hit.match),
      ["general-purpose subagent"],
      style,
    );
  }
});

test("a general-purpose subagent anywhere inside a code span is left whole for the vocabulary check", () => {
  for (const span of [
    "Run `dispatch a general-purpose subagent` here.",
    "Run ``a `x` then a general-purpose subagent`` here.",
    "A span may wrap: `dispatch a\ngeneral-purpose subagent` here.",
    "Run `a **general-purpose** subagent` here.",
  ]) {
    for (const style of ["opencode", "codex"] as const) {
      const rendered = renderHarnessPhrasing(span, style);
      assert.equal(rendered, span, `${style}: ${span}`);
      assert.equal(claudeOnlyVocabulary(rendered).length, 1, `${style}: ${span}`);
    }
  }
});

test("a general-purpose subagent outside every code span still renders per harness", () => {
  const text = [
    "Use `x`, then dispatch a general-purpose subagent; `y` follows.",
    "A lone ` backtick opens nothing: a general-purpose subagent.",
    "```text",
    "Prompt: dispatch a general-purpose subagent. A ` inside a fence is literal.",
    "```",
    "After the fence, a general-purpose subagent.",
  ].join("\n");
  assert.equal(
    renderHarnessPhrasing(text, "opencode"),
    [
      "Use `x`, then dispatch a `general` subagent; `y` follows.",
      "A lone ` backtick opens nothing: a `general` subagent.",
      "```text",
      "Prompt: dispatch a `general` subagent. A ` inside a fence is literal.",
      "```",
      "After the fence, a `general` subagent.",
    ].join("\n"),
  );
  assert.deepEqual(claudeOnlyVocabulary(renderHarnessPhrasing(text, "codex")), []);
});

test("an emphasized general-purpose subagent renders per harness and is Claude-only vocabulary", () => {
  const text = "Dispatch a **general-purpose** subagent, or a *general-purpose* subagent.";
  assert.equal(
    renderHarnessPhrasing(text, "opencode"),
    "Dispatch a **`general`** subagent, or a *`general`* subagent.",
  );
  assert.equal(renderHarnessPhrasing(text, "codex"), "Dispatch a subagent, or a subagent.");
  assert.deepEqual(
    claudeOnlyVocabulary(text).map((hit) => hit.match),
    ["general-purpose** subagent", "general-purpose* subagent"],
  );
});

test("an underscore-emphasized general-purpose subagent renders like the star forms", () => {
  const text = "Dispatch a _general-purpose_ subagent, or a __general-purpose__ subagent.";
  assert.equal(
    renderHarnessPhrasing(text, "opencode"),
    "Dispatch a _`general`_ subagent, or a __`general`__ subagent.",
  );
  assert.equal(renderHarnessPhrasing(text, "codex"), "Dispatch a subagent, or a subagent.");
  assert.deepEqual(
    claudeOnlyVocabulary(text).map((hit) => hit.match),
    ["general-purpose_ subagent", "general-purpose__ subagent"],
  );
  for (const style of ["opencode", "codex"] as const) {
    assert.deepEqual(claudeOnlyVocabulary(renderHarnessPhrasing(text, style)), [], style);
    const span = "Run `a _general-purpose_ subagent` here.";
    assert.equal(renderHarnessPhrasing(span, style), span, `${style}: a code span stays whole`);
    assert.equal(claudeOnlyVocabulary(span).length, 1, `${style}: and is left for the vocabulary check`);
  }
  assert.equal(renderHarnessPhrasing("a snake_general-purpose subagent", "codex"), "a snake_general-purpose subagent");
});

test("Claude Code's TodoWrite tool name is Claude-only vocabulary; OpenCode 1's lowercase word is not", () => {
  assert.deepEqual(
    claudeOnlyVocabulary("Track it with TodoWrite, or `TodoWrite`.").map((hit) => hit.match),
    ["TodoWrite", "TodoWrite"],
  );
  assert.deepEqual(claudeOnlyVocabulary("The OpenCode 1 `todowrite` tool was removed."), []);
});

// Every upstream todo phrase the Process bodies carry, in their own line breaks.
const TODO_PHRASES = [
  '"task-done: run tests, ledger the result; mark todo complete" [shape=box];',
  '"Append completion to ledger, mark todo complete" [shape=box];',
  "Track progress in a ledger file, not only in todos. Harness todos are a",
  "live view; the ledger is the record.",
  "Read the plan once, note its context and Global Constraints, and create a",
  "todo per task. If the plan names a Spec, read that too.",
  "- Mark the task's todo in_progress.",
  "A failing run records nothing. When it records,",
  "mark the todo complete and take the next task.",
  "[Create todos for all tasks]",
  "Create a todo per step. Steps 5–7 run only on their stated condition.",
  "If it has a checklist, create a todo per item.",
  "**IMPORTANT: Create a todo for EACH checklist item below.**",
  "- Use tracking: todos for checklists",
  "✅ Checklists without todo tracking = steps get skipped. Every time.",
  "❌ Some people find a todo list helpful for checklists.",
  "a spec or plan file, a todo-list record whose",
].join("\n");

test("OpenCode todo phrasing: Claude Code and Codex keep upstream's todo words", () => {
  assert.equal(renderHarnessPhrasing(TODO_PHRASES, "claude"), TODO_PHRASES);
  assert.equal(renderHarnessPhrasing(TODO_PHRASES, "codex"), TODO_PHRASES);
});

test("OpenCode todo phrasing: a ledger skill keeps its checklist in the ledger, any other a visible one", () => {
  assert.equal(
    renderHarnessPhrasing(TODO_PHRASES, "opencode"),
    [
      `"task-done: run tests, ledger the result; tick the task's checklist line" [shape=box];`,
      `"Append completion to ledger, tick the task's checklist line" [shape=box];`,
      "Track progress in a ledger file. The ledger is both the",
      "live view and the record.",
      "Read the plan once, note its context and Global Constraints, and add a",
      "Markdown checklist with one line per task to the ledger, unless the ledger already has one. If the plan names a Spec, read that too.",
      "- Leave the task's checklist line unticked until task-done records it.",
      "A failing run records nothing. When it records,",
      "tick the task's checklist line and take the next task.",
      "[Add a checklist of all tasks to the ledger]",
      "Keep a visible Markdown checklist with one line per step, and tick each line as it is done. Steps 5–7 run only on their stated condition.",
      "If it has a checklist, keep a visible Markdown checklist with one line per item, and tick each line as it is done.",
      "**IMPORTANT: Copy EACH checklist item below into a visible Markdown checklist, and tick each line as it is done.**",
      "- Use tracking: a visible Markdown checklist, ticked line by line",
      "✅ Checklists without line-by-line ticking = steps get skipped. Every time.",
      "❌ Some people find ticking lines off helpful for checklists.",
      "a spec or plan file, a Markdown checklist whose",
    ].join("\n"),
  );
});

test("every todo word outside code is caught; OpenCode's rendering of the table has none", () => {
  assert.deepEqual(
    openCodeTodoWords(TODO_PHRASES).map((hit) => hit.match),
    [
      "todo",
      "todo",
      "todos",
      "todos",
      "todo",
      "todo",
      "todo",
      "todos",
      "todo",
      "todo",
      "todo",
      "todos",
      "todo",
      "todo",
      "todo",
    ],
    "a todo word in every row of the table",
  );
  assert.deepEqual(openCodeTodoWords(renderHarnessPhrasing(TODO_PHRASES, "opencode")), []);
  assert.deepEqual(openCodeTodoWords("Then add a new todo, or update the To-Do list."), [
    { index: 15, match: "todo" },
    { index: 35, match: "To-Do" },
  ]);
});

test("a paraphrase no table row renders still fails the OpenCode todo check", () => {
  for (const paraphrase of [
    "Use the todo list to track the plan.",
    "Track each task as a todo.",
    "Check off each todo as it is done.",
    "Make a todo for the review.",
    "Write todos for every step.",
    "Create a to-do list first.",
    "Create a TODO per step.",
  ]) {
    assert.equal(renderHarnessPhrasing(paraphrase, "opencode"), paraphrase, `no row renders "${paraphrase}"`);
    assert.equal(openCodeTodoWords(paraphrase).length, 1, paraphrase);
  }
  assert.deepEqual(openCodeTodoWords("a todo_list, and a todos_x"), [
    { index: 2, match: "todo" },
    { index: 19, match: "todos" },
  ]);
});

test("OpenCode 1's todo tool names are todo words in prose; OpenCode 2 has no such tool", () => {
  assert.deepEqual(
    openCodeTodoWords("Track it with todowrite, read it with todoread, or call TodoWrite and TODOREAD.").map(
      (hit) => hit.match,
    ),
    ["todowrite", "todoread", "TodoWrite", "TODOREAD"],
  );
  assert.deepEqual(openCodeTodoWords("The OpenCode 1 `todowrite` and `todoread` tools were removed."), []);
});

test("a todo word spelled with a non-ASCII hyphen or dash is still a todo word", () => {
  assert.deepEqual(
    openCodeTodoWords("Keep a to‐do, a to‑do list, a to–do, and two to—dos.").map((hit) => hit.match),
    ["to‐do", "to‑do", "to–do", "to—dos"],
  );
});

test("the two-word 'to do' is ordinary English, not a todo word", () => {
  for (const prose of ["List the things to do next.", "What to do when the build fails", "To do this, run it."]) {
    assert.deepEqual(openCodeTodoWords(prose), [], prose);
  }
});

test("a todo word in a URL is part of an address, not prose", () => {
  for (const url of [
    "See https://example.com/docs/to-do for the list.",
    "Read <https://example.com/guides/todo> first.",
    "Follow [the guide](https://example.com/to-do) and [the other](http://example.com/a/todos?x=1).",
  ]) {
    assert.deepEqual(openCodeTodoWords(url), [], url);
  }
  assert.deepEqual(
    openCodeTodoWords("Open https://example.com/to-do, then add a todo.").map((hit) => hit.match),
    ["todo"],
    "prose after the URL is still read",
  );
});

test("a TODO marker is code only when it is written as code", () => {
  for (const code of [
    "Insert a `// TODO: guard param` comment at the site.",
    "Vitest skips `test.todo` cases.",
    "```csharp\n// TODO: guard param\nvar x = param;\n```\n",
    "~~~\nTODO: later\n~~~\n",
    "Before.\n\n```\nTODO: an unclosed fence runs to the end\n",
  ]) {
    assert.deepEqual(openCodeTodoWords(code), [], code);
  }
  assert.deepEqual(
    openCodeTodoWords("Leave a TODO: marker in prose, and add a TODO comment to the stub.").map((hit) => hit.match),
    ["TODO", "TODO"],
  );
  const literal = "Write `create a todo per item` as the example.";
  assert.deepEqual(openCodeTodoWords(literal), [], "a code span is literal");
  assert.equal(renderHarnessPhrasing(literal, "opencode"), literal, "a code span is left whole");
});

test("a TODO marker is code in every CommonMark code block and in HTML code", () => {
  for (const code of [
    // A fence in a list item, indented four or more spaces to the item's content.
    "1. Add the guard:\n\n    ```csharp\n    // TODO: guard param\n    ```\n",
    "- Add the guard:\n\n      ~~~\n      TODO: later\n      ~~~\n",
    "1. Step\n   - Nested:\n\n       ```\n       TODO: later\n       ```\n",
    // A fence in a blockquote, and in a list item inside one.
    "> Quoted:\n>\n> ```\n> TODO: later\n> ```\n",
    "> - Item\n>\n>   ```\n>   TODO: later\n>   ```\n",
    // An indented code block, at the top level and in a list item.
    "Before.\n\n    // TODO: guard param\n    var x = param;\n",
    "Before.\n\n    // TODO: one\n\n    // TODO: two after a blank line\n",
    "- Item\n\n      // TODO: code in the item\n",
    // HTML code.
    "Write <code>// TODO: guard</code> at the site.",
    'Write <code class="x">TODO</code> and <CODE>todo</CODE>.',
  ]) {
    assert.deepEqual(openCodeTodoWords(code), [], code);
  }
});

test("prose around and after CommonMark code is still read", () => {
  const prose = (text: string, expected: string[]): void => {
    assert.deepEqual(
      openCodeTodoWords(text).map((hit) => hit.match),
      expected,
      text,
    );
  };
  // An indented line that continues a list item or a paragraph is prose, not code.
  prose("1. Step one\n\n    Then keep a todo list.\n", ["todo"]);
  prose("- Item\n\n    Add a todo here.\n", ["todo"]);
  prose("A paragraph\n    with an indented todo line.\n", ["todo"]);
  prose("> Quoted prose with a todo.\n", ["todo"]);
  // A blockquote or list item that ends closes the fence it held.
  prose("> ```\n> TODO: code\nA todo after the quote.\n", ["todo"]);
  prose("- Step\n\n  ```\n  TODO: code\nA todo after the item.\n", ["todo"]);
  // An indented code block ends at the first less-indented line.
  prose("Before.\n\n    TODO: code\nA todo after the code.\n", ["todo"]);
  // HTML code covers only its own element.
  prose("<code>todo</code>, but a todo outside it.", ["todo"]);
  prose("An unclosed <code>todo stays prose.", ["todo"]);
  // Inside a top-level fence, a fence line indented four spaces closes nothing.
  prose("```\n    ```\nTODO: still in the fence\n```\nA todo after it.\n", ["todo"]);
});

test("a fence in a list item or a blockquote holds no code span", () => {
  const text = "1. Prompt:\n\n    ```text\n    dispatch a general-purpose subagent, `x`\n    ```\n";
  assert.equal(
    renderHarnessPhrasing(text, "opencode"),
    "1. Prompt:\n\n    ```text\n    dispatch a `general` subagent, `x`\n    ```\n",
  );
  const quoted = "> ```\n> dispatch a general-purpose subagent, `x`\n> ```\n";
  assert.equal(renderHarnessPhrasing(quoted, "codex"), "> ```\n> dispatch a subagent, `x`\n> ```\n");
});
