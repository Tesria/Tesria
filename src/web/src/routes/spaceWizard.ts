/**
 * The pure half of the New Space wizard (dev-plan 21.2), tested in
 * spaceWizard.test.ts without rendering anything.
 */

/**
 * The key a name suggests: its letters and digits, from the first letter
 * (a key starts with one), at most six, upper-cased. "Team handbook" gives
 * TEAMHA; "2026 plans" gives PLANS.
 */
export function suggestKey(name: string): string {
  return name.replace(/[^a-zA-Z0-9]/g, '').replace(/^[0-9]+/, '').slice(0, 6).toUpperCase()
}

/**
 * What is wrong with a key, in words, or null when the server will take it:
 * 2 to 50 letters and digits, starting with a letter (SpaceEndpoints'
 * KeyPattern). Said before Continue rather than at the last step.
 */
export function keyProblem(key: string): string | null {
  if (key.length === 0) return 'Choose a key.'
  if (!/^[A-Za-z0-9]+$/.test(key)) return 'A key has only letters and digits, with no spaces.'
  if (!/^[A-Za-z]/.test(key)) return 'A key starts with a letter.'
  if (key.length < 2) return 'A key has at least 2 characters.'
  if (key.length > 50) return 'A key has at most 50 characters.'
  return null
}

/** Who may see the space: everyone signed in at a level, or only its groups. */
export type Audience = { kind: 'everyone'; level: 0 | 1 | 2 } | { kind: 'groups' }

/** What the create call sends as everyoneAccess: the level, or null for nobody else. */
export function everyoneAccessOf(audience: Audience): number | null {
  return audience.kind === 'everyone' ? audience.level : null
}

/** The level's words, as the wizard's select and summary say it. */
export const LEVEL_WORDS: Record<0 | 1 | 2, string> = { 0: 'Can view', 1: 'Can edit', 2: 'Can administer' }

/** One plain sentence for what a choice means. */
export function audienceSentence(audience: Audience): string {
  if (audience.kind === 'groups') {
    return 'Only the people you put in its groups can open it. Nobody else sees that it exists.'
  }
  switch (audience.level) {
    case 0:
      return 'Everyone with an account can read it. Only the people in its groups can change it.'
    case 1:
      return 'Everyone with an account can read and edit it. Only its Admins can manage it.'
    default:
      return 'Everyone with an account can read, edit and manage it, but only its Admins can choose who else is an admin.'
  }
}

/**
 * The people who end up in each group, for the summary and the request:
 * nobody twice in a group, and the creator always in Admins (the server
 * puts them there whatever is sent).
 */
export function groupMembers(chosen: Record<number, string[]>, creatorId: string): { role: number; userIds: string[] }[] {
  return [2, 1, 0, 3].map((role) => ({
    role,
    userIds: [...new Set((chosen[role] ?? []).filter((id) => !(role === 2 && id === creatorId)))],
  }))
}
