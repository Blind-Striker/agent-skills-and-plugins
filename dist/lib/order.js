/** Locale-independent UTF-16 code-unit ordering, matching JavaScript's relational string comparison. */
export function ordinalCompare(left, right) {
  return left < right ? -1 : left > right ? 1 : 0;
}
