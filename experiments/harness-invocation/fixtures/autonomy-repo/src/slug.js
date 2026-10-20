/**
 * Slug helpers.
 */

/**
 * Turns text into a URL slug.
 *
 * Contract: the slug is lowercase; every run of characters other than `a-z` and `0-9` becomes a
 * single `-`; and the slug never starts or ends with `-`.
 *
 * @param {string} text Any text.
 * @returns {string} The slug.
 */
export function slugify(text) {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "-")
    .replace(/^-+|-+$/g, "");
}
