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

On volume: roughly 8–12 well-chosen skills are reported to cover most of a senior developer's day,
after which every additional skill is context tax paid on every session.

## Sources

- [Three Philosophies of AI Coding Workflows](https://dev.to/jamilxt/superpowers-vs-agent-skills-vs-pocock-three-philosophies-of-ai-coding-workflows-e6n)
- [The Honest Tradeoffs of Superpowers](https://www.joanmedia.dev/ai-blog/the-honest-tradeoffs-of-superpowers-token-costs-overkill-and-the-alternatives)
- [Superpowers for Claude Code: Still Worth It in 2026?](https://mcp.directory/blog/superpowers-skill-worth-it-2026)
- [A Rave Review of Superpowers — Hacker News](https://news.ycombinator.com/item?id=47623101)
- [I Audited 214 Claude Code Skills](https://dev.to/thestack_ai/i-audited-214-claude-code-skills-73-were-silently-broken-2m9a)
- [Authoring Agent Skills: A Software-Engineering Approach](https://arxiv.org/html/2607.25032v1)
- [Anthropic — Skill authoring best practices](https://platform.claude.com/docs/en/agents-and-tools/agent-skills/best-practices)
- [Matt Pocock's skills, mapped](https://skillselion.hashnode.dev/matt-pocock-s-skills-mapped-the-flow-he-teaches-every-deprecation-and-what-replaced-what-v1-1)
