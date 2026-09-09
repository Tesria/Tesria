import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError, type ApiTokenSummary, type CreatedApiToken } from '../api/client'

/** Profile → API tokens: personal credentials, so they live with the profile. */
export function ApiTokensSection() {
  const [tokens, setTokens] = useState<ApiTokenSummary[] | null>(null)
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [justCreated, setJustCreated] = useState<CreatedApiToken | null>(null)

  function load() {
    api.apiTokens.list().then(setTokens).catch(() => {})
  }

  useEffect(load, [])

  async function create(e: FormEvent) {
    e.preventDefault()
    if (!name.trim()) return
    setError(null)
    try {
      const created = await api.apiTokens.create(name)
      setJustCreated(created)
      setName('')
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the token.')
    }
  }

  async function revoke(id: string) {
    if (!confirm('Revoke this token? Anything using it will stop working immediately.')) return
    await api.apiTokens.revoke(id)
    load()
  }

  return (
    <>
      <p className="muted small">
        Use a token to call the REST API from scripts or integrations, without a browser session:{' '}
        <code>Authorization: Bearer &lt;token&gt;</code>. A token can do anything you can do.
      </p>
      {error && <p className="alert alert--error">{error}</p>}

      {justCreated && (
        <div className="panel">
          <p style={{ marginTop: 0 }}>
            <strong>Copy this token now — it won't be shown again:</strong>
          </p>
          <code style={{ wordBreak: 'break-all', display: 'block', marginBottom: '0.5rem' }}>
            {justCreated.token}
          </code>
          <button type="button" className="btn btn--ghost btn--sm" onClick={() => setJustCreated(null)}>
            Done
          </button>
        </div>
      )}

      <form className="panel form-inline" onSubmit={create}>
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} placeholder="CI pipeline" required />
        </label>
        <button type="submit" className="btn btn--primary">Create token</button>
      </form>

      {tokens && tokens.length === 0 && <p className="muted">No tokens yet.</p>}
      <ul className="version-list">
        {tokens?.map((t) => (
          <li key={t.id} className="version">
            <span className="version__num">{t.name}</span>
            <code className="muted small">{t.prefix}</code>
            <span className="muted small">
              {t.lastUsedAt ? `last used ${new Date(t.lastUsedAt).toLocaleString()}` : 'never used'}
            </span>
            <span className="version__actions">
              <button type="button" className="link-btn link-btn--danger" onClick={() => revoke(t.id)}>
                Revoke
              </button>
            </span>
          </li>
        ))}
      </ul>
    </>
  )
}
