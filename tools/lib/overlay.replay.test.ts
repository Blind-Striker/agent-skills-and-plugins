import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { test } from "node:test";
import { copyUpstream, cutPatch, cutResidue, replayPatch } from "./overlay.ts";

const chunk = (file: string, lines: string[]) =>
  [`diff --git a/${file} b/${file}`, `--- a/${file}`, `+++ b/${file}`, "@@ -1,2 +1,2 @@", ...lines, ""].join("\n");

test("cutResidue names an added marker line and an added .rej file", () => {
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+<<<<<<< ours"])), ["SKILL.md: conflict marker"]);
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+||||||| base"])), ["SKILL.md: conflict marker"]);
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" keep", "+>>>>>>>"])), ["SKILL.md: conflict marker"]);
  const rej = [
    "diff --git a/SKILL.md.rej b/SKILL.md.rej",
    "new file mode 100644",
    "--- /dev/null",
    "+++ b/SKILL.md.rej",
    "@@ -0,0 +1 @@",
    "+x",
    "",
  ].join("\n");
  assert.deepEqual(cutResidue(rej), ["SKILL.md.rej: reject file"]);
});

test("cutResidue ignores markers in context, removed lines, and setext underlines", () => {
  assert.deepEqual(cutResidue(chunk("SKILL.md", [" <<<<<<< HEAD", "->>>>>>> theirs", "+=======", "+Title"])), []);
});

const lines = (n: number) => Array.from({ length: n }, (_, i) => `line ${i + 1}`);
const git = (cwd: string, ...a: string[]) =>
  execFileSync("git", ["-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "core.autocrlf=false", ...a], {
    cwd,
    stdio: "ignore",
  });

function put(dir: string, files: Record<string, string>): void {
  for (const [f, c] of Object.entries(files)) {
    mkdirSync(dirname(join(dir, f)), { recursive: true });
    writeFileSync(join(dir, f), c);
  }
}

/** An upstream item committed as the old pin, the old patch cut from an edited copy, then upstream moved. */
function scenario(opts: { commit: boolean; edit: (f: string[]) => void; move: (up: string) => void; link?: boolean }) {
  const up = mkdtempSync(join(tmpdir(), "up-"));
  put(up, { "SKILL.md": `${lines(30).join("\n")}\n`, "notes.md": "n1\nn2\n" });
  if (opts.commit) {
    git(up, "init", "-q");
    git(up, "add", "-A");
    git(up, "commit", "-qm", "pin");
  }
  const edited = mkdtempSync(join(tmpdir(), "ed-"));
  cpSync(up, edited, { recursive: true, filter: copyUpstream(up) });
  const skill = lines(30);
  opts.edit(skill);
  put(edited, { "SKILL.md": `${skill.join("\n")}\n`, "notes.md": "n1\nN2 ours\n" });
  const patch = cutPatch(up, edited);
  opts.move(up);
  const real = mkdtempSync(join(tmpdir(), "rp-"));
  // A junction on Windows, a directory symlink elsewhere: the path the caller writes is not git's.
  const parent = opts.link ? join(mkdtempSync(join(tmpdir(), "ln-")), "j") : real;
  if (opts.link) {
    symlinkSync(real, parent, "junction");
  }
  const work = join(parent, "w");
  cpSync(up, work, { recursive: true, filter: copyUpstream(up) });
  return { work, results: replayPatch(work, up, patch) };
}
const at = (up: string, n: number, text: string) => {
  const f = join(up, "SKILL.md");
  const l = readFileSync(f, "utf8").split("\n");
  l[n - 1] = text;
  writeFileSync(f, l.join("\n"));
};

test("replay: far change applies, near change merges, same line conflicts", () => {
  const edit = (f: string[]) => {
    f[14] = "line 15 ours";
  };
  const far = scenario({ commit: true, edit, move: (up) => at(up, 1, "line 1 upstream") });
  assert.deepEqual(
    far.results.map((r) => `${r.file}:${r.status}`),
    ["SKILL.md:applied", "notes.md:applied"],
  );
  const near = scenario({ commit: true, edit, move: (up) => at(up, 10, "line 10 upstream") });
  assert.equal(near.results[0]?.status, "merged");
  assert.match(readFileSync(join(near.work, "SKILL.md"), "utf8"), /line 10 upstream[\s\S]*line 15 ours/);
  const same = scenario({ commit: true, edit, move: (up) => at(up, 15, "line 15 upstream") });
  assert.deepEqual(same.results[0], { file: "SKILL.md", status: "conflict", regions: 1 });
  assert.equal(same.results[1]?.status, "applied", "one file's conflict never blocks another");
  assert.match(readFileSync(join(same.work, "SKILL.md"), "utf8"), /^<<<<<<< ours$[\s\S]*^\|\|\|\|\|\|\| base$/m);
  assert.ok(!existsSync(join(same.work, ".git")), "the seed repository is removed");
});

test("replay: no base blob rejects; a deleted target is gone with its chunk in .rej", () => {
  const edit = (f: string[]) => {
    f[14] = "line 15 ours";
  };
  const nobase = scenario({ commit: false, edit, move: (up) => at(up, 10, "line 10 upstream") });
  assert.equal(nobase.results[0]?.status, "rejected");
  assert.match(nobase.results[0]?.detail ?? "", /^1 of 1 hunks/);
  assert.ok(existsSync(join(nobase.work, "SKILL.md.rej")));
  const gone = scenario({ commit: true, edit, move: (up) => rmSync(join(up, "notes.md")) });
  assert.deepEqual(
    gone.results.map((r) => r.status),
    ["applied", "gone"],
  );
  assert.match(readFileSync(join(gone.work, "notes.md.rej"), "utf8"), /^\+N2 ours$/m);
});

test("replay: an ignore rule in the item never hides a file from the seed index", () => {
  const r = scenario({
    commit: true,
    edit: (f) => {
      f[14] = "line 15 ours";
    },
    move: (up) => {
      at(up, 10, "line 10 upstream");
      put(up, { ".gitignore": "SKILL.md\n" });
    },
  });
  assert.equal(r.results[0]?.status, "merged");
});

test("replay never writes through an inherited GIT_INDEX_FILE or GIT_DIR", () => {
  const decoy = mkdtempSync(join(tmpdir(), "decoy-"));
  const saved = { GIT_INDEX_FILE: process.env.GIT_INDEX_FILE, GIT_DIR: process.env.GIT_DIR };
  process.env.GIT_INDEX_FILE = join(decoy, "index");
  process.env.GIT_DIR = join(decoy, "repo.git");
  try {
    scenario({
      commit: false,
      edit: (f) => {
        f[14] = "x";
      },
      move: () => {},
    });
  } finally {
    for (const [k, v] of Object.entries(saved)) {
      if (v === undefined) {
        delete process.env[k];
      } else {
        process.env[k] = v;
      }
    }
  }
  assert.ok(!existsSync(join(decoy, "index")) && !existsSync(join(decoy, "repo.git")));
});

test("replay through a junction or symlinked path still lands the patch", () => {
  const r = scenario({
    commit: true,
    edit: (f) => {
      f[14] = "line 15 ours";
    },
    move: (up) => at(up, 1, "line 1 upstream"),
    link: true,
  });
  assert.deepEqual(
    r.results.map((x) => `${x.file}:${x.status}`),
    ["SKILL.md:applied", "notes.md:applied"],
  );
  assert.match(readFileSync(join(r.work, "SKILL.md"), "utf8"), /^line 15 ours$/m);
});

test("a host apply.whitespace=fix never rewrites the lines a replay lands", () => {
  const cfg = join(mkdtempSync(join(tmpdir(), "cfg-")), "gitconfig");
  writeFileSync(cfg, "[apply]\n\twhitespace = fix\n");
  const saved = process.env.GIT_CONFIG_GLOBAL;
  process.env.GIT_CONFIG_GLOBAL = cfg;
  try {
    const edit = (f: string[]) => {
      f[14] = "line 15 ours   ";
    };
    const far = scenario({ commit: true, edit, move: (up) => at(up, 1, "line 1 upstream") });
    assert.equal(far.results[0]?.status, "applied");
    assert.match(readFileSync(join(far.work, "SKILL.md"), "utf8"), /^line 15 ours {3}$/m);
    const near = scenario({ commit: true, edit, move: (up) => at(up, 10, "line 10 upstream") });
    assert.equal(near.results[0]?.status, "merged");
    assert.match(readFileSync(join(near.work, "SKILL.md"), "utf8"), /^line 15 ours {3}$/m);
  } finally {
    if (saved === undefined) {
      delete process.env.GIT_CONFIG_GLOBAL;
    } else {
      process.env.GIT_CONFIG_GLOBAL = saved;
    }
  }
});
