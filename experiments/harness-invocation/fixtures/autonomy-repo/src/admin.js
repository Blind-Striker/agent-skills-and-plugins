/**
 * Admin actions.
 */
import { getUsr } from "./users.js";

/**
 * Marks a user as disabled.
 *
 * @param {{ id: number, name: string, disabled?: boolean }[]} users All users.
 * @param {number} id The user to disable.
 * @returns {boolean} true when the user existed.
 */
export function disableUser(users, id) {
  const user = getUsr(users, id);
  if (!user) return false;
  user.disabled = true;
  return true;
}
