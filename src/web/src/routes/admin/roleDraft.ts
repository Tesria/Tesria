/** The Roles grid's unsaved state, as pure functions (dev-plan 11.1; T7-024). */

type RoleRights = { id: string; permissions: string[] }

/** Draft state: role id to the set of keys it would hold after Save. */
export type Draft = Record<string, Set<string>>

export function toDraft(roles: RoleRights[]): Draft {
  return Object.fromEntries(roles.map((r) => [r.id, new Set(r.permissions)]))
}

/** What saving `draft` would change about `role`, as the grid loaded it. */
export function changes(role: RoleRights, draft: Set<string>): { added: string[]; removed: string[] } {
  const held = new Set(role.permissions)
  return {
    added: [...draft].filter((k) => !held.has(k)).sort(),
    removed: [...held].filter((k) => !draft.has(k)).sort(),
  }
}

/**
 * Someone else saved while this grid was open (T7-024). Carries this
 * person's own ticks and clears, measured against the grid as it was
 * loaded, onto the roles as they are now: their change stays, and the
 * review box then describes exactly what saving would do. A role that no
 * longer exists is dropped; a new one starts as it is.
 */
export function rebase(loaded: RoleRights[], draft: Draft, fresh: RoleRights[]): Draft {
  const before = new Map(loaded.map((r) => [r.id, r]))
  return Object.fromEntries(fresh.map((role) => {
    const next = new Set(role.permissions)
    const was = before.get(role.id)
    const mine = draft[role.id]
    if (was && mine) {
      const { added, removed } = changes(was, mine)
      for (const k of added) next.add(k)
      for (const k of removed) next.delete(k)
    }
    return [role.id, next]
  }))
}
