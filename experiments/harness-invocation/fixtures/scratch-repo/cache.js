const store = {};
export function put(k, v, ttlSeconds) {
  store[k] = { v: v, exp: Date.now() + ttlSeconds * 1000 };
}
export function get(k) {
  const e = store[k];
  if (e == undefined) return undefined;
  if (Date.now() > e.exp) { delete store[k]; return undefined; }
  return e.v;
}
export function stats() {
  let n = 0;
  for (const k in store) { n = n + 1; }
  return { size: n, keys: Object.keys(store) };
}
