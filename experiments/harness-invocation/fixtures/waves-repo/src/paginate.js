/**
 * Splits lists into pages.
 */

/**
 * Returns one page of a list.
 *
 * @template T
 * @param {T[]} items The whole list.
 * @param {number} page The page number, starting at 1.
 * @param {number} size Items per page, at least 1.
 * @returns {T[]} The items on that page, or an empty array past the last page.
 */
export function paginate(items, page, size) {
  const start = (page - 1) * size;
  return items.slice(start, start + size);
}

/**
 * Counts the pages that `total` items fill.
 *
 * @param {number} total The number of items.
 * @param {number} size Items per page, at least 1.
 * @returns {number} The page count, which is 0 when `total` is 0.
 */
export function pageCount(total, size) {
  return Math.ceil(total / size);
}
