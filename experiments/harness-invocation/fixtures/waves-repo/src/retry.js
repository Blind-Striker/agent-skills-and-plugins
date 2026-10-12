/**
 * Retries an async operation with exponential backoff.
 *
 * Callers recieve the operation's first result, or its last error when every
 * attempt fails.
 */

/**
 * Computes the wait after a failed attempt.
 *
 * @param {number} attempt The failed attempt's number, starting at 1.
 * @param {number} baseMs The wait after the first failed attempt, in milliseconds.
 * @returns {number} `baseMs * 2 ** (attempt - 1)`.
 */
export function backoff(attempt, baseMs) {
  return baseMs * 2 ** (attempt - 1);
}

/**
 * Calls `fn` until it resolves, at most 3 times in all.
 *
 * @template T
 * @param {() => Promise<T>} fn The operation.
 * @param {{ baseMs?: number, sleep?: (ms: number) => Promise<void> }} [options]
 *   `baseMs` defaults to 100. `sleep` defaults to a timer; tests replace it.
 * @returns {Promise<T>} The first result. Rejects with the last error when all 3 attempts fail.
 */
export async function retry(fn, options = {}) {
  const baseMs = options.baseMs ?? 100;
  const sleep = options.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
  let lastError;
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    try {
      return await fn();
    } catch (error) {
      lastError = error;
      if (attempt < 3) {
        await sleep(backoff(attempt, baseMs));
      }
    }
  }
  throw lastError;
}
