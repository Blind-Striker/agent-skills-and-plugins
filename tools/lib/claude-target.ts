import type { SkillIntent } from "./manifest.ts";

/** The two Claude Code invocation keys, so a stated intent replaces whatever upstream said. */
export const CLAUDE_INVOCATION_KEYS = ["user-invocable", "disable-model-invocation"] as const;

/**
 * Claude Code's half of ADR-0005: one artifact, and the dial is frontmatter. An item that states no
 * intent is left alone entirely — silence is not a default, so adopting the field item by item
 * costs nothing on the items that have not adopted it.
 */
export function claudeInvocation(invocation: SkillIntent["invocation"]): Record<string, unknown> {
  switch (invocation) {
    case "auto":
      return { "user-invocable": false };
    case "manual":
      return { "disable-model-invocation": true };
    default:
      return {}; // `both` sets neither; absent sets nothing at all
  }
}
