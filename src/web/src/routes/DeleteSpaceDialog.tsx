import { type FormEvent, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type Space, type SpaceDeletionPreview } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { PasswordInput } from '../components/PasswordInput'

function bytes(value: number): string {
  if (value === 0) return 'no files'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  return `${(value / 1024 ** i).toFixed(i === 0 ? 0 : 1)} ${units[i]}`
}

const count = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`

/**
 * Deleting a space (dev-plan 11.3).
 *
 * Two answers, for two different mistakes. Typing the key proves the right
 * space is on screen, which is the error someone actually makes; the password
 * proves it is them, and is checked by the server in the same request rather
 * than by the five-minute sudo window, because "you signed in a few minutes
 * ago" is not "you mean it".
 */
export function DeleteSpaceDialog({
  space, onCancel, onDeleted,
}: {
  space: Space
  onCancel: () => void
  onDeleted: () => void
}) {
  const { user } = useAuth()
  const [preview, setPreview] = useState<SpaceDeletionPreview | null>(null)
  const [confirmKey, setConfirmKey] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // An account without a password (provisioned through SSO) confirms with a
  // one-time code instead, the same choice /auth/reauth offers.
  const byCode = user !== null && user !== undefined && !user.hasPassword

  useEffect(() => {
    let canceled = false
    api.spaces.deletionPreview(space.key)
      .then((p) => !canceled && setPreview(p))
      .catch(() => !canceled && setError('Could not count what is in this space.'))
    return () => { canceled = true }
  }, [space.key])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && !busy && onCancel()
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [busy, onCancel])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.spaces.remove(space.key, {
        confirmKey,
        password: password || undefined,
        code: code || undefined,
      })
      onDeleted()
    } catch (err) {
      setError(err instanceof ApiError && err.status === 401
        ? 'That did not match. The attempt counts toward locking this account.'
        : err instanceof ApiError ? err.message : 'Could not delete the space.')
      setBusy(false)
    }
  }

  const answered = confirmKey.length > 0 && (byCode ? code.length > 0 : password.length > 0)

  return (
    <div className="recovery-prompt" role="dialog" aria-modal="true" aria-label={`Delete the space ${space.key}`}>
      <form className="recovery-prompt__card danger-form" onSubmit={submit}>
        <h2>Delete {space.name}?</h2>

        <div className="confirm__body">
          <p>
            This destroys <strong>{preview ? count(preview.pages, 'page', 'pages') : 'every page'}</strong>
            {preview && preview.attachments > 0 && (
              <> and <strong>{count(preview.attachments, 'attachment', 'attachments')}</strong> ({bytes(preview.bytes)})</>
            )}
            {' '}in <strong>{space.key}</strong>, with every version, comment and
            restriction. Pages already in the trash go too, and none of it can be
            restored from there.
          </p>
          <p>
            Only a backup taken before now still holds this content. It cannot be
            undone from inside Tesria.
          </p>
          {preview?.isPublic && (
            <p className="alert alert--error small">
              This space is published. Its public URLs stop working immediately.
            </p>
          )}
          <p>
            If you only want it out of the way,{' '}
            <Link to={`/spaces/${space.key}/settings#archive`} onClick={onCancel}>archive it
            instead</Link>. That is reversible, and a space administrator can do it.
          </p>
        </div>

        {error && <p className="alert alert--error">{error}</p>}

        <label>
          <span>Type <strong>{space.key}</strong> to confirm</span>
          <input
            value={confirmKey}
            onChange={(e) => setConfirmKey(e.target.value)}
            autoFocus
            autoComplete="off"
            spellCheck={false}
            aria-label={`Type ${space.key} to confirm`}
          />
        </label>

        {byCode ? (
          <label>
            <span>A code from your authenticator</span>
            <input
              value={code}
              onChange={(e) => setCode(e.target.value)}
              inputMode="numeric"
              autoComplete="one-time-code"
              placeholder="123456"
            />
          </label>
        ) : (
          <label>
            <span>Your password</span>
            <PasswordInput
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
            />
          </label>
        )}

        <div className="row-gap">
          <button type="submit" className="btn btn--danger" disabled={busy || !answered}>
            {busy ? 'Deleting…' : `Delete ${space.key}`}
          </button>
          <button type="button" className="btn btn--ghost" disabled={busy} onClick={onCancel}>
            Cancel
          </button>
        </div>
      </form>
    </div>
  )
}
