const hits = {};
export function allow(key, limit, windowSeconds) {
  const now = Date.now();
  if (hits[key] == undefined) hits[key] = [];
  hits[key] = hits[key].filter(t => now - t < windowSeconds * 1000);
  if (hits[key].length >= limit) { return false; }
  hits[key].push(now);
  return true;
}
export function reset() { for (const k in hits) { delete hits[k]; } }
