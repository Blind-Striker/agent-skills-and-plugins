# Skill framework landscape

Date: 2026-07-30

What the wider community concluded about the skill frameworks this repo vendors, and what the
evidence says about skill quality in general. This is *input* to curation; the why of any individual
take/skip/modify decision belongs beside the item in `curation/*.yaml`.

Mechanics — which harness lets whom invoke what — are in
[skill-invocation-across-harnesses.md](skill-invocation-across-harnesses.md).

## Where the superpowers debate landed

The methodology is not seriously contested; the weight is. The dominant criticism is "bloated",
not "wrong": paying token overhead for scaffolding on models that already plan competently. A
controlled comparison found superpowers running cheaper with fewer tokens *and* better output on
non-trivial tasks, while **simple tasks cost more** — the design phases are pure overhead when the
task is fully specified.

The recurring recommendation is to stop treating it as a monolith. Reviews converge on brainstorm,
plan and debug as the parts that keep earning their cost, and on dropping blanket workflow
enforcement for an experienced developer with a tuned `CLAUDE.md`. The debugging discipline in
particular is defended at every model tier: reproduce → isolate → hypothesize beats guessing.

A second criticism is rigidity: plans that pre-specify the exact files to edit hurt on exploratory
work, where the right implementation is discovered rather than declared up front.

### What its maintainers say about all this

Two positions in the upstream `CLAUDE.md` bear directly on curation.

On authoring guidance: superpowers **deliberately** diverges from Anthropic's published
best-practices and will not accept PRs that reformat skills to comply "without extensive eval
evidence". So a skill body over the recommended length is a tested choice, not sloppiness — and its
content deserves to be judged on how it steers an agent, not on conformance.

On invocation: they hold that without the SessionStart bootstrap the skills are "dead weight —
present on disk but never invoked", and their acceptance test for a new harness is that
`brainstorming` auto-triggers on "Let's make a react todo list". That claim is overstated as
written — a harness loads every skill's name and description regardless, so a skill with a good
description is reachable on its own. What the bootstrap actually supplies is *propensity and
priority*: a near-zero threshold for invoking, an ordering rule between process and implementation
skills, and a table of pre-refuted excuses for not invoking. The honest reading is that without it
the skills fire less aggressively and less predictably, not never.

The two objectives are simply different. Upstream optimises for *never miss*; a curated set that
puts the trigger in the user's hand optimises for *never surprise*. Both are coherent, and the
frontmatter dial exists precisely to choose. The real cost of choosing the second is that upstream
descriptions were tuned assuming the bootstrap did the routing — strip the pressure out of one and
it must be replaced with an honest trigger sentence, or the skill simply never fires.

## Three philosophies

The frameworks this repo draws from differ in what they try to own:

- **superpowers** — a complete methodology with a staged pipeline; deep on the inner build loop,
  strong on ambiguous problems, heavy on small ones.
- **mattpocock-skills** — requirements-first and composable, explicitly critical of frameworks that
  own the process on the grounds that they "take away your control and make bugs in the process hard
  to resolve". Lighter, and weaker at producing detailed documents. Its interrogation skills
  (`grill-me`, `grill-with-docs`) are its most-used pieces.
- **Addy Osmani's agent-skills** — broad lifecycle coverage rather than depth, monolithic in
  adoption. Not vendored here.

### Fused versus split ceremonies

Comparing the two entry points head to head shows where "too heavy for small tasks" actually comes
from, and it is not verbosity.

`grilling` is six sentences and carries the whole interview discipline: one question at a time, look
facts up yourself but put every *decision* to the human, and do not act until they confirm.
`brainstorming` contains that and adds project-context exploration, decomposition of over-large
requests, two-to-three approaches with a recommendation, sectioned design presentation, spec
writing, a self-review pass and a user review gate.

The difference that matters is that superpowers **fuses** those steps into one mandatory flow whose
terminal state is invoking `writing-plans` — there is no supported way to grill and stop. Pocock
**splits** the same ground into separately invoked steps (`grill-me` → `to-spec` → `to-tickets` →
`implement`), so the human chooses how far to go. The weight complaint is a complaint about fusion,
not about content.

A practical consequence for curation: taking `brainstorming` means owning body edits regardless of
taste. Its hard gate, its "You MUST" phrasing, its hard-coded coupling to `writing-plans`, its
references to skills outside our set, and its spec path under `docs/superpowers/specs/` — a
directory this repo treats as merge-transient scratch — all have to go.

### Where the two overlap, and where they do not

Same job, both frameworks — the pairs any mix-and-match decision has to compare on content:

| Job | superpowers | mattpocock-skills |
|---|---|---|
| Sharpening requirements | `brainstorming` | `grill-me` + `grill-with-docs` → `grilling` |
| Writing a spec | `writing-plans` | `to-spec` |
| Breaking work down | (inside `writing-plans`) | `to-tickets` |
| Implementing | `executing-plans`, `subagent-driven-development` | `implement` |
| Test-first | `test-driven-development` | `tdd` |
| Debugging | `systematic-debugging` | `diagnosing-bugs` |
| Reviewing | `requesting-code-review`, `receiving-code-review` | `code-review` |
| Authoring skills | `writing-skills` | `writing-great-skills` |

Only superpowers has: `using-git-worktrees`, `finishing-a-development-branch`,
`verification-before-completion`, `dispatching-parallel-agents`.

Only mattpocock has: `domain-modeling` and `codebase-design` (the DDD side), `wayfinder` (mapping
work too large for one session), `handoff`, `research`, `prototype`, `teach`.

The standing warning against mixing: two frameworks installed as *routers* fight over command names,
compete on routing logic, and pull in different TDD philosophies. Cherry-picking individual skills is
fine as long as exactly one thing routes. In this repo nothing upstream routes — selection is the
manifest's job and the trigger is the user's.

## What the harness now does itself

Claude Code ships bundled skills that overlap the vendored frameworks directly — `/debug`,
`/code-review`, `/verify` against superpowers' `systematic-debugging`, `requesting-code-review` and
`verification-before-completion`. OpenCode ships no such equivalents.

Two consequences for curation. First, "the harness already does this" is a per-harness observation
and never a global skip reason in a repo that targets both. Second, bundled does not mean better:
whether upstream content steers an agent more effectively than the built-in is a judgement made by
reading both, not by counting features.

## Skill quality: what the evidence says

An audit of 214 community skills found 73% scoring below 60/100. The dominant failure mode, in 68%
of them, was a vague description with no trigger phrases — and the failure is silent, because a bad
`SKILL.md` never throws, it just never gets selected. Adding concrete trigger phrasing moved scores
by 20–35 points in a single edit.

The authoring guidance that follows from this, and which agrees with both Anthropic's official
best-practices and the academic treatment of skills as software artifacts:

- One coherent capability per skill. A diffuse scope produces a diffuse description, which matches
  less well — granularity is a *selection* concern before it is a maintenance one.
- The description states what it does *and* when to use it, in third person, with the key use case
  first.
- Progressive disclosure: metadata always loaded, body on selection, bundled files on demand. Keep
  the body under 500 lines and references one level deep, because nested references get partially
  read.
- Evaluations before documentation — measure the gap without the skill first, so the skill solves an
  observed failure rather than an imagined one.

On volume, one reported figure: roughly 8–12 well-chosen skills are said to cover most of a senior
developer's day, after which every additional skill is context tax paid on every session. It is
cited here as an observation from the sources below, **not** as a budget for this repo — the size of
a module is a curation decision like any other, and no count has been agreed.

## Sources

- [Three Philosophies of AI Coding Workflows](https://dev.to/jamilxt/superpowers-vs-agent-skills-vs-pocock-three-philosophies-of-ai-coding-workflows-e6n)
- [The Honest Tradeoffs of Superpowers](https://www.joanmedia.dev/ai-blog/the-honest-tradeoffs-of-superpowers-token-costs-overkill-and-the-alternatives)
- [Superpowers for Claude Code: Still Worth It in 2026?](https://mcp.directory/blog/superpowers-skill-worth-it-2026)
- [A Rave Review of Superpowers — Hacker News](https://news.ycombinator.com/item?id=47623101)
- [I Audited 214 Claude Code Skills](https://dev.to/thestack_ai/i-audited-214-claude-code-skills-73-were-silently-broken-2m9a)
- [Authoring Agent Skills: A Software-Engineering Approach](https://arxiv.org/html/2607.25032v1)
- [Anthropic — Skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- [Matt Pocock's skills, mapped](https://skillselion.hashnode.dev/matt-pocock-s-skills-mapped-the-flow-he-teaches-every-deprecation-and-what-replaced-what-v1-1)
