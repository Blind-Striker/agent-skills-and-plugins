/**
 * Fixed time windows.
 */

/**
 * Tests whether a timestamp falls inside a window.
 *
 * Contract: the window is half-open, [start, start + lengthMs). It includes
 * `start` and excludes `start + lengthMs`, so two adjacent windows never share
 * an instant.
 *
 * @param {number} ts The timestamp, in milliseconds.
 * @param {number} start The window's first instant, in milliseconds.
 * @param {number} lengthMs The window's length, in milliseconds.
 * @returns {boolean} true when `ts` is inside the window.
 */
export function inWindow(ts, start, lengthMs) {
  return ts >= start && ts <= start + lengthMs;
}

/**
 * Finds the start of the window that holds a timestamp.
 *
 * @param {number} ts The timestamp, in milliseconds.
 * @param {number} lengthMs The window length, in milliseconds. Windows start at 0.
 * @returns {number} The largest multiple of `lengthMs` that is not after `ts`.
 */
export function windowStart(ts, lengthMs) {
  return Math.floor(ts / lengthMs) * lengthMs;
}
