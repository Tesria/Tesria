import { type FormEvent, useEffect, useState } from 'react'
import {
  api,
  ApiError,
  type Backup,
  type BackupOverview,
  type RestorePreview,
} from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { PasswordInput } from '../../components/PasswordInput'
import { bytes, relative } from './format'

/**
 * Restoring the wiki from the admin page (dev-plan 9.4): the dialog that asks
 * for it, the progress while it runs, and the kept copy afterwards.
 *
 * Kept out of AdminBackupsPage because it is a different kind of thing. That
 * page reports on backups; this one spends them. It follows the same two-
 * answer pattern as deleting a space (11.3), for the same two mistakes:
 * typing the label proves the right backup is on screen, and the password
 * proves it is them, checked in the request rather than by the sudo window.
 */

function when(iso: string | null | undefined): string {
  return iso ? new Date(iso).toLocaleString() : 'Unknown'
}

/** A local datetime string the `datetime-local` input accepts. */
function toLocalInput(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/**
 * What has happened since the backup, in words. This is the sentence somebody
 * actually decides on, so it counts things a person would miss rather than
 * rows in tables.
 */
function losses(p: RestorePreview): string[] {
  const out: string[] = []
  const n = (v: number, one: string, many: string) => `${v} ${v === 1 ? one : many}`
  if (p.versionsSaved > 0) out.push(n(p.versionsSaved, 'page edit', 'page edits'))
  if (p.pagesCreated > 0) out.push(n(p.pagesCreated, 'new page', 'new pages'))
  if (p.commentsPosted > 0) out.push(n(p.commentsPosted, 'comment', 'comments'))
  if (p.attachmentsAdded > 0) out.push(`${n(p.attachmentsAdded, 'attachment', 'attachments')} (${bytes(p.attachmentBytes)})`)
  if (p.accountsCreated > 0) out.push(n(p.accountsCreated, 'new account', 'new accounts'))
  return out
}

export function RestoreDialog({
  backup, onClose, onQueued,
}: {
  backup: Backup
  onClose: () => void
  onQueued: (jobId: string) => void
}) {
  const { user } = useAuth()
  const [preview, setPreview] = useState<RestorePreview | null>(null)
  const [at, setAt] = useState('')
  const [confirmLabel, setConfirmLabel] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const byCode = user !== null && user !== undefined && !user.hasPassword

  useEffect(() => {
    let canceled = false
    api.admin.backups.restorePreview(backup.label, at ? new Date(at).toISOString() : undefined)
      .then((p) => !canceled && setPreview(p))
      .catch(() => !canceled && setError('Could not read this backup.'))
    return () => { canceled = true }
  }, [backup.label, at])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !busy && onClose()
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [busy, onClose])

  const isPitr = preview?.mode === 'pitr'
  const answered = confirmLabel === backup.label && (byCode ? code.length > 0 : password.length > 0)
  const allowed = preview !== null && preview.blockedBy.length === 0

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const res = await api.admin.backups.restore(backup.label, {
        confirmLabel,
        password: password || undefined,
        code: code || undefined,
        at: isPitr && at ? new Date(at).toISOString() : null,
      })
      onQueued(res.jobId)
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? 'That did not match. The attempt counts toward locking this account.'
        : err instanceof ApiError ? err.message : 'Could not start the restore.')
      setBusy(false)
    }
  }

  const missed = preview ? losses(preview) : []

  return (
    <div className="recovery-prompt" role="dialog" aria-modal="true" aria-label={`Restore the backup ${backup.label}`}>
      <form className="recovery-prompt__card recovery-prompt__card--roomy danger-form" onSubmit={submit}>
        <h2>Restore this backup?</h2>

        <div className="confirm__body">
          {!preview ? (
            <p className="muted">Reading the backup…</p>
          ) : (
            <>
              <p>
                This replaces <strong>the whole wiki</strong> with the copy taken{' '}
                <strong>{when(preview.backupAt)}</strong>
                {isPitr && <>, rolled forward to the moment you choose</>}.
              </p>

              {missed.length > 0 ? (
                <p>
                  Everything written since then goes back too:{' '}
                  <strong>{missed.join(', ')}</strong>.
                </p>
              ) : (
                <p className="muted">Nothing has been written since that backup was taken.</p>
              )}

              <p>
                A backup of the wiki as it is now is taken first, always, and the copy this
                replaces is kept so you can undo it.
              </p>

              {preview.sessionsEnding > 0 && (
                <p className="muted small">
                  {preview.sessionsEnding} sign-in{preview.sessionsEnding === 1 ? '' : 's'} from after
                  the backup will end, so some people, possibly including you, will have to sign in
                  again. That is expected.
                </p>
              )}

              {preview.blockedBy.length > 0 && (
                <div className="alert alert--error small">
                  {preview.blockedBy.map((b) => <p key={b}>{b}</p>)}
                </div>
              )}
            </>
          )}
        </div>

        {isPitr && preview && (
          <label>
            <span>Roll forward to</span>
            <input
              type="datetime-local"
              value={at || (preview.targetAt ? toLocalInput(preview.targetAt) : '')}
              min={preview.earliestTarget ? toLocalInput(preview.earliestTarget) : undefined}
              max={preview.latestTarget ? toLocalInput(preview.latestTarget) : undefined}
              onChange={(e) => setAt(e.target.value)}
            />
            <span className="muted small">
              Anywhere between {when(preview.earliestTarget)} and {when(preview.latestTarget)}.
            </span>
          </label>
        )}

        {error && <p className="alert alert--error">{error}</p>}

        <label>
          <span>Type <strong>{backup.label}</strong> to confirm</span>
          <input
            value={confirmLabel}
            onChange={(e) => setConfirmLabel(e.target.value)}
            autoFocus
            autoComplete="off"
            spellCheck={false}
            aria-label={`Type ${backup.label} to confirm`}
          />
        </label>

        {byCode ? (
          <label>
            <span>A code from your authenticator</span>
            <input value={code} onChange={(e) => setCode(e.target.value)}
              inputMode="numeric" autoComplete="one-time-code" placeholder="123456" />
          </label>
        ) : (
          <label>
            <span>Your password</span>
            <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password" />
          </label>
        )}

        <div className="row-gap">
          <button type="submit" className="btn btn--danger" disabled={busy || !answered || !allowed}>
            {busy ? 'Starting…' : 'Restore now'}
          </button>
          <button type="button" className="btn btn--ghost" onClick={onClose} disabled={busy}>Cancel</button>
        </div>
      </form>
    </div>
  )
}

/**
 * What the person who started the restore watches. During a point-in-time
 * restore the database is down, so the elapsed time comes from the clock:
 * saying "still working, this long" honestly beats an empty screen.
 */
export function RestoreProgress({ restore, onCancel }: {
  restore: BackupOverview['restore']
  onCancel: () => void
}) {
  const [elapsed, setElapsed] = useState('')

  useEffect(() => {
    if (!restore.startedAt) return
    const started = new Date(restore.startedAt).getTime()
    const tick = () => {
      const secs = Math.max(0, Math.round((Date.now() - started) / 1000))
      setElapsed(secs < 90 ? `${secs} seconds` : `${Math.round(secs / 60)} minutes`)
    }
    tick()
    const timer = window.setInterval(tick, 1000)
    return () => window.clearInterval(timer)
  }, [restore.startedAt])

  return (
    <section className="profile__section profile__section--wide">
      <h2>A restore is in progress</h2>
      <p>
        The wiki is read-only for everyone until it finishes. Started {when(restore.startedAt)},
        {' '}{elapsed} ago.
      </p>
      <p className="muted small">
        A backup is taken first, then the copy is restored beside the live one and checked, then
        swapped in. The application restarts itself at the end, so this page comes back on its own.
      </p>
      {restore.cancelRequested ? (
        <p className="muted">
          Stopping has been requested. If the switch has already happened it will finish, and undoing
          it is then the way back.
        </p>
      ) : (
        <button type="button" className="btn btn--ghost" onClick={onCancel}>Stop the restore</button>
      )}
    </section>
  )
}

/**
 * The copy the last restore replaced. It is the undo, so it is shown until it
 * is gone, with what it costs and when the retention policy will take it.
 */
export function KeptCopyCard({ restore, onChanged }: {
  restore: BackupOverview['restore']
  onChanged: () => void
}) {
  const { user } = useAuth()
  const kept = restore.keptCopy
  const [action, setAction] = useState<'undo' | 'discard' | null>(null)
  const [confirmLabel, setConfirmLabel] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!kept || kept.removedAt) return null

  const byCode = user !== null && user !== undefined && !user.hasPassword
  const word = action === 'undo' ? 'UNDO' : 'REMOVE'
  const answered = confirmLabel === word && (byCode ? code.length > 0 : password.length > 0)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const input = { confirmLabel, password: password || undefined, code: code || undefined }
      if (action === 'undo') await api.admin.backups.undoRestore(input)
      else await api.admin.backups.discardKept(input)
      setAction(null)
      setConfirmLabel('')
      setPassword('')
      setCode('')
      onChanged()
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? 'That did not match. The attempt counts toward locking this account.'
        : err instanceof ApiError ? err.message : 'That did not work.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="profile__section profile__section--wide">
      <h2>The copy kept before the last restore</h2>
      <p>
        The wiki was restored from <code>{restore.lastRestoreFrom}</code>{' '}
        {relative(restore.lastRestoredAt ?? kept.restoredAt)}.
        {kept.mode === 'logical'
          ? ' The wiki as it was just before that is kept, and putting it back takes a moment.'
          : ' Going back means another point-in-time restore, to the moment this one began.'}
      </p>

      <dl className="backup-card__facts">
        <div><dt>Kept since</dt><dd>{when(kept.restoredAt)}</dd></div>
        {kept.databaseBytes !== null && <div><dt>Size</dt><dd>{bytes(kept.databaseBytes)}</dd></div>}
        <div>
          <dt>Removed by the policy</dt>
          <dd>{restore.keptCopyExpiresAt ? when(restore.keptCopyExpiresAt) : 'Not while retention is off'}</dd>
        </div>
      </dl>
      <p className="muted small">It counts toward the wiki's space on the charts above until it goes.</p>

      {action === null ? (
        <div className="row-gap">
          <button type="button" className="btn" onClick={() => setAction('undo')}>Undo the restore</button>
          <button type="button" className="btn btn--ghost" onClick={() => setAction('discard')}>Remove the copy</button>
        </div>
      ) : (
        <form className="danger-form" onSubmit={submit}>
          <p>
            {action === 'undo'
              ? 'This puts the previous wiki back. A backup of the current one is taken first, and everyone is signed out of their editors while it happens.'
              : 'This removes the kept copy permanently. After it, the restore cannot be undone.'}
          </p>
          {error && <p className="alert alert--error">{error}</p>}
          <label>
            <span>Type <strong>{word}</strong> to confirm</span>
            <input value={confirmLabel} onChange={(e) => setConfirmLabel(e.target.value)}
              autoComplete="off" spellCheck={false} aria-label={`Type ${word} to confirm`} />
          </label>
          {byCode ? (
            <label>
              <span>A code from your authenticator</span>
              <input value={code} onChange={(e) => setCode(e.target.value)}
                inputMode="numeric" autoComplete="one-time-code" placeholder="123456" />
            </label>
          ) : (
            <label>
              <span>Your password</span>
              <PasswordInput value={password} onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password" />
            </label>
          )}
          <div className="row-gap">
            <button type="submit" className="btn btn--danger" disabled={busy || !answered}>
              {action === 'undo' ? 'Undo the restore' : 'Remove the copy'}
            </button>
            <button type="button" className="btn btn--ghost" onClick={() => setAction(null)} disabled={busy}>
              Cancel
            </button>
          </div>
        </form>
      )}
    </section>
  )
}
