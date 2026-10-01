import { useState } from 'react'
import { api, ApiError, type BackupKeyStatus } from '../../api/client'

/**
 * Where the backup key is, in words (dev-plan 25.1). Shared by the setup
 * wizard and Administration, Backups, so both say the same thing. The key
 * itself is never shown: the app cannot read it, by design.
 */
export function BackupKeyWhere() {
  return (
    <ul className="small">
      <li>
        The file <code>backup-key.txt</code> in the folder Tesria was installed in, next to{' '}
        <code>docker-compose.yml</code>.
      </li>
      <li>
        Or, in a terminal in that folder: <code>docker compose run --rm init show-backup-key</code>
      </li>
    </ul>
  )
}

/**
 * The warning on Administration, Backups while a key Tesria generated has not
 * been said to be saved. It stays until someone with the backup policy right
 * says so, however many times the page is visited: a key that exists only on
 * this machine is lost with it, and every offsite copy with it.
 */
export function BackupKeyNotice({
  status, mayConfirm, onSaved,
}: {
  status: BackupKeyStatus
  mayConfirm: boolean
  onSaved: (next: BackupKeyStatus) => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  if (!status.generated || status.savedAt) return null

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      onSaved(await api.admin.backups.keySaved())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not record that.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="profile__section profile__section--wide backup-key-notice backups__section" role="alert">
      <h2>Save the Backup Key</h2>
      <p>
        Tesria made the key that encrypts your continuous backup, the one you can rewind to any
        moment, and it may exist only on the server. If the server is lost before the key is saved
        somewhere else, that backup cannot be restored, by anyone.
      </p>
      <p className="small"><strong>Where to find it</strong>, on the server:</p>
      <BackupKeyWhere />
      <p className="small">
        Put it in a password manager or another place that is not this server. Whoever runs the
        server can get it for you.
      </p>
      {error && <p className="alert alert--error small">{error}</p>}
      {mayConfirm ? (
        <button type="button" className="btn btn--primary" disabled={busy} onClick={confirm}>
          {busy ? 'Saving…' : 'I Saved It'}
        </button>
      ) : (
        <p className="muted small">An administrator who can change the backup policy can mark it saved.</p>
      )}
    </section>
  )
}
