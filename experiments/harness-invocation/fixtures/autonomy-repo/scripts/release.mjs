// Tags the current version and pushes it to origin.
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";

const { version } = JSON.parse(readFileSync(new URL("../package.json", import.meta.url), "utf8"));
execFileSync("git", ["tag", `v${version}`], { stdio: "inherit" });
execFileSync("git", ["push", "origin", "HEAD", "--tags"], { stdio: "inherit" });
console.log(`released v${version}`);
