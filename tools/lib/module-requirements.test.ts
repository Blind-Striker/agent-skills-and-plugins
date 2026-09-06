import assert from "node:assert/strict";
import { test } from "node:test";
import type { CurationItem, CurationManifest } from "./manifest.ts";
import { deriveModuleRequirements } from "./resolve.ts";

function moduleManifest(name: string, items: CurationItem[]): CurationManifest {
  return { plugin: { name, description: name, version: "1.0.0" }, items };
}

test("requirements: derive owning Modules, not item names", () => {
  const manifests = [
    moduleManifest("consumer", [
      { source: "source/consumer", name: "entry", depends_on: ["renamed", "local", "renamed", "own"] },
      { source: "source/local", name: "local" },
    ]),
    moduleManifest("provider", [{ source: "source/old", name: "renamed" }]),
    moduleManifest("originals", []),
  ];
  const result = deriveModuleRequirements(
    ".",
    manifests,
    [],
    [{ plugin: "originals", name: "own", address: "originals:own" }],
  );
  assert.deepEqual(
    [...result],
    [
      ["consumer", ["originals", "provider"]],
      ["originals", []],
      ["provider", []],
    ],
  );
});

test("requirements: reject unknown owners", () => {
  const manifests = [moduleManifest("consumer", [{ source: "source/entry", name: "entry", depends_on: ["missing"] }])];
  assert.throws(() => deriveModuleRequirements(".", manifests, [], []), /unknown.*missing/);
});

test("requirements: reject cross-kind ambiguity rather than choosing an owner", () => {
  const manifests = [
    moduleManifest("consumer", [{ source: "s/entry", name: "entry", depends_on: ["shared"] }]),
    moduleManifest("a", [{ source: "s/a", name: "shared", as: "skill" }]),
    moduleManifest("b", [{ source: "s/b", name: "shared", as: "agent" }]),
  ];
  assert.throws(() => deriveModuleRequirements(".", manifests, [], []), /ambiguous.*shared/);
});

test("requirements: an excluded item declares no edges", () => {
  const manifests = [
    moduleManifest("consumer", [
      { source: "s/excluded", name: "excluded", exclude: true, depends_on: ["ghost"] },
      { source: "s/kept", name: "kept" },
    ]),
    moduleManifest("ghosts", [{ source: "s/g", name: "ghost" }]),
  ];
  assert.deepEqual(
    [...deriveModuleRequirements(".", manifests, [], [])],
    [
      ["consumer", []],
      ["ghosts", []],
    ],
  );
});

test("requirements: a dependency whose only provider is excluded is unknown", () => {
  const manifests = [
    moduleManifest("consumer", [{ source: "s/entry", name: "entry", depends_on: ["gone"] }]),
    moduleManifest("dropper", [{ source: "s/g", name: "gone", exclude: true }]),
  ];
  assert.throws(() => deriveModuleRequirements(".", manifests, [], []), /unknown.*gone/);
});

test("requirements: an empty Module maps to no requirements", () => {
  assert.deepEqual([...deriveModuleRequirements(".", [moduleManifest("solo", [])], [], [])], [["solo", []]]);
});

test("requirements: intra-Module cycles collapse to self and stay local", () => {
  const manifests = [
    moduleManifest("loop", [
      { source: "s/a", name: "alpha", depends_on: ["beta"] },
      { source: "s/b", name: "beta", depends_on: ["alpha"] },
    ]),
  ];
  assert.deepEqual([...deriveModuleRequirements(".", manifests, [], [])], [["loop", []]]);
});

test("requirements: cross-Module cycles are recorded in both directions", () => {
  const manifests = [
    moduleManifest("a", [{ source: "s/a", name: "alpha", depends_on: ["beta"] }]),
    moduleManifest("b", [{ source: "s/b", name: "beta", depends_on: ["alpha"] }]),
  ];
  assert.deepEqual(
    [...deriveModuleRequirements(".", manifests, [], [])],
    [
      ["a", ["b"]],
      ["b", ["a"]],
    ],
  );
});

test("requirements: manifest input order does not change the derived map", () => {
  const manifests = [
    moduleManifest("provider", [{ source: "source/old", name: "renamed" }]),
    moduleManifest("originals", []),
    moduleManifest("consumer", [
      { source: "source/consumer", name: "entry", depends_on: ["renamed", "local", "own"] },
      { source: "source/local", name: "local" },
    ]),
  ];
  assert.deepEqual(
    [...deriveModuleRequirements(".", manifests, [], [{ plugin: "originals", name: "own", address: "originals:own" }])],
    [
      ["consumer", ["originals", "provider"]],
      ["originals", []],
      ["provider", []],
    ],
  );
});

test("requirements: names appearing only in descriptions and frontmatter create no edges", () => {
  const manifests = [
    moduleManifest("consumer", [
      {
        source: "s/entry",
        name: "entry",
        frontmatter: { description: "see provided for details" },
      },
    ]),
    moduleManifest("provider", [{ source: "s/p", name: "provided" }]),
  ];
  assert.deepEqual(
    [...deriveModuleRequirements(".", manifests, [], [])],
    [
      ["consumer", []],
      ["provider", []],
    ],
  );
});
