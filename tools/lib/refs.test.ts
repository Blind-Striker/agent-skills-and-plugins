import assert from "node:assert/strict";
import { test } from "node:test";
import {
  candidateHits,
  extractRefs,
  scanPathClaims,
  scanRefs,
  scanSkillToolCalls,
  straySkillToolMentions,
} from "./refs.ts";

test("extractRefs finds namespaced references and classifies their kind by the leading slash", () => {
  const body = [
    "Use the superpowers:test-driven-development skill at a correct seam.",
    "When stuck, suggest opening /superpowers:brainstorming to the user.",
    "Plain prose with no references.",
  ].join("\n");
  const refs = extractRefs(body);
  assert.deepEqual(refs, [
    {
      kind: "model",
      ns: "superpowers",
      name: "test-driven-development",
      address: "superpowers:test-driven-development",
    },
    { kind: "pointer", ns: "superpowers", name: "brainstorming", address: "superpowers:brainstorming" },
  ]);
});

test("extractRefs ignores lookalikes: longer tokens, chained colons, URLs", () => {
  const body = [
    "2fa:setup starts inside a longer token", // char before ns is [a-z0-9-]
    "a:b:c is a chain, not a reference", // ':' on either side
    "https://example.com/skills/x is a URL", // no [a-z] follows the colon
    "C:\\Users\\deniz is a Windows path", // uppercase ns never matches
  ].join("\n");
  assert.deepEqual(extractRefs(body), []);
});

test("extractRefs ignores a match that starts inside a CamelCase token", () => {
  assert.deepEqual(extractRefs("see ServerName:tool_name here"), []);
});

test("extractRefs ignores a match that starts inside a snake_case token", () => {
  assert.deepEqual(extractRefs("see server_name:tool here"), []);
});

// A chain is rejected because `a:b` addresses nothing there — but a colon is also ordinary
// punctuation, and the sentence that ends a reference with one still names a real target.
test("a colon that opens prose ends the reference rather than chaining it", () => {
  assert.deepEqual(extractRefs("use superpowers:tdd: it gates the loop"), [
    { kind: "model", ns: "superpowers", name: "tdd", address: "superpowers:tdd" },
  ]);
});

// The rewrite replaces in place, so it needs the position of every hit — and it must be THIS scan's
// position, or the two readers disagree about what a reference is at exactly the margin.
test("scanRefs carries the offset of the address, the pointer slash excluded", () => {
  const body = "see /superpowers:brainstorming first";
  assert.deepEqual(scanRefs(body), [
    {
      kind: "pointer",
      ns: "superpowers",
      name: "brainstorming",
      address: "superpowers:brainstorming",
      index: body.indexOf("superpowers:brainstorming"),
    },
  ]);
});

// A namespace may carry hyphens (`dotnet-skills:akka-best-practices`), so a hyphenated lookalike is
// indistinguishable from a real one and extracts as the maximal address it spells — never as the
// namespace it happens to contain. That is exactly what rewriteRefs would touch: a curated
// `superpowers:beta` key never matches here, and the linker resolves the address actually written.
test("a hyphenated lookalike is its own address, not the namespace inside it", () => {
  assert.deepEqual(extractRefs("not-superpowers:beta"), [
    { kind: "model", ns: "not-superpowers", name: "beta", address: "not-superpowers:beta" },
  ]);
});

test("extractRefs keeps duplicates in order — callers decide about uniqueness", () => {
  const refs = extractRefs("superpowers:tdd then superpowers:tdd again");
  assert.equal(refs.length, 2);
});

test("candidateHits matches known names as standalone words only", () => {
  const names = ["research", "tdd", "writing-plans"];
  const body = [
    "Run /tdd where possible.", // slash prose — a hit
    "invoke the writing-plans skill", // bare name — a hit
    "researching is not the research skill", // 'researching' must not hit; bare 'research' does
    "superpowers:tdd", // fact spelling — colon boundary, no candidate hit
  ].join("\n");
  assert.deepEqual(candidateHits(body, names), ["research", "tdd", "writing-plans"]);
});

test("candidateHits returns nothing when no name appears", () => {
  assert.deepEqual(candidateHits("nothing here", ["tdd"]), []);
});

test("landing climbs are path claims only at the depth that reaches the skills directory", () => {
  const claims = (t: string, d: number) => scanPathClaims(t, d).map((c) => `${c.kind}:${c.segment}:${c.path}`);
  assert.deepEqual(claims("[a](../aspireify/SKILL.md)", 0), ["climb:aspireify:../aspireify/SKILL.md"]);
  assert.deepEqual(claims("[a](../../aspireify/SKILL.md)", 1), ["climb:aspireify:../../aspireify/SKILL.md"]);
  assert.deepEqual(claims("[a](../../aspireify/SKILL.md)", 0), [], "wrong depth");
  assert.deepEqual(claims("x/../aspireify/y", 0), [], "inside a longer path");
  assert.deepEqual(claims('AddCSharpApp("api", "../Api")', 0), [], "uppercase is illustrative");
  assert.deepEqual(claims("see `../using-superpowers/references/`.", 0), [
    "climb:using-superpowers:../using-superpowers/references/",
  ]);
  assert.deepEqual(claims("read ../beta/notes.md.", 0), ["climb:beta:../beta/notes.md"], "one trailing dot dropped");
  assert.deepEqual(
    claims("[a](../deniz-process.beta/SKILL.md)", 0),
    ["climb:deniz-process.beta:../deniz-process.beta/SKILL.md"],
    "a respelled OpenCode ID is read back, so P can judge the OpenCode tree",
  );
});

test("skill-tool calls: three forms, four verbs, payloads located", () => {
  const forms = (t: string) =>
    scanSkillToolCalls(t).map(
      (c) =>
        `${c.verb}|${c.form}|${c.payloads.map((p) => `${p.text}@${p.index}`).join(",")}|${t.slice(c.index, c.end)}`,
    );
  assert.deepEqual(forms('Call the Skill tool with "mattpocock-skills:grilling".'), [
    'Call|with|mattpocock-skills:grilling@26|Call the Skill tool with "mattpocock-skills:grilling"',
  ]);
  assert.deepEqual(forms('Always call the Skill tool twice, for "a" and "b", to pin'), [
    'call|twice|a@39,b@47|call the Skill tool twice, for "a" and "b"',
  ]);
  assert.deepEqual(forms('a subagent that calls the Skill tool with "research". Use'), [
    'calls|with|research@43|calls the Skill tool with "research"',
  ]);
  assert.deepEqual(forms('by calling the Skill tool with "prototype". Links'), [
    'calling|with|prototype@32|calling the Skill tool with "prototype"',
  ]);
  assert.deepEqual(forms("should call the Skill tool for."), ["call|generic||call the Skill tool for"]);
  assert.deepEqual(forms("call the Skill tool for whichever skills"), ["call|generic||call the Skill tool for"]);
  assert.deepEqual(forms('call the Skill tool for "x"'), [], "a single for-handle is not a form");
  assert.deepEqual(forms("call the Skill tool format"), []);
});

test("a skill-tool mention outside a recognized call is stray", () => {
  assert.deepEqual(straySkillToolMentions('Call the Skill tool with "a:b". call the Skill tool for.'), []);
  const text = 'Invoke the Skill tool on "x". Use the skill tool.';
  assert.deepEqual(straySkillToolMentions(text), [text.indexOf("Skill tool"), text.indexOf("skill tool.")]);
});
