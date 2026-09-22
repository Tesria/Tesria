import { useState } from 'react'
import { api, ApiError, type BackupTarget } from '../../api/client'

/**
 * Administration → Backups → Storage targets (dev-plan 9.2 step 5).
 *
 * One card per configured slot, built entirely from what the backup sidecars
 * published. The app holds no credential for any of this: keys and
 * passphrases arrive as fingerprints, which is enough to tell two apart and
 * useless for recovering either.
 *
 * The card's job is to answer one question honestly, which is whether there
 * is a copy of this instance somewhere else and when it was last checked.
 * That is why an absent drive still shows what it last held rather than
 * going blank, and why a slot that cannot stand in for a schedule says so.
 */

const SLOT_TITLE: Record<string, string> = {
  cloud: 'Cloud',
  nas: 'Network drive',
  removable: 'Removable drive',
}

const KIND_LABEL: Record<string, string> = {
  database: 'Database',
  files: 'Uploads and dumps',
}

function bytes(n: number | null | undefined): string {
  if (n == null) return 'Not reported'
  if (n === 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(n) / Math.log(1024)), units.length - 1)
  return `${(n / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

function relative(iso: string | null | undefined): string {
  if (!iso) return 'Never'
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 90) return 'just now'
  const steps: [number, string][] = [[60, 'minute'], [60, 'hour'], [24, 'day'], [7, 'week']]
  let value = seconds, unit = 'second'
  for (const [size, name] of steps) {
    if (value < size) break
    value = Math.round(value / size)
    unit = name
  }
  return `${value} ${unit}${value === 1 ? '' : 's'} ago`
}

/** What the card says at a glance, and the colour it says it in. */
function state(target: BackupTarget): { tone: 'ok' | 'warn' | 'bad'; text: string } {
  if (target.problem) return { tone: 'bad', text: target.problem }
  if (target.present === false) {
    // For a drive this is the ordinary state, not a fault; for a share it is
    // a fault, which is why only the NAS raises an alert elsewhere.
    return target.slot === 'removable'
      ? { tone: 'warn', text: 'Not plugged in' }
      : { tone: 'bad', text: 'Not reachable' }
  }
  if (target.walBacklogFiles != null && target.walBacklogFiles >= 3)
    return { tone: 'bad', text: `${target.walBacklogFiles} WAL segments waiting` }
  if (!target.lastBackupAt) return { tone: 'warn', text: 'Nothing copied yet' }
  return { tone: 'ok', text: 'Healthy' }
}

function Dot({ tone }: { tone: 'ok' | 'warn' | 'bad' }) {
  return <span className={`backup-dot backup-dot--${tone}`} aria-hidden="true" />
}

function TargetCard({
  slot, rows, canRun, onCopied,
}: {
  slot: string
  rows: BackupTarget[]
  canRun: boolean
  onCopied: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const files = rows.find((r) => r.kind === 'files')
  const database = rows.find((r) => r.kind === 'database')
  const primary = files ?? database
  if (!primary) return null
  const tone = state(primary)

  async function copyNow() {
    setBusy(true)
    setError(null)
    try {
      await api.admin.backups.copyToTarget(slot)
      onCopied()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The copy could not be started.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="backup-card">
      <h3 className="backup-card__title">{SLOT_TITLE[slot] ?? slot}</h3>
      <p className="backup-card__state"><Dot tone={tone.tone} /> {tone.text}</p>

      <dl className="backup-card__facts">
        <dt>Where</dt>
        <dd>
          {primary.bucket
            ? <>{primary.location} · {primary.bucket}{primary.prefix}</>
            : primary.location ?? 'Not reported'}
        </dd>
        {rows.map((row) => (
          <RepositoryLine key={row.kind} row={row} />
        ))}
        <dt>Encryption</dt>
        <dd>
          {primary.passphraseFingerprint
            ? <>On · passphrase <code>{primary.passphraseFingerprint}</code></>
            : 'Not reported'}
        </dd>
        {primary.keyFingerprint && (
          <>
            <dt>Storage key</dt>
            <dd><code>{primary.keyFingerprint}</code></dd>
          </>
        )}
      </dl>

      {primary.message && <p className="backup-card__pending small">{primary.message}</p>}
      {error && <p className="alert alert--error small">{error}</p>}

      {slot === 'removable' && (
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          disabled={busy || !canRun || primary.present === false}
          onClick={copyNow}
        >
          {busy ? 'Starting…' : 'Copy now'}
        </button>
      )}
      <p className="muted small">Checked {relative(primary.updatedAt)}</p>
    </section>
  )
}

/** One repository's line inside a card: a slot can hold two. */
function RepositoryLine({ row }: { row: BackupTarget }) {
  return (
    <>
      <dt>{KIND_LABEL[row.kind] ?? row.kind}</dt>
      <dd>
        {row.lastBackupAt ? relative(row.lastBackupAt) : 'Nothing copied yet'}
        {row.bytesStored != null && <> · {bytes(row.bytesStored)}</>}
        {row.lastVerifyAt
          ? <> · verified {relative(row.lastVerifyAt)}</>
          : <> · <span className="backup-text--bad">not verified</span></>}
      </dd>
    </>
  )
}

export function StorageTargets({
  targets, manualOnly, canRun, onCopied,
}: {
  targets: BackupTarget[]
  manualOnly: boolean
  canRun: boolean
  onCopied: () => void
}) {
  const enabled = targets.filter((t) => t.enabled)
  const slots = [...new Set(enabled.map((t) => t.slot))]

  return (
    <section className="profile__section profile__section--wide" id="storage-targets">
      <h2>Storage targets</h2>
      <p className="muted small">
        Where copies of this instance are kept, other than on this machine. Configured in
        the server&rsquo;s <code>.env</code> and read only by the backup agents: the keys and
        passphrases never reach the application, so what is shown here are fingerprints.
      </p>

      {manualOnly && (
        <p className="alert alert--warning">
          <strong>This instance has no offsite backup.</strong> The only target configured is a
          removable drive, which is copied to when somebody asks rather than on a schedule.
          Between those times there is no copy anywhere but this machine.
        </p>
      )}

      {slots.length === 0 ? (
        <p className="muted">
          No offsite target is configured, so the only copies of this instance are on this
          machine. A disk failure would take the backups with it. See{' '}
          <code>docs/backup-recovery.md</code> for setting one up.
        </p>
      ) : (
        <div className="backup-cards">
          {slots.map((slot) => (
            <TargetCard
              key={slot}
              slot={slot}
              rows={enabled.filter((t) => t.slot === slot)}
              canRun={canRun}
              onCopied={onCopied}
            />
          ))}
        </div>
      )}
    </section>
  )
}
