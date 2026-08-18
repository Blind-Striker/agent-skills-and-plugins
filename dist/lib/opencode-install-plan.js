function compareStrings(left, right) {
  return left.localeCompare(right);
}
function sortedKeys(record) {
  return Object.keys(record).sort(compareStrings);
}
function uniqueSorted(names) {
  return [...new Set(names)].sort(compareStrings);
}
function finding(code, message, extra = {}) {
  const result = { code, message };
  if (extra.module !== undefined) {
    result.module = extra.module;
  }
  if (extra.path !== undefined) {
    result.path = extra.path;
  }
  return result;
}
function recordedIdentity(owned) {
  return { sha256: owned.sha256, mode: owned.mode };
}
function bytesMatch(left, right) {
  return left.sha256 === right.sha256;
}
function identityMatches(left, right, platform) {
  return bytesMatch(left, right) && (platform === "windows" || left.mode === right.mode);
}
function observedAt(observed, path) {
  return Object.hasOwn(observed, path) ? observed[path] : undefined;
}
function compareFindings(left, right) {
  const code = compareStrings(left.code, right.code);
  if (code !== 0) {
    return code;
  }
  const path = compareStrings(left.path ?? "", right.path ?? "");
  if (path !== 0) {
    return path;
  }
  return compareStrings(left.module ?? "", right.module ?? "");
}
function compareOperations(left, right) {
  const path = compareStrings(left.path, right.path);
  if (path !== 0) {
    return path;
  }
  return compareStrings(left.kind, right.kind);
}
function compareTransfers(left, right) {
  const path = compareStrings(left.path, right.path);
  if (path !== 0) {
    return path;
  }
  const from = compareStrings(left.fromModule, right.fromModule);
  if (from !== 0) {
    return from;
  }
  return compareStrings(left.toModule, right.toModule);
}
function resolveRequested(current, manifests, request, findings) {
  if (request.kind === "update") {
    const selected = uniqueSorted(Object.keys(current.modules));
    if (selected.length === 0) {
      findings.push(finding("unknown_module", "update requires a non-empty Selection"));
      return [];
    }
    const known = [];
    for (const name of selected) {
      if (!Object.hasOwn(manifests, name)) {
        findings.push(
          finding(
            "unknown_module",
            `${name} is selected but absent from this package; remove it explicitly, then retry`,
            { module: name },
          ),
        );
      } else {
        known.push(name);
      }
    }
    return known;
  }
  if (request.all) {
    return uniqueSorted(Object.keys(request.kind === "install" ? manifests : current.modules));
  }
  const named = uniqueSorted(request.modules);
  if (named.length === 0) {
    findings.push(finding("unknown_module", `${request.kind} requires explicit Modules or all`));
    return [];
  }
  const known = [];
  for (const name of named) {
    if (request.kind === "install" && !Object.hasOwn(manifests, name)) {
      findings.push(finding("unknown_module", `${name} is absent from this package`, { module: name }));
      continue;
    }
    if (request.kind === "remove" && !Object.hasOwn(current.modules, name)) {
      findings.push(finding("unknown_module", `${name} is not a selected Module`, { module: name }));
      continue;
    }
    known.push(name);
  }
  return known;
}
function nextSelection(current, request, requested) {
  const selected = new Set(Object.keys(current.modules));
  if (request.kind === "install") {
    for (const name of requested) {
      selected.add(name);
    }
  } else if (request.kind === "remove") {
    for (const name of requested) {
      selected.delete(name);
    }
  }
  return [...selected].sort(compareStrings);
}
function selectionChanges(current, selection) {
  const before = new Set(Object.keys(current.modules));
  const after = new Set(selection);
  return {
    added: selection.filter((name) => !before.has(name)),
    removed: uniqueSorted([...before].filter((name) => !after.has(name))),
  };
}
function claim(final, findings, path, module, identity) {
  const existing = final.get(path);
  if (existing && existing.module !== module) {
    findings.push(
      finding(
        "ownership_collision",
        `${path} cannot be owned by both ${existing.module} and ${module}; reconcile them together`,
        { module, path },
      ),
    );
    return;
  }
  if (!existing) {
    final.set(path, { module, identity });
  }
}
function buildFinalOwnership(current, manifests, request, affected, selection, findings) {
  const final = new Map();
  for (const name of selection) {
    if (affected.has(name) && request.kind !== "remove") {
      const moduleManifest = manifests[name];
      if (!moduleManifest) {
        continue;
      }
      for (const path of sortedKeys(moduleManifest.files)) {
        const identity = moduleManifest.files[path];
        if (identity) {
          claim(final, findings, path, name, identity);
        }
      }
      continue;
    }
    if (!affected.has(name)) {
      for (const path of sortedKeys(current.files)) {
        const owned = current.files[path];
        if (owned?.module === name) {
          claim(final, findings, path, name, recordedIdentity(owned));
        }
      }
    }
  }
  const transfers = [];
  const paths = uniqueSorted([...Object.keys(current.files), ...final.keys()]);
  for (const path of paths) {
    const old = current.files[path];
    const next = final.get(path);
    if (!old || !next || old.module === next.module) {
      continue;
    }
    if (affected.has(old.module) && affected.has(next.module)) {
      transfers.push({ path, fromModule: old.module, toModule: next.module });
    }
  }
  return { final, transfers };
}
function planPath(path, old, next, observed, request, affected, operations, findings) {
  if (observed.kind === "directory" || observed.kind === "link") {
    const owner = next?.module ?? old?.module;
    findings.push(
      finding(
        "type_mismatch",
        `${path} must be an ordinary file; replace the ${observed.kind} by hand, then retry`,
        owner === undefined ? { path } : { module: owner, path },
      ),
    );
    return;
  }
  if (old && observed.kind === "file" && !identityMatches(observed.identity, old, request.platform)) {
    findings.push(
      finding("local_modification", `${path} was modified locally; restore, move, or delete it by hand, then retry`, {
        module: old.module,
        path,
      }),
    );
    return;
  }
  if (old && observed.kind === "absent") {
    if (request.kind === "remove" && affected.has(old.module)) {
      operations.push({ kind: "drop-missing-claim", path, module: old.module });
    } else {
      findings.push(
        finding(
          "state_drift",
          `${path} is missing from the Destination; restore it or clean stale Install state, then retry`,
          { module: old.module, path },
        ),
      );
    }
    return;
  }
  if (!old && next) {
    if (observed.kind === "file") {
      findings.push(
        finding("unowned_collision", `${path} already exists and is unowned; delete or move it by hand, then retry`, {
          module: next.module,
          path,
        }),
      );
      return;
    }
    operations.push({
      kind: "add",
      path,
      module: next.module,
      source: path,
      identity: next.identity,
    });
    return;
  }
  if (old && !next) {
    operations.push({
      kind: "remove",
      path,
      module: old.module,
      identity: recordedIdentity(old),
    });
    return;
  }
  if (!old || !next || identityMatches(old, next.identity, request.platform)) {
    return;
  }
  if (request.platform === "posix" && bytesMatch(old, next.identity) && old.mode !== next.identity.mode) {
    operations.push({
      kind: "chmod",
      path,
      module: next.module,
      from: old.mode,
      to: next.identity.mode,
    });
    return;
  }
  operations.push({
    kind: "replace",
    path,
    module: next.module,
    source: path,
    identity: next.identity,
  });
}
function buildNextState(current, manifests, request, selection, affected, final) {
  const modules = Object.create(null);
  for (const name of selection) {
    if (affected.has(name) && request.kind !== "remove") {
      const moduleManifest = manifests[name];
      if (moduleManifest) {
        modules[name] = { version: moduleManifest.version, digest: moduleManifest.digest };
      }
      continue;
    }
    const existing = current.modules[name];
    if (existing) {
      modules[name] = { version: existing.version, digest: existing.digest };
    }
  }
  const files = Object.create(null);
  for (const path of [...final.keys()].sort(compareStrings)) {
    const entry = final.get(path);
    if (entry) {
      files[path] = { module: entry.module, sha256: entry.identity.sha256, mode: entry.identity.mode };
    }
  }
  return { schemaVersion: 1, modules, files };
}
export function planReconcile(current, manifests, observed, request) {
  const findings = [];
  const requested = resolveRequested(current, manifests, request, findings);
  const affected = new Set(requested);
  const selection = nextSelection(current, request, requested);
  const changes = selectionChanges(current, selection);
  const { final, transfers } = buildFinalOwnership(current, manifests, request, affected, selection, findings);
  const operations = [];
  for (const path of uniqueSorted([...Object.keys(current.files), ...final.keys()])) {
    const old = current.files[path];
    const next = final.get(path);
    const oldAffected = old !== undefined && affected.has(old.module);
    const nextAffected = next !== undefined && affected.has(next.module);
    if (!oldAffected && !nextAffected) {
      continue;
    }
    const snapshot = observedAt(observed, path);
    if (snapshot === undefined) {
      const owner = next?.module ?? old?.module;
      findings.push(
        finding(
          "missing_observation",
          `${path} has no Destination observation; observe it explicitly, including absent`,
          owner === undefined ? { path } : { module: owner, path },
        ),
      );
      continue;
    }
    planPath(path, old, next, snapshot, request, affected, operations, findings);
  }
  findings.sort(compareFindings);
  operations.sort(compareOperations);
  transfers.sort(compareTransfers);
  if (findings.length > 0) {
    return { request, selectionChanges: changes, operations: [], transfers: [], nextState: current, findings };
  }
  return {
    request,
    selectionChanges: changes,
    operations,
    transfers,
    nextState: buildNextState(current, manifests, request, selection, affected, final),
    findings,
  };
}
