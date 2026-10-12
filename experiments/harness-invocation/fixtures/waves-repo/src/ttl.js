/**
 * Expiry for cache entries.
 */

/**
 * Computes when an entry expires.
 *
 * @param {number} now The current time, in milliseconds.
 * @param {number} ttlMs How long the entry lives, in milliseconds.
 * @returns {number} `now + ttlMs`.
 */
export function expiryTime(now, ttlMs) {
  return now + ttlMs;
}

/**
 * Tells whether an entry has expired.
 *
 * Contract: an entry is expired at `expiresAt` itself and at every later
 * instant. It is live only before `expiresAt`.
 *
 * @param {number} now The current time, in milliseconds.
 * @param {number} expiresAt The entry's expiry time, in milliseconds.
 * @returns {boolean} true when the entry has expired.
 */
export function isExpired(now, expiresAt) {
  return now > expiresAt;
}
