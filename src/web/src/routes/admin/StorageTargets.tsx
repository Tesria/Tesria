import { useState } from 'react'
import { api, ApiError, type BackupTarget } from '../../api/client'
import { PieChart } from '../../components/PieChart'

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
  // First, because it outranks everything else here: a copy that exists,
  // passes its own integrity check and will not turn back into a database
  // is the failure all of this is meant to prevent.
  if (target.lastDrillOk === false)
    return { tone: 'bad', text: 'The last restore drill failed' }
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
        <dt>Last restore drill</dt>
        <dd>
          {primary.lastDrillAt
            ? <>
                {relative(primary.lastDrillAt)} ·{' '}
                {primary.lastDrillOk
                  ? 'restored cleanly'
                  : <span className="backup-text--bad">did not restore</span>}
              </>
            : 'Never'}
        </dd>
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

      <Composition rows={rows} />

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

/**
 * Roughly what this much storage costs a month, from the provider's list
 * price. Deliberately "about", and deliberately showing when the price was
 * last checked: list prices move, and a stale number presented as fact is a
 * small lie on a card whose job is to be trusted. Storage only; egress is
 * free on B2 up to three times what you store, and a restore is the only
 * time it matters.
 */
const PRICES_CHECKED = 'Sep 2026'
const PRICE_PER_GB_MONTH: Record<string, number> = {
  b2: 0.006,   // $6/TB, first 10GB free
  s3: 0.023,   // S3 Standard
}

function cost(type: string | null | undefined, bytesStored: number): string | null {
  const rate = type ? PRICE_PER_GB_MONTH[type] : undefined
  if (rate === undefined) return null
  const gb = bytesStored / 1024 ** 3
  const free = type === 'b2' ? 10 : 0
  const billable = Math.max(0, gb - free)
  const monthly = billable * rate
  if (monthly < 0.01) return `nothing yet (${PRICES_CHECKED} prices)`
  return `$${monthly.toFixed(2)} a month (${PRICES_CHECKED} prices)`
}

/**
 * What this target is holding, as a pie.
 *
 * Cloud storage has no free space and no total, so a used-against-free chart
 * would have to invent a denominator, which would be a lie on a card whose
 * whole job is to be trusted. This charts **composition** instead: the
 * database repository against the files one, which is a real ratio and the
 * one worth knowing, since the database is usually the smaller of the two
 * and grows differently. A slot with only one repository has no composition
 * to show and gets nothing.
 */
function Composition({ rows }: { rows: BackupTarget[] }) {
  const sized = rows.filter((r) => (r.bytesStored ?? 0) > 0)
  if (sized.length < 2) return null

  const slices = sized.map((r, i) => ({
    label: KIND_LABEL[r.kind] ?? r.kind,
    value: r.bytesStored ?? 0,
    color: i === 0 ? 'var(--chart-wiki)' : 'var(--chart-backups)',
  }))
  const total = slices.reduce((sum, s) => sum + s.value, 0)

  return (
    <div className="disk-chart">
      <PieChart
        slices={slices}
        size={72}
        label={slices.map((s) => `${s.label} ${bytes(s.value)}`).join(', ')}
      />
      <ul className="disk-chart__legend">
        {slices.map((s) => (
          <li key={s.label} className="disk-chart__row">
            <span className="disk-chart__name">
              <span className="disk-chart__key" style={{ background: s.color }} aria-hidden="true" />
              {s.label}
            </span>
            <span className="disk-chart__value">{bytes(s.value)}</span>
          </li>
        ))}
        <li className="disk-chart__row disk-chart__row--total">
          <span className="disk-chart__name">Stored</span>
          <span className="disk-chart__value">{bytes(total)}</span>
        </li>
        {cost(rows[0]?.type, total) && (
          <li className="disk-chart__row">
            <span className="disk-chart__name">Costs about</span>
            <span className="disk-chart__value">{cost(rows[0]?.type, total)}</span>
          </li>
        )}
      </ul>
    </div>
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
