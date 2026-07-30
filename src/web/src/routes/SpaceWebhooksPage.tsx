import { type FormEvent, useCallback, useEffect, useState } from 'react'
import { api, ApiError, type CreatedWebhook, type Webhook } from '../api/client'
import { useSpaceContext } from './SpacePage'

export function SpaceWebhooksPage() {
  const { space } = useSpaceContext()
  const [hooks, setHooks] = useState<Webhook[] | null>(null)
  const [url, setUrl] = useState('')
  const [events, setEvents] = useState('*')
  const [error, setError] = useState<string | null>(null)
  const [forbidden, setForbidden] = useState(false)
  const [justCreated, setJustCreated] = useState<CreatedWebhook | null>(null)

  const load = useCallback(() => {
    setError(null)
    api.webhooks
      .list(space.key)
      .then((h) => {
        setHooks(h)
        setForbidden(false)
      })
      .catch((err: unknown) => {
        if (err instanceof ApiError && err.status === 403) setForbidden(true)
        else setError(err instanceof Error ? err.message : 'Failed to load webhooks.')
      })
  }, [space.key])

  useEffect(load, [load])

  async function create(e: FormEvent) {
    e.preventDefault()
    if (!url.trim() || !events.trim()) return
    setError(null)
    try {
      const created = await api.webhooks.create(space.key, { url, events })
      setJustCreated(created)
      setUrl('')
      load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create the webhook.')
    }
  }

  async function remove(id: string) {
    if (!confirm('Delete this webhook?')) return
    await api.webhooks.remove(space.key, id)
    load()
  }

  if (forbidden) {
    return (
      <div className="page-wrap">
        <h1>Webhooks</h1>
        <p className="alert alert--error">You need admin rights on this space to manage its webhooks.</p>
      </div>
    )
  }

  return (
    <div className="page-wrap">
      <h1>Webhooks</h1>
      <p className="muted small">
        POST a signed payload to a URL when something happens in this space — e.g.{' '}
        <code>page.created</code>, <code>page.updated</code>, <code>comment.created</code>, or{' '}
        <code>*</code> for everything.
      </p>
      {error && <p className="alert alert--error">{error}</p>}

      {justCreated && (
        <div className="panel">
          <p style={{ marginTop: 0 }}>
            <strong>Copy this signing secret now — it won't be shown again:</strong>
          </p>
          <code style={{ wordBreak: 'break-all', display: 'block', marginBottom: '0.5rem' }}>
            {justCreated.secret}
          </code>
          <p className="muted small">
            Each delivery includes an <code>X-Webhook-Signature: sha256=…</code> header — an HMAC-SHA256
            of the request body using this secret — so you can verify it really came from here.
          </p>
          <button type="button" className="btn btn--ghost btn--sm" onClick={() => setJustCreated(null)}>
            Done
          </button>
        </div>
      )}

      <form className="panel form-inline" onSubmit={create}>
        <label>
          URL
          <input
            type="url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            placeholder="https://example.com/hooks/confluenceclone"
            required
          />
        </label>
        <label>
          Events
          <input value={events} onChange={(e) => setEvents(e.target.value)} placeholder="* or page.updated,comment.created" />
        </label>
        <button type="submit" className="btn btn--primary">Add webhook</button>
      </form>

      {hooks && hooks.length === 0 && <p className="muted">No webhooks configured.</p>}
      <ul className="version-list">
        {hooks?.map((h) => (
          <li key={h.id} className="version">
            <span className="version__num" style={{ wordBreak: 'break-all' }}>{h.url}</span>
            <span className="badge">{h.events}</span>
            <span className="version__actions">
              <button type="button" className="link-btn link-btn--danger" onClick={() => remove(h.id)}>
                Delete
              </button>
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}
