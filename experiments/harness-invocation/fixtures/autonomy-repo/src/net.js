/**
 * Network address helpers.
 */

/**
 * Validates a host name.
 *
 * @param {string} text A host name such as `example.com` or `localhost`.
 * @returns {string} The host name, lowercased.
 * @throws {TypeError} When the text is not a host name.
 */
export function parseHost(text) {
  if (!/^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*$/i.test(text)) {
    throw new TypeError(`invalid host: ${text}`);
  }
  return text.toLowerCase();
}

/**
 * Parses a TCP port.
 *
 * @param {string} text The port as text, such as `8080`.
 * @returns {number} The port.
 */
export function parsePort(text) {
  return Number(text);
}
