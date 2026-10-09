---
record_id: opencode2-bulk-apply-windows-2026-10-09
date: 2026-10-09
repo_head: 20fc59d1a8c234e2520c4cf16b79813c26aa87a6
kind: runtime-stress
summary: On the Windows workstation an isolated OpenCode 2.0.23 server crashed (Bun 1.4.2 segmentation fault in the native watcher, exit 3) during a full-estate bulk Apply in 4 of 33 sessions, all 4 with the lab on the system volume (4 of 18 there, 0 of 15 on a data volume); the Release gate stays closed.
isolation_ok: true
harness_name: opencode
harness_version: 2.0.23
measured_checkout: aa582a52914ff22739a0cd465fcbc5a35dc3974d
runner_revision: uncommitted lab scripts over measured_checkout (method below); installer node tools/install-opencode.ts at measured_checkout
upstream_issue: https://github.com/anomalyco/opencode/issues/47505
---

# Windows bulk Apply against a running OpenCode 2 server

This tier-2 record covers plan Task 17 for upstream issue
[`anomalyco/opencode#47505`](https://github.com/anomalyco/opencode/issues/47505), which reports that a
bulk update of global skills terminated the shared OpenCode 2 service on Windows.

**Result: the server is not safe during a bulk Apply on Windows.** An isolated OpenCode 2.0.23
server died during the Apply in 4 of 33 sessions. Each time it exited with code 3 after a Bun 1.4.2
segmentation fault in the native watcher addon. The first run's three planned repetitions all
survived, but they ran only on a data volume. An independent verifier then ran 29 more sessions and
reproduced the crash 4 times, every time with the lab on the system volume, which also holds the
real user profile. Plan Task 17 Step 4 applies: the termination is recorded here, the Release gate
stays closed, and the choice of mitigation returns to the curator.

`repo_head` is the HEAD before this record was committed. Every session measured the installer and
Bundles of `measured_checkout`; the commit between the two changed only documentation. No model or
LLM call was made.

## Environment

- Windows workstation: Windows 11 Pro 10.0.26300, PowerShell 7.6.6, Node v24.13.0, npm 11.20.0.
- OpenCode 2.0.23 (`/api/info` and the server log); the crash banner names Bun v1.4.2.
  `opencode.exe` was started directly, not through the `.cmd` shim, so its exit code is its own.
- The estate is the four checkout Bundles: 115 skills (Akka 5, Aspire 8, General 65, Process 37)
  and 2 agents. With OpenCode's 2 built-in skills (`opencode`, `report`), `/api/skill` must list 117
  after install and 2 after remove.
- Two NTFS volumes held labs: the **system** volume, which also holds the real user profile and the
  OpenCode binary, and a separate **data** volume.

## Method

Each session used a fresh lab outside the repository and the user profile and a fresh server:

1. Build the lab with `Get-OpenCodeLabEnvironment` and `Initialize-OpenCodeLab` from
   [`common.ps1`](../common.ps1), add `OPENCODE_DISABLE_MODELS_FETCH=1`, remove the lab's cleared
   variables, and set a random server password. `<lab>/config/service.json` is `{"disabled":true}`.
2. Start the server with cwd `<lab>/project`: either `opencode serve --hostname 127.0.0.1 --port
   <free> --print-logs` or, in service-mode sessions, `opencode serve --service --port <free>` with
   the password seeded in the lab's `service.json`.
3. Poll `GET /api/skill?location[directory]=<lab>/project` every second for the whole session,
   recording HTTP status, skill count, latency, and whether the server process is alive. Wait until
   the list is exactly the 2 built-ins.
4. Run, from the repository root with the lab environment (`OPENCODE_CONFIG_DIR=<lab>/config`):
   `node tools/install-opencode.ts install --all --yes`, then `remove --all --yes`, then
   `install --all --yes`. After each Apply, poll every 200 ms until the exact ID set equals the
   expected set and stays unchanged for 3 s (timeout 120 s). Stress sessions run the Applies back
   to back with no wait.
5. Check that every skill path is `/builtin/` or below the lab, stop the server in `finally`,
   record whether it had exited by itself and with which code, save its stdout, stderr, and
   `<lab>/xdg/data/opencode/log/opencode.log`, and delete the lab.

The first run (sessions `F`, plus one pilot) used one set of scripts. The verifier wrote its own
scripts independently and varied the lab volume, the sampling load during an Apply (100 ms extra
sampling on or off), the server mode, and the sequence. The scripts are not committed; this section
and the table below are the recipe (tier 2, not tier 3).

## Results per session

| Session | Lab volume | Server | Polling | Sequence | Server alive after Apply | Server outcome | Apply s | Max s to consistent list | Watcher stops |
|---|---|---|---|---|---:|---|---|---:|---:|
| F rep1 | data | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 2.22-2.73 | 0.550 | 20 |
| F rep2 | data | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 2.23-3.21 | 0.437 | 30 |
| F rep3 | data | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 2.17-2.77 | 0.592 | 20 |
| F pilot | data | `serve` | 1 s | install, remove, install | 3 of 3 | alive (about 4.3 min, 260 poll rows, all HTTP 200) | 2.12-2.89 | not counted | - |
| sys-1 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 4.66-5.55 | 0.006 | 50 |
| sys-2 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 0 of 1 | **crashed** (exit 3) in step 1 (install) | 6.57 | - | 14 |
| sys-3 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 5.08-7.00 | 0.004 | 56 |
| sys-4 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 4.78-5.35 | 0.003 | 48 |
| sys-5 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 4.29-5.21 | 0.003 | 50 |
| sys-6 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 4.21-5.00 | 0.003 | 44 |
| sys-7 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 4.23-5.32 | 0.003 | 48 |
| sys-8 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 5.17-6.96 | 0.004 | 56 |
| sys-9 | system | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 4.20-5.01 | 0.003 | 46 |
| sys-slow-1 | system | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 4.42-5.20 | 0.006 | 50 |
| sys-slow-2 | system | `serve` | 1 s | install, remove, install | 0 of 1 | **crashed** (exit 3) in step 1 (install) | 4.95 | - | 0 |
| sys-slow-3 | system | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 4.29-5.05 | 0.003 | 46 |
| sys-slow-4 | system | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 3.97-4.78 | 0.003 | 44 |
| sys-slow-5 | system | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 3.97-4.64 | 0.002 | 44 |
| sys-slow-6 | system | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 3.98-4.84 | 0.003 | 44 |
| sys-slow-7 | system | `serve` | 1 s | install, remove, install | 3 of 3 | alive | 4.17-4.97 | 0.004 | 44 |
| sys-slow-8 | system | `serve` | 1 s | install, remove, install | 1 of 2 | **crashed** (exit 3) in step 2 (remove) | 4.09-4.86 | 0.003 | 28 |
| data-1 | data | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.14-2.70 | 0.409 | 20 |
| data-2 | data | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.18-2.70 | 0.372 | 20 |
| data-3 | data | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.15-2.68 | 0.003 | 20 |
| data-4 | data | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.17-2.65 | 0.003 | 20 |
| data-5 | data | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.08-2.59 | 0.003 | 20 |
| data-6 | data | `serve` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.08-2.65 | 0.003 | 20 |
| sys-stress | system | `serve` | 1 s + 100 ms in Apply | back-to-back (3) | 2 of 3 | **crashed** (exit 3) in step 3 (install) | 4.58-5.78 | - | 32 |
| data-svc-stress | data | `serve --service` | 1 s + 100 ms in Apply | back-to-back (7) | 7 of 7 | alive | 2.17-2.97 | 0.328 | 52 |
| data-svc-1 | data | `serve --service` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.28-2.85 | 0.347 | 18 |
| data-svc-2 | data | `serve --service` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.17-2.67 | 0.003 | 20 |
| data-svc-3 | data | `serve --service` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.24-2.78 | 0.156 | 20 |
| data-svc-4 | data | `serve --service` | 1 s + 100 ms in Apply | install, remove, install | 3 of 3 | alive | 2.12-2.71 | 0.393 | 20 |

`F` sessions are the first run; the others are the verifier's. `sys-slow` sessions turned the
verifier's extra 100 ms sampling off, so their load matched the first run. The pilot's expected set
was wrong (a script bug computed 3 IDs instead of 117), so its timing is not counted; it still
shows liveness. "Max s to consistent list" is measured from the end of the Apply; the verifier also
sampled during the Apply, so its list was often complete when the Apply ended. Back-to-back sessions
check consistency only at the end.

## Totals

| Lab volume | Sessions | Sessions where the server crashed | Applies started | Applies during which it crashed |
|---|---:|---:|---:|---:|
| system | 18 | 4 | 49 | 4 |
| data | 15 | 0 | 49 | 0 |
| all | 33 | 4 | 98 | 4 |

- Crashes happened in install steps (3) and a remove step (1): as the first Apply of a session
  (2), later in a normal sequence (1), and in a back-to-back sequence (1). Two of the 8 `sys-slow`
  sessions crashed, so the crash does not need the verifier's extra sampling load.
- The server never crashed on the data volume, including 5 service-mode sessions and a 7-Apply
  back-to-back run. A one-sided Fisher exact test of the volume split gives p of about 0.13 over
  the verifier's 29 sessions and about 0.07 over all 33. The volume is therefore a plausible factor,
  not an established cause. Applies on the system volume took 4.0-7.0 s against 2.1-3.2 s on the
  data volume, and produced 44-56 watcher stops per completed three-Apply session against 18-30;
  more watcher stop and restart cycles give more chances to hit the fault.
- In every crash the installer still exited 0 and finished its Apply. Only the server died, and the
  list then never converged because nothing answered.

When the server survived (29 sessions):

- Every Apply exited 0 (the first run also saw empty installer stderr), and every poll returned
  HTTP 200.
- After each checked Apply the exact ID set matched the expectation: 117 after install, 2 after
  remove.
  The worst time from the end of an Apply to a consistent list was 0.592 s; none came near the
  120 s timeout.
- During an Apply, `/api/skill` returned partial lists (in the first run, for example 10, 36, 81, and
  102 skills during an install, and 5, 43, 77, and 80 during a remove), because the watcher rescans about every 0.5 s while files
  change. This is expected, and it is why a running server can see a partly applied tree.
- In the final list of every surviving verifier session, the hidden (`opencode/autoinvoke: false`)
  set was exactly the 26 Bundle `manual` skills. Each installed skill path was `<lab>/config/skills/<id>/SKILL.md`, and
  no sample, including the samples taken during an Apply, listed a skill outside the lab.
- In the verifier's sequential sessions, the 2 Bundle agents were listed as subagents after each
  install and were absent after each remove. The surviving back-to-back session checked them only
  after its last install, where both were listed as subagents.
- The server's own log held only INFO lines, except one WARN per service-mode session (see
  caveats).

## Crash excerpt (sanitized)

Session `sys-2`, first install, about 3.5 s into the Apply. The tail of the server's stdout log is
followed by its stderr. `<lab>` replaces the lab path, timestamps and run IDs are removed, and the
crash-report link is shortened.

```text
level=INFO message="watcher stopped" path="<lab>\\config\\skills" type=directory
level=INFO message="watcher subscribe" path="<lab>\\config\\skill" type=file ignores=0
level=INFO message="watcher started" path="<lab>\\config\\skill" type=file backend=node ignores=0
level=INFO message="watcher subscribe" path="<lab>\\config\\skills" type=directory ignores=0
level=INFO message="watcher started" path="<lab>\\config\\skills" type=directory backend=windows ignores=0
level=INFO message="skills rescanned" file="<lab>\\config\\skills\\deniz-process.receiving-code-review" skills=[...]
level=INFO message="watcher stopped" path="<lab>\\config\\skill" type=file
level=INFO message="watcher stopped" path="<lab>\\config\\skills" type=directory
============================================================
Bun v1.4.2 (744846f84) Windows x64
Windows v10.26300
Args: "...\@opencode\cli\bin\opencode.exe" ... "serve" "--hostname" "127.0.0.1" "--port" "<port>" "--print-logs"
Elapsed: 8161ms | User: 3765ms | Sys: 2671ms
RSS: 0.33 GB | Peak: 0.37 GB | Commit: 0.70 GB

panic(thread <n>): Segmentation fault at address 0x88
oh no: Bun has crashed. This indicates a bug in Bun, not your code.

 https://bun.report/1.4.2/wa1744846f... (frames in .bun-3579391051-3cba9b8dc4fa4e45.node, ntdll.dll, KERNELBASE.dll, KERNEL32.DLL)
```

The other three crashes have the same shape: the last log lines are `watcher stopped` for
`config\skill` and `config\skills` within a `skills rescanned` cycle, then a segmentation fault at
another address (for example `0x17BA22B0020` in `sys-slow-8`, during remove). Every crash report's
stack lies in the same native `.bun-*.node` addon, which is the file watcher.

## Method gap in the first run

The first run concluded that the crash did not reproduce, partly from "0 WARN, ERROR, or exception
lines" in `opencode.log`. That evidence could not show a crash. In all 4 crashes, `opencode.log` held
no WARN or ERROR line and no panic, and it lost about the last 300 ms of lines before the crash. Only
process liveness, the exit code, and the server's stderr show the crash. Future runs must check
those, not the log file alone.

## Isolation

- Every lab server and installer process had `HOME`, `USERPROFILE`, `OPENCODE_TEST_HOME`, the four
  XDG roots, `OPENCODE_CONFIG_DIR`, `OPENCODE_DB`, and `TEMP` below its own lab, with
  `OPENCODE_DISABLE_PROJECT_CONFIG=1` and `OPENCODE_DISABLE_AUTOUPDATE=1`, and the lab's cleared
  variables removed. Each server used its own free 127.0.0.1 port and random password, and the
  lab's `service.json` disabled the managed service.
- The real `~/.config/opencode` (384 files) and `~/.local/state/opencode` (198 files) had the same
  per-file fingerprint before and after; `~/.agents` was absent throughout.
- The real managed `opencode serve --service` kept the same process and creation time and was never
  contacted. No lab process remained; every lab was deleted; the repository stayed clean.
- No skill path outside the lab appeared in any sample, and every watcher path in the lab logs was
  below `<lab>/config` or the lab's home folder. No lab path appears in the real OpenCode log.
- Changes seen in real state came from other writers, not from the labs: `~/.claude/skills` changed
  only in Claude's own skill-sync files (`synced/<account>/manifest.json` and
  `.last-complete-round`), and the real `opencode.db` and `~/.bun/install/cache` changed at moments
  when the real service log shows other sessions working. Every lab used its own database.

## Caveats

- **Isolation gap.** `Get-OpenCodeLabEnvironment` does not redirect `APPDATA` or `LOCALAPPDATA`.
  No lab wrote below them (no OpenCode, crash-dump, or error-reporting change), but the variables
  were inherited from the real profile.
- **Service mode only on the data volume.** At startup a service-mode server tries to hard-link the
  running `opencode.exe` into `<lab>/xdg/cache`. Across volumes that fails with `EXDEV` and logs the
  one WARN line `could not link the running binary`. On the system volume the link would succeed and
  change the real binary's link count, so service mode was deliberately not run there. The real
  managed service, whose profile is on the system volume, is therefore not directly measured.
- **Scope.** One location (the lab project) per server, OpenCode 2.0.23 only, Windows only. Bulk
  Apply on the Linux host was not measured.
- **Captured stdout timestamps.** Server stdout and stderr captured through PowerShell events carry
  delivery timestamps that can lag; the timeline comes from the server's own log lines.

## Consequence

- Plan Task 17 Step 4 applies, and the plan's stop condition "the Windows bulk-Apply measurement
  terminates the service" is met. The Release gate (plan Task 19 Step 1) stays closed until the
  curator chooses a mitigation and it is documented. The options are to stop OpenCode before Apply
  on Windows in the documented procedure, or to wait for an upstream fix.
- Plan Task 18 Step 3 must stop OpenCode, including the managed service, before Apply on the
  Windows workstation.
