/**
 * The Agent Skills limit on a skill's `description`, which every harness shares. It applies to the
 * text each tree emits, not to the neutral source: rendering a namespaced fact adds its plugin
 * prefix, so a description that fits upstream can overflow once localized, by a different amount in
 * each harness. No emitter shortens a description to fit; an overflow is a curation decision, so the
 * build and `validate` both fail with the item and its length (transformation-and-emission.md).
 */
export const MAX_SKILL_DESCRIPTION_LENGTH = 1024;

/** The finding for one emitted `SKILL.md`, named by its repository path, or undefined when it fits. */
export function descriptionLengthProblem(path: string, description: string): string | undefined {
  return description.length > MAX_SKILL_DESCRIPTION_LENGTH
    ? `${path}: description is ${description.length} characters, over the ${MAX_SKILL_DESCRIPTION_LENGTH}-character skill limit`
    : undefined;
}
