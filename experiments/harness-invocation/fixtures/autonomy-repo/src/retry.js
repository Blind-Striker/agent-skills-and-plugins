/**
 * Retry timing.
 */

/**
 * The delay before a retry attempt.
 *
 * Contract: attempt 1 waits `base` milliseconds, and each later attempt waits `base` longer than
 * the one before it.
 *
 * @param {number} attempt The attempt number, starting at 1.
 * @param {number} base The base delay in milliseconds.
 * @returns {number} The delay in milliseconds.
 */
export function backoff(attempt, base) {
  return base * attempt;
}
