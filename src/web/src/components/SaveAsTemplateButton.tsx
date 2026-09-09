import { type FormEvent, useState } from 'react'
import { api, ApiError } from '../api/client'

type Props = {
  spaceId: string
  contentJson: string
  defaultName: string
}

/** Turns a page's current content into a reusable template. */
export function SaveAsTemplateButton({ spaceId, contentJson, defaultName }: Props) {
  const [open, setOpen] = useState(false)
  const [name, setName] = useState(defaultName)
  const [scope, setScope] = useState<'space' | 'instance'>('space')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!name.trim()) return
    setBusy(true)
    setError(null)
    try {
      await api.templates.create({
        spaceId: scope === 'space' ? spaceId : null,
        name,
        contentJson,
      })
      setDone(true)
      setOpen(false)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the template.')
    } finally {
      setBusy(false)
    }
  }

  if (done) return <span className="muted small">Saved as template ✓</span>

  return (
    <>
      <button type="button" className="btn btn--ghost" onClick={() => setOpen((v) => !v)}>
        {open ? 'Cancel' : 'Save as template'}
      </button>
      {open && (
        <form className="card principal-picker template-form" onSubmit={submit}>
          {error && <p className="alert alert--error">{error}</p>}
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Template name"
            required
          />
          <select value={scope} onChange={(e) => setScope(e.target.value as 'space' | 'instance')}>
            <option value="space">This space only</option>
            <option value="instance">Instance-wide</option>
          </select>
          <button type="submit" className="btn btn--primary btn--sm" disabled={busy}>
            {busy ? 'Saving…' : 'Save'}
          </button>
        </form>
      )}
    </>
  )
}
