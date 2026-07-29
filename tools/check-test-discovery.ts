import { globSync } from "node:fs";

// `npm test` passes a glob to node --test; if a shell or a Node version expanded or dropped it
// differently, the suite would report a happy "0 failures" while running nothing. Assert the glob
// still finds the test files before the runner gets a chance to be quietly empty.
const MINIMUM = 5;
const found = globSync("tools/**/*.test.ts");
if (found.length < MINIMUM) {
  console.error(`Test discovery broken: tools/**/*.test.ts matched ${found.length} file(s), expected >= ${MINIMUM}.`);
  process.exit(1);
}
