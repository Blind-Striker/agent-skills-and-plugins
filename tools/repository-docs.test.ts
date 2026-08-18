import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { test } from "node:test";

const root = join(import.meta.dirname, "..");

test("README build command names every committed generated tree", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  assert.match(readme, /`npm run build`[^\n]*`plugins\/`[^\n]*`opencode\/`[^\n]*`dist\/`/);
});

test("README Release recipe verifies the downloaded digest before npm exec", () => {
  const readme = readFileSync(join(root, "README.md"), "utf8");
  const sectionStart = readme.indexOf("### OpenCode from the private Release package");
  assert.ok(sectionStart >= 0, "README lost the private Release section");
  const nextHeading = readme.indexOf("\n##", sectionStart + 1);
  const section = readme.slice(sectionStart, nextHeading >= 0 ? nextHeading : undefined);
  const fenceOpen = section.indexOf("```powershell");
  assert.ok(fenceOpen >= 0, "recipe lost its PowerShell code block");
  const fenceClose = section.indexOf("```", fenceOpen + 3);
  assert.ok(fenceClose >= 0, "recipe PowerShell code block is unterminated");
  const recipe = section.slice(fenceOpen, fenceClose);

  const download = "gh release download installer-v0.1.0 --repo Blind-Striker/agent-skills-and-plugins";
  const asset = '"deniz-agent-skills-0.1.0.tgz"';
  const digest = "69532caf101f5626ea652cdb7e1046783b3d64fe79613e4d59f21e95eccb9460";
  const expected = "$expected = ";
  const compute = "Get-FileHash -LiteralPath $package -Algorithm SHA256";
  const compare = "if ($actual -ne $expected) { throw";
  const firstExec = "npm exec --yes --package $package -- deniz-skills install --all";

  const at = (needle: string) => recipe.indexOf(needle);
  const downloadAt = at(download);
  const assetAt = at(asset);
  const expectedAt = at(expected);
  const digestAt = at(digest);
  const computeAt = at(compute);
  const compareAt = at(compare);
  const execAt = at(firstExec);

  assert.ok(downloadAt >= 0, "recipe lost the exact gh release download (tag)");
  assert.ok(assetAt >= 0, "recipe lost the exact asset name");
  assert.ok(expectedAt >= 0, "recipe lost the expected-hash assignment");
  assert.ok(digestAt >= 0, "recipe lost the expected release digest");
  assert.ok(computeAt >= 0, "recipe lost the hash computation");
  assert.ok(compareAt >= 0, "recipe lost the mismatch comparison/failure");
  assert.ok(execAt >= 0, "recipe lost the packed npm exec");
  assert.ok(downloadAt < assetAt, "recipe must name the asset with the download");
  assert.ok(expectedAt < digestAt, "recipe must put the digest inside the expected assignment");
  assert.ok(
    downloadAt < expectedAt && expectedAt < computeAt && computeAt < compareAt && compareAt < execAt,
    "recipe must download, set the expected digest, compute, compare, and fail before the first npm exec",
  );
  assert.ok(
    recipe.indexOf("npm exec") === execAt,
    "recipe must contain no npm exec before the verified first npm exec",
  );
});

test("OpenCode lab describes installer composition rather than a mounted build tree", () => {
  const lab = readFileSync(join(root, "experiments", "harness-invocation", "lab.ps1"), "utf8");
  assert.match(lab, /installer composition/i);
  assert.doesNotMatch(lab, /built tree mounted as the global config/i);
});

test("package research lead question describes per-Module Bundles and installer", () => {
  const research = readFileSync(
    join(root, "docs", "research", "2026-08-07-opencode-plugin-package-artifacts.md"),
    "utf8",
  );
  const lead = research.split("## Direct answer", 1)[0] ?? "";
  assert.match(lead, /per-Module Bundles/i);
  assert.match(lead, /installer/i);
  assert.doesNotMatch(lead, /stages its generated `opencode\/` tree/i);
});
