---
name: implement-spec
description: Build a spec's tickets in parallel on one integration branch.
disable-model-invocation: true
---

You have been provided a spec. This spec should have tickets associated with it, describing how to implement the spec.

The issue tracker should have been provided to you. If not, tell the user to run `/deniz-process:setup-matt-pocock-skills`.

The goal is the entire spec implemented on a single **integration branch**, with every ticket resolved the way the issue tracker closes work.

The tickets are not a list of steps. They are a **task graph** with blocking relationships between them. This means there is always a **frontier** of tickets which are ready to be grabbed.

Communication to and from subagents should be sparse. Communicate primarily through **context pointers**: to the spec, tickets, research notes, and previous commits. Don't duplicate information already available via pointers.

**Implementer subagents** should be run in the background where possible for maximum concurrency.

## Steps

1. Read the spec and tickets to understand the task graph.

2. (optional) Use an **exploration subagent** to conduct any exploration required by the tickets - relevant codebase files or external documentation. Ensure the exploration subagent can save files - it should save its markdown notes in a directory outside the repo, accessible by all future subagents. This lets **implementer subagents** focus on implementation rather than exploration.

3. Create the integration branch. If the issue tracker closes work through PRs, or the user asks for one, open a draft PR after the first merge in step 5 (a branch with no commits ahead of main can't open one), marked as closing the spec and tickets.

4. Use **implementer subagents** to implement each ticket, each in its own worktree on its own branch. Use the harness's own per-subagent worktree where it has one; otherwise create each with `git worktree add <path> -b <ticket-branch> <integration-branch>`. Each implementer subagent:
   - confirms its worktree is based on the integration branch before starting, and resets onto it if not;
   - calls the Skill tool with "deniz-process:test-driven-development" to build the ticket at the seams the spec or ticket names, and reports back instead of picking a seam when the choice matters and none is named;
   - merges the integration branch tip into its own branch before reporting done

   A worktree holds only what git tracks, so run a ticket whose tests need untracked fixtures, local databases, or credentials in the main checkout instead.

5. Once an **implementer subagent** completes, merge its work to the integration branch with a **merger subagent**.

6. If this changes the **frontier** of available tickets, kick off more **implementer subagents** to work on the new tickets. This allows for maximum concurrency. Compute the frontier from the tickets merged into the integration branch, not from the tracker, whose blocked-by count drops only when a blocker closes. When two frontier tickets touch one shared file, add a blocking edge so they run one after the other, or have the exploration notes fix the exact names each one adds.

7. Once all tickets are complete, call the Skill tool with "deniz-process:requesting-code-review" on the integration branch. Fix all issues raised by the code review in a single **implementer subagent**. Then re-check only the fixed findings and run the project's whole test suite; start no second broad review.

8. If a draft PR exists, mark it ready for review. Otherwise, resolve each ticket the way the issue tracker closes work, and report the integration branch.

9. Clean up all **implementer subagent** worktrees.
