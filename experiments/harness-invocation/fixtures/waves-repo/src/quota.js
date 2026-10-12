/**
 * Request quotas for one period.
 */

/**
 * Decides whether one more request fits in the quota.
 *
 * Contract: a request is allowed while `used` is below `limit`. Once `used`
 * reaches `limit`, the quota is spent and every further request is refused.
 *
 * @param {number} used Requests already made in this period.
 * @param {number} limit Requests allowed per period.
 * @returns {boolean} true when one more request is allowed.
 */
export function allow(used, limit) {
  return used <= limit;
}

/**
 * Counts the requests still allowed in this period.
 *
 * @param {number} used Requests already made in this period.
 * @param {number} limit Requests allowed per period.
 * @returns {number} `limit - used`, and never less than 0.
 */
export function remaining(used, limit) {
  return Math.max(0, limit - used);
}
