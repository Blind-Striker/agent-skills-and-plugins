import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import type { CurationManifest } from "./manifest.ts";
import { collectOriginalSkillProblems, ownSkillIdentities } from "./own-skills.ts";

test("original skill identities come from Plugin names and top-level directories", () => {
  const root = mkdtempSync(join(tmpdir(), "own-skills-"));
  mkdirSync(join(root, "skills", "deniz-general", "writing-tests"), { recursive: true });
  mkdirSync(join(root, "skills", "deniz-process", "grilling"), { recursive: true });
  writeFileSync(join(root, "skills", "deniz-general", "NOTICE.md"), "not a skill\n");
  const manifests: CurationManifest[] = [
    { plugin: { name: "deniz-general", description: "d", version: "0.1.0" }, items: [] },
    { plugin: { name: "deniz-process", description: "d", version: "0.1.0" }, items: [] },
  ];

  assert.deepEqual(ownSkillIdentities(root, manifests), [
    { plugin: "deniz-general", name: "writing-tests", address: "deniz-general:writing-tests" },
    { plugin: "deniz-process", name: "grilling", address: "deniz-process:grilling" },
  ]);
});

const generalManifest = (names: string[]): CurationManifest => ({
  plugin: { name: "deniz-general", description: "d", version: "0.1.0" },
  original_skills: Object.fromEntries(names.map((name) => [name, { invocation: "auto" as const }])),
  items: [],
});

function plantSkill(root: string, path: string, name: string): void {
  mkdirSync(join(root, path), { recursive: true });
  writeFileSync(join(root, path, "SKILL.md"), `---\nname: ${name}\ndescription: d\n---\n\nBody.\n`);
}

test("every original skill directory pairs with exactly one declaration", () => {
  const root = mkdtempSync(join(tmpdir(), "own-skills-"));
  plantSkill(root, "skills/deniz-general/declared", "declared");
  plantSkill(root, "skills/deniz-general/undeclared", "undeclared");
  plantSkill(root, "skills/deniz-general/misnamed", "other");
  mkdirSync(join(root, "skills", "deniz-general", "empty"), { recursive: true });
  writeFileSync(join(root, "skills", "deniz-general", "NOTICE.md"), "not a skill\n");
  plantSkill(root, "skills/deniz-nowhere/lost", "lost");

  assert.deepEqual(
    collectOriginalSkillProblems(root, [generalManifest(["declared", "misnamed", "empty", "missing"])]),
    [
      "curation/deniz-general.yaml: original_skills.missing has no skills/deniz-general/missing/ directory — remove the declaration or add the skill",
      "skills/deniz-general/empty/SKILL.md is missing — an original skill is a directory with a SKILL.md",
      "skills/deniz-general/misnamed/SKILL.md: name is other but the directory is misnamed — the directory is the skill's identity in every harness",
      "skills/deniz-general/undeclared/ has no declaration — add original_skills: undeclared: with its invocation to curation/deniz-general.yaml",
      "skills/deniz-nowhere/ matches no curation manifest — the build never reads it; use a Module's plugin name or add curation/deniz-nowhere.yaml",
    ],
  );
});

test("a symlinked original skill is named, not followed", (t) => {
  const root = mkdtempSync(join(tmpdir(), "own-skills-"));
  plantSkill(root, "skills/deniz-general/real", "real");
  try {
    symlinkSync(join(root, "skills", "deniz-general", "real"), join(root, "skills", "deniz-general", "linked"), "dir");
  } catch {
    t.skip("creating symlinks requires elevated privileges on this platform");
    return;
  }

  assert.deepEqual(collectOriginalSkillProblems(root, [generalManifest(["real"])]), [
    "skills/deniz-general/linked is a symlink — original skills are real directories, and the build never follows a link",
  ]);
});

test("an original SKILL.md whose frontmatter does not parse is named", () => {
  const root = mkdtempSync(join(tmpdir(), "own-skills-"));
  mkdirSync(join(root, "skills", "deniz-general", "broken"), { recursive: true });
  writeFileSync(
    join(root, "skills", "deniz-general", "broken", "SKILL.md"),
    "---\nname: [broken\ndescription: d\n---\n\nBody.\n",
  );

  const problems = collectOriginalSkillProblems(root, [generalManifest(["broken"])]);
  assert.equal(problems.length, 1, problems.join("\n"));
  assert.match(problems[0] ?? "", /^skills\/deniz-general\/broken\/SKILL\.md: frontmatter does not parse — \S/);
});
