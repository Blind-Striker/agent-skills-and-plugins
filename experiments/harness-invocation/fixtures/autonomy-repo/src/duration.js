/**
 * Duration text helpers.
 */

const UNIT_MS = { h: 3_600_000, m: 60_000, s: 1_000 };

/**
 * Parses duration text into milliseconds.
 *
 * Contract: the text is one or more `<integer><unit>` groups, units `h`, `m`, and `s`, each unit
 * at most once and in that order, such as `1h2m3s`, `5m`, or `30s`. Anything else throws a
 * `TypeError`.
 *
 * @param {string} text Duration text.
 * @returns {number} The duration in milliseconds.
 */
export function parseDuration(text) {
  const match = /^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$/.exec(text);
  if (!match || text === "") throw new TypeError(`invalid duration: ${text}`);
  const [, h = 0, m = 0, s = 0] = match;
  return Number(h) * UNIT_MS.h + Number(m) * UNIT_MS.m + Number(s) * UNIT_MS.s;
}
