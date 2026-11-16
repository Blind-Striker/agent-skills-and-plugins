worker-model grok v2

This block is for review tasks.

## Review

- Scope: new lock, race, or cancellation-lifetime code only; the task names the files and every
  conditional-compilation or target branch (for example a modern branch and a downlevel branch), and the
  review walks each one.
- Walk every interleaving with named threads, and name the schedule that breaks an invariant.
- When you conclude that something "can't happen", give the proof as a schedule-by-schedule argument. The
  coordinator checks it; it is not final.
- Rate each finding's severity, with the input that shows it.
