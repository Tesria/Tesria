import { type FormEvent, useState } from 'react'
import { api, ApiError, Permission } from '../api/client'
import { useAuth } from '../auth/AuthContext'

type Props = {
  spaceId: string
  contentJson: string
  defaultName: string
}

/** Turns a page's current content into a reusable template. */
export function SaveAsTemplateButton({ spaceId, contentJson, defaultName }: Props) {
  // Instance-wide templates take their own right (dev-plan 14.1); without
  // it the choice is not offered at all.
  const mayOfferEverywhere = useAuth().can(Permission.TemplatesInstance)
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
          <label>
            Template name
            <input
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Such as Meeting notes"
              required
            />
          </label>
          {mayOfferEverywhere && (
            <label>
              Offer it in
              <select value={scope} onChange={(e) => setScope(e.target.value as 'space' | 'instance')}>
                <option value="space">This space only</option>
                <option value="instance">Every space (instance-wide)</option>
              </select>
            </label>
          )}
          <button type="submit" className="btn btn--primary btn--sm" disabled={busy}>
            {busy ? 'Saving…' : 'Save'}
          </button>
        </form>
      )}
    </>
  )
}
