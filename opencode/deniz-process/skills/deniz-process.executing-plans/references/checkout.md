# The Checkout

Part of deniz-process.executing-plans. Read it at skill start, before the
workspace is created.

Respect an approved current checkout, including main/master, and do not ask again merely because
this skill loaded. Use deniz-process.using-git-worktrees only when isolation is requested or a concrete
conflict requires it; otherwise retain the current workspace. A new checkout choice still needs
approval, and this setup grants no permission to discard existing work.

The default branch (main/master) is approved only by your human partner: an earlier answer in this
work or a standing preference in their instructions (AGENTS.md, CLAUDE.md, or similar). Without one,
ask once before the first commit there, in the same message as any other question you have at
start, and keep the answer for the rest of the plan.
