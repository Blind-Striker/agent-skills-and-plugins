/**
 * User records.
 */

/**
 * Finds a user by id.
 *
 * @param {{ id: number, name: string, disabled?: boolean }[]} users All users.
 * @param {number} id The id to find.
 * @returns {{ id: number, name: string, disabled?: boolean } | undefined} The user, if any.
 */
export function getUsr(users, id) {
  return users.find((user) => user.id === id);
}
