/**
 * Duplicate detection.
 */

/**
 * Lists the values that appear more than once.
 *
 * Contract: values are compared with `===`. Each duplicated value appears once in the result,
 * and the result is ordered by where each value appears for the second time.
 *
 * @template T
 * @param {T[]} values Any values.
 * @returns {T[]} The duplicated values.
 */
export function findDuplicates(values) {
  const result = [];
  for (let i = 0; i < values.length; i++) {
    for (let j = 0; j < i; j++) {
      if (values[j] === values[i]) {
        if (!result.includes(values[i])) result.push(values[i]);
        break;
      }
    }
  }
  return result;
}
