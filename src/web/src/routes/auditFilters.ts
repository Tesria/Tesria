import type { AuditEntry } from '../api/client'

/**
 * The Audit tab's filters (T7-019): the action picker's groups, the date
 * range as the server wants it, and folding a burst of failed sign-ins into
 * one row. Pure, so the edge cases are tested (auditFilters.test.ts).
 */

/** One prefix of the action picker: `user.` picks every `user.*` action. */
export type AuditActionPrefix = { prefix: string; actions: string[] }
export type AuditActionGroup = { label: string; prefixes: AuditActionPrefix[] }

/**
 * Every action Tesria records, grouped by what it is about. A prefix option
 * also catches actions added after this list was written, so an entry never
 * becomes unreachable by filter. Keep in step with the server's
 * `audit.Record(...)` calls.
 */
export const AUDIT_ACTION_GROUPS: AuditActionGroup[] = [
  {
    label: 'Accounts and sign-in',
    prefixes: [
      {
        prefix: 'user.',
        actions: [
          'user.login', 'user.login_failed', 'user.reauthenticated', 'user.registered', 'user.role_changed',
          'user.status_changed', 'user.unlocked', 'user.email_changed', 'user.password_changed',
          'user.password_recovered', 'user.recovery_email_requested', 'user.recovery_failed', 'user.reset_issued',
          'user.recovery_codes_regenerated', 'user.recovery_codes_acknowledged', 'user.totp_enabled',
          'user.totp_disabled', 'user.session_revoked', 'user.sessions_revoked', 'user.other_sessions_revoked',
          'user.tokens_revoked',
        ],
      },
      { prefix: 'owner.', actions: ['owner.assigned', 'owner.transferred'] },
      { prefix: 'invite.', actions: ['invite.created', 'invite.revoked'] },
    ],
  },
  {
    label: 'Roles, groups and permissions',
    prefixes: [
      { prefix: 'role.', actions: ['role.created', 'role.updated', 'role.deleted'] },
      {
        prefix: 'group.',
        actions: ['group.created', 'group.updated', 'group.deleted', 'group.member_added', 'group.member_removed', 'group.renamed_for_builtin'],
      },
      { prefix: 'permissions.', actions: ['permissions.changed'] },
    ],
  },
  {
    label: 'Spaces and pages',
    prefixes: [
      {
        prefix: 'space.',
        actions: [
          'space.created', 'space.deleted', 'space.archived', 'space.unarchived', 'space.permission_granted',
          'space.permission_revoked', 'space.published', 'space.unpublished', 'space.public_settings_changed',
          'space.access_recovered', 'space.opened', 'space.exported', 'space.exports_changed', 'space.imported',
        ],
      },
      {
        prefix: 'page.',
        actions: [
          'page.created', 'page.updated', 'page.moved', 'page.copied', 'page.draft_discarded', 'page.trashed',
          'page.restored', 'page.purged', 'page.restricted', 'page.unrestricted',
        ],
      },
    ],
  },
  {
    label: 'Security',
    prefixes: [
      {
        prefix: 'security.',
        actions: ['security.alert_acknowledged', 'security.alert_resolved', 'security.network_blocked', 'security.network_unblocked'],
      },
      { prefix: 'token.', actions: ['token.revoked_by_admin'] },
      { prefix: 'audit.', actions: ['audit.chain_verified'] },
    ],
  },
  {
    label: 'Backups',
    prefixes: [
      {
        prefix: 'backup.',
        actions: [
          'backup.requested', 'backup.copy_requested', 'backup.policy_changed', 'backup.key_saved',
          'backup.target_test_requested', 'backup.restore_test_requested', 'backup.restore_requested',
          'backup.restored', 'backup.restore_cancel_requested', 'backup.restore_cancelled',
          'backup.restore_abandoned', 'backup.restore_undo_requested', 'backup.restore_kept_discarded',
        ],
      },
    ],
  },
  {
    label: 'Settings and the instance',
    prefixes: [
      { prefix: 'settings.', actions: ['settings.updated'] },
      {
        prefix: 'branding.',
        actions: ['branding.changed', 'branding.reset', 'branding.logo_uploaded', 'branding.logo_removed', 'branding.favicon_uploaded', 'branding.favicon_removed'],
      },
      { prefix: 'mail.', actions: ['mail.signin_connected', 'mail.signin_disconnected'] },
      { prefix: 'email.', actions: ['email.failed'] },
      { prefix: 'instance.', actions: ['instance.upgraded'] },
      { prefix: 'setup.', actions: ['setup.completed'] },
      { prefix: 'dependencies.', actions: ['dependencies.checked'] },
    ],
  },
]

/** `YYYY-MM-DD` from a date field, as a local day; null when it is not one. */
function localDay(value: string): Date | null {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!m) return null
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return Number.isNaN(d.getTime()) ? null : d
}

/**
 * The server's `from` (inclusive) and `to` (exclusive) for two date fields
 * read as whole days in the viewer's time zone: from the start of the first
 * day to the start of the day after the last, so "to September 28" includes
 * all of the 28th. Either may be empty.
 */
export function dayRange(from: string, to: string): { from?: string; to?: string } {
  const start = localDay(from)
  const end = localDay(to)
  const range: { from?: string; to?: string } = {}
  if (start) range.from = start.toISOString()
  if (end) range.to = new Date(end.getFullYear(), end.getMonth(), end.getDate() + 1).toISOString()
  return range
}

/** A row of the list: one entry, or a run of failed sign-ins folded into one. */
export type AuditRow =
  | { kind: 'entry'; entry: AuditEntry }
  | { kind: 'run'; entries: AuditEntry[] }

/** The action whose bursts are folded, and how many in a row it takes. */
export const FOLDED_ACTION = 'user.login_failed'
export const FOLD_AT = 3

/**
 * Folds each run of at least `FOLD_AT` consecutive failed sign-ins into one
 * row, so a burst of them does not push everything else off the screen. The
 * entries keep their order; nothing is dropped.
 */
export function foldRuns(entries: AuditEntry[]): AuditRow[] {
  const rows: AuditRow[] = []
  let run: AuditEntry[] = []
  const flush = () => {
    if (run.length >= FOLD_AT) rows.push({ kind: 'run', entries: run })
    else for (const entry of run) rows.push({ kind: 'entry', entry })
    run = []
  }
  for (const entry of entries) {
    if (entry.action === FOLDED_ACTION) {
      run.push(entry)
      continue
    }
    flush()
    rows.push({ kind: 'entry', entry })
  }
  flush()
  return rows
}
