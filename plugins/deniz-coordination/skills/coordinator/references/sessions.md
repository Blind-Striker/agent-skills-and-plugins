# Separate sessions

Read before you brief a lane that runs in a session of its own.

## Briefs and gates

- The user opens and names the session. You write its brief to a file outside the repository and
  send the session a short kickoff that points to the file.
- A brief names: the lane's role and host, the repositories and the working folders it may use,
  what to read first, an ordered queue, the files it owns and the files it must not touch, how it
  takes a shared gate, and its report format with a line cap. A lane runs the repository's canonical
  gate scripts as they are.
- A lane works through its queue without asking per item. It asks you only when its queue is empty,
  when it reaches a reserved action, or when it meets a contradiction. You hold three gates: spec
  approval for a change to behaviour, a public surface or a canon rule; merge, after you read the diff
  and its evidence, and after the merge commit's own CI ran, before you report it done or build on it
  (a green change is not a green main branch); and every reserved action.
- You are the one session that talks to the user. Lanes take their decisions from you, within any
  approval the user delegated to you. A reserved action (a push to a shared branch,
  a release, a machine change, a public action such as filing issues) runs only in the session the
  user told directly. A relayed approval does not cover it, and a peer session's go is not the
  user's go.

## Shared resources

- Files that several lanes change (changelogs, generated baselines, registries, build lock files)
  get an order from you: name which change lands first, and the later one rebases once on its merge.
- A heavy job (a full gate, a benchmark, a mutant run, a workspace preparation) takes the machine's lock and
  announces its start and end to you. A lock is a handle the job holds (an open file handle on
  Windows, `flock` on POSIX), never a file whose existence is the lock. Nobody frees a lock by
  deleting it. A job that waits on something else releases the lock while it waits.

## Messages and records

- A message names the task, the commit, the working folder, the files, the evidence, and the
  decision it asks for. "Green" in a message has the meaning `delegating-to-worker-models` gives it.
- A lane's stop names the rule it hit and its evidence. Answer a stop with a decision, never with
  "try again".
- Keep these files outside the repository, current at every decision rather than at the end: the
  status file (what is in flight, who holds it, each lane's next step); the decisions file (the
  user's words quoted, with the date and the scope); and one chapter per lane, written by that lane
  (its method, guards, traps and where it stands). A message is not a record.
- Keep two running lists beside them, fed by the lanes in one line each: findings (each one ends as
  an issue, a note, or a decline with its reason) and tool or skill friction, which goes to the
  skill's owner at a natural boundary.

## Pause, compaction and handoff

- Before a planned pause: every lane updates its chapter; nothing is running or dispatched and no
  lock is held; every branch is pushed or deleted and every worktree is cleaned or has a reason to
  stay; you write the status snapshot. After the pause, each lane reads its chapter, checks its live
  state against git and the forge, and reports before it acts; you re-brief each lane in one message.
- Before your own context is compacted, write the handoff and say so on screen. After it, read the
  handoff and the status file, then re-brief every lane.
- A handoff starts with the user's request word for word and its acceptance, lists the decisions
  made with their sources and the decisions not made, and ends with the next step.
- Before a long pause or a change of lead, test the handoff: a fresh session that has read only the
  handoff files answers the questions the user would ask, and you compare its answers with yours.
  Fix every miss in the files.
