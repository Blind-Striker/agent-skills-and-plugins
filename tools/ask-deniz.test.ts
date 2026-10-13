import assert from "node:assert/strict";
import { mkdtempSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { assembleItem } from "./lib/assemble.ts";
import { parseDoc } from "./lib/frontmatter.ts";
import { loadManifest } from "./lib/manifest.ts";
import { ownSkillIdentities } from "./lib/own-skills.ts";
import { extractRefs } from "./lib/refs.ts";
import { declaredItems, resolveItem } from "./lib/resolve.ts";
import { buildRewriteMap, rewriteRefs } from "./lib/rewrite.ts";
import { scanSubmodule } from "./lib/scan.ts";

const root = join(import.meta.dirname, "..");

function estate() {
  const manifests = readdirSync(join(root, "curation"))
    .filter((name) => name.endsWith(".yaml"))
    .sort()
    .map((name) => loadManifest(join(root, "curation", name)));
  const components = readdirSync(join(root, "external"), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .flatMap((entry) => scanSubmodule(join(root, "external"), entry.name));
  const process = manifests.find((manifest) => manifest.plugin.name === "deniz-process");
  assert.ok(process);
  const item = process.items.find(
    (candidate) => resolveItem(root, process.plugin.name, candidate, components).outName === "ask-deniz",
  );
  assert.ok(item);
  const staging = mkdtempSync(join(tmpdir(), "ask-deniz-map-"));
  try {
    const assembled = assembleItem(root, staging, process, item, components);
    assert.ok(assembled);
    const map = buildRewriteMap(manifests, components, ownSkillIdentities(root, manifests));
    const text = rewriteRefs(readFileSync(join(assembled.dir, "SKILL.md"), "utf8"), map);
    return { item, text, manifests, components, refs: extractRefs(text) };
  } finally {
    rmSync(staging, { recursive: true, force: true });
  }
}

test("ask-deniz is both-invocable and maps every General item without starting manual ceremonies", () => {
  const { item, text, manifests, components, refs } = estate();
  assert.equal(item.invocation, "both");
  const general = manifests.find((manifest) => manifest.plugin.name === "deniz-dotnet-general");
  assert.ok(general);
  const targets = declaredItems(root, general, components);
  assert.equal(targets.length, 59);
  for (const target of targets) {
    if (target.outType === "agent") {
      assert.ok(text.includes(`\`${target.outName}\``), `missing delegated specialist ${target.outName}`);
      assert.ok(!refs.some((ref) => ref.name === target.outName), "agent must not be a fake skill invocation");
      continue;
    }
    const kind = target.intent.invocation === "manual" ? "pointer" : "model";
    assert.ok(
      refs.some((ref) => ref.ns === general.plugin.name && ref.name === target.outName && ref.kind === kind),
      `missing ${kind} route for ${target.outName}`,
    );
    if (kind === "pointer") {
      assert.ok(
        !refs.some((ref) => ref.name === target.outName && ref.kind === "model"),
        `manual load: ${target.outName}`,
      );
    }
  }
});

test("router and cheatsheet expose every manual ceremony as a human action", () => {
  const { manifests, components, refs } = estate();
  const cheat = readFileSync(join(root, "docs", "cheatsheet.md"), "utf8");
  const manual = manifests
    .flatMap((manifest) => declaredItems(root, manifest, components))
    .filter((target) => target.intent.invocation === "manual");
  assert.equal(manual.length, 30);
  for (const target of manual) {
    assert.ok(cheat.includes(`\`${target.outName}\``), `cheatsheet misses manual ${target.outName}`);
    assert.ok(
      refs.some((ref) => ref.ns === target.plugin && ref.name === target.outName && ref.kind === "pointer"),
      `router misses manual pointer ${target.outName}`,
    );
    assert.ok(!refs.some((ref) => ref.ns === target.plugin && ref.name === target.outName && ref.kind === "model"));
  }
});

test("General routing identities stay aligned with the human map", () => {
  const { text, manifests, components } = estate();
  const general = manifests.find((manifest) => manifest.plugin.name === "deniz-dotnet-general");
  assert.ok(general);
  const cheat = readFileSync(join(root, "docs", "cheatsheet.md"), "utf8");
  const section = cheat.slice(cheat.indexOf("## `deniz-dotnet-general`"), cheat.indexOf("## Specialized Modules"));
  for (const target of declaredItems(root, general, components)) {
    assert.ok(section.includes(`\`${target.outName}\``), `human map misses ${target.outName}`);
  }
  assert.match(parseDoc(text).frontmatter.description as string, /installed deniz Modules/);
});
