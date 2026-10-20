/**
 * Report rendering.
 */
import { getUsr } from "./users.js";

/**
 * Renders one user as a report line.
 *
 * @param {{ id: number, name: string }[]} users All users.
 * @param {number} id The user to render.
 * @returns {string} `#<id> <name>`, or `#<id> (unknown)` when there is no such user.
 */
export function formatUserLine(users, id) {
  const user = getUsr(users, id);
  return user ? `#${user.id} ${user.name}` : `#${id} (unknown)`;
}

/**
 * Renders rows as tab-separated text, one line per row.
 *
 * Contract: a field that contains a tab, a double quote, or a newline is wrapped in double quotes,
 * and each double quote inside it is doubled. Other fields are written as they are. Rows are
 * joined with `\n`.
 *
 * @param {string[][]} rows The rows.
 * @returns {string} The TSV text.
 */
export function toTsv(rows) {
  return rows
    .map((row) =>
      row
        .map((field) => (/[\t"\n]/.test(field) ? `"${field.replace(/"/g, '""')}"` : field))
        .join("\t"),
    )
    .join("\n");
}
