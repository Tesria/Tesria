/**
 * What a Storage Targets card says at a glance, and the color it says it in,
 * as plain logic.
 *
 * A card can stand for two repositories: the cloud holds the database
 * (pgBackRest) and the files (restic), written by different services, and
 * either can be the broken one. The card used to read only the files row, so
 * a cloud database copy that could not be reached, or had never had a
 * backup, was shown Healthy (T8-007, T8-008). The card now says the worst of
 * its rows.
 */

export type TargetTone = 'ok' | 'warn' | 'bad'

export interface TargetRow {
  slot: string
  kind: string
  problem?: string | null
  present?: boolean | null
  walBacklogFiles?: number | null
  lastBackupAt?: string | null
  lastDrillOk?: boolean | null
  message?: string | null
}

const RANK: Record<TargetTone, number> = { ok: 0, warn: 1, bad: 2 }

/** One repository's state. */
export function rowState(row: TargetRow): { tone: TargetTone; text: string } {
  // First, because it outranks everything else here: a copy that exists,
  // passes its own integrity check and will not turn back into a database
  // is the failure all of this is meant to prevent.
  if (row.lastDrillOk === false) return { tone: 'bad', text: 'The last restore drill failed' }
  if (row.problem) return { tone: 'bad', text: row.problem }
  if (row.present === false) {
    // For a drive this is the ordinary state, not a fault; for a share it is
    // a fault, which is why only the NAS raises an alert elsewhere.
    return row.slot === 'removable'
      ? { tone: 'warn', text: 'Not plugged in' }
      : { tone: 'bad', text: 'Not reachable' }
  }
  if (row.walBacklogFiles != null && row.walBacklogFiles >= 3)
    return { tone: 'bad', text: `${row.walBacklogFiles} WAL segments waiting` }
  // A message is the service's word on what it could not do: a copy that
  // failed, a repository it could not open. The removable drive's messages
  // are notes instead ("safe to remove"), so they do not count.
  if (row.message && row.slot !== 'removable') return { tone: 'bad', text: 'Needs attention' }
  if (!row.lastBackupAt) return { tone: 'warn', text: 'Nothing copied yet' }
  return { tone: 'ok', text: 'Healthy' }
}

/** The card's state: the worst of its rows, the first of them on a tie. */
export function targetState(rows: TargetRow[]): { tone: TargetTone; text: string } {
  let worst: { tone: TargetTone; text: string } | null = null
  for (const row of rows) {
    const s = rowState(row)
    if (!worst || RANK[s.tone] > RANK[worst.tone]) worst = s
  }
  return worst ?? { tone: 'warn', text: 'Nothing copied yet' }
}
