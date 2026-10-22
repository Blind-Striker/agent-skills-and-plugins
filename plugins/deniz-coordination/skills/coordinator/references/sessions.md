# Separate sessions

Read before you brief a lane that runs in a session of its own.

- The user opens and names the session. You write its brief to a file outside the repository and
  send the session a short kickoff that points to the file.
- A brief names: the lane's role and host, the repositories and the working folders it may use,
  what to read first, an ordered queue, the files it owns and the files it must not touch, how it
  takes a shared gate (one heavy run at a time, announced before and after), and its report format
  with a line cap.
- You are the one session that talks to the user. Lanes take their decisions from you, within any
  approval the user delegated to you. An action the repository reserves (a push to a shared branch,
  a release, a machine change, a public or bulk action) runs only in the session the user told
  directly; a relayed approval does not cover it.
- A message names the task, the commit, the working folder, the files, the evidence, and the
  decision it asks for. Keep the status file current beside the messages; a message is not a record.
- Before your own context is compacted, write the handoff and say so on screen. After it, read the
  handoff and the status file, then re-brief every lane.
- A handoff starts with the user's request word for word and its acceptance, lists the decisions
  made with their sources and the decisions not made, and ends with the next step.
