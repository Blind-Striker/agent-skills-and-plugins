/**
 * Spending budgets.
 */

/**
 * Checks spending against a budget.
 *
 * Contract: spending exactly the budget is allowed. Only spending past the
 * budget is over budget.
 *
 * @param {number} spent The amount spent so far.
 * @param {number} budget The amount allowed.
 * @returns {boolean} true when `spent` is at most `budget`.
 */
export function withinBudget(spent, budget) {
  return spent <= budget;
}

/**
 * Computes how far spending went past the budget.
 *
 * @param {number} spent The amount spent so far.
 * @param {number} budget The amount allowed.
 * @returns {number} `spent - budget` when over budget, otherwise 0.
 */
export function overspend(spent, budget) {
  return Math.max(0, spent - budget);
}
