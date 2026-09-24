import { type FormEvent, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type ImportedPack } from '../api/client'
import { SpaceAccessEditor } from './SpaceAccessEditor'

/** "1 page", "3 pages": an English plural, which the counts here all need. */
function count(n: number, noun: string) {
  return `${n} ${n === 1 ? noun : `${noun}s`}`
}

/** "Ada", "Ada and Grace", "Ada, Grace and Alan". */
function list(names: string[]) {
  if (names.length <= 1) return names[0] ?? ''
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}

/**
 * Spaces → Import a pack (dev-plan 8.5).
 *
 * The result is the interesting part of this form. An import deliberately
 * drops two things the pack could not honestly carry: who was allowed to read
 * what, and who wrote what. Both are stated afterwards rather than in the
 * small print beforehand, because the moment they matter is the moment the
 * space exists and somebody has to go and set them.
 */
export function ImportPackForm({ onImported }: { onImported: () => void }) {
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportedPack | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const file = fileRef.current?.files?.[0]
    if (!file) {
      setError('Choose a pack file.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const imported = await api.spaces.importPack(file, key.trim().toUpperCase(), name.trim() || undefined)
      setResult(imported)
      // The list reloads rather than being patched with a space assembled
      // here: the server decided what this space actually is, and guessing
      // the other half of it would show something subtly wrong until the
      // next refresh.
      onImported()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The pack could not be imported.')
    } finally {
      setBusy(false)
    }
  }

  if (result) {
    return (
      <div className="card">
        <p className="profile__ok">
          <strong>{result.name}</strong> is in, under the key <code>{result.key}</code>:{' '}
          {count(result.pages, 'page')}, {count(result.versions, 'version')},{' '}
          {count(result.attachments, 'attachment')}, {count(result.comments, 'comment')} and{' '}
          {count(result.templates, 'template')}.
        </p>
        {(result.spaceRestrictions > 0 || result.pageRestrictions > 0) && (
          <p className="alert alert--warning">
            The original had restrictions on who could read it: {result.spaceRestrictions} on the space
            and {result.pageRestrictions} on individual pages. Those do not travel in a pack, because
            they name people on another instance. Set the page restrictions again from each page’s
            Restrictions tab.
          </p>
        )}
        <p className="muted small">
          Everything here is attributed to you.{' '}
          {result.authors.length === 0
            ? 'The pack recorded no original authors.'
            : `It was originally written by ${list(result.authors)}.`}{' '}
          That record is kept in the audit log rather than asserted against accounts on this
          instance, because a name is not an account.
        </p>
        {result.source && (
          <p className="muted small">
            Exported from {result.source}
            {result.madeWith && result.madeWith !== 'Tesria' ? `, made with ${result.madeWith}` : ''}.
          </p>
        )}
        {/* Straight on to access (dev-plan 15.1): the space starts private to
            whoever imported it, and this is the moment to say who else. */}
        <h2 style={{ fontSize: '1.05rem', marginBottom: '0.25rem' }}>Who should have access?</h2>
        <p className="muted small">
          For now only you can see {result.name}. Give access to people or groups: the Users group
          is everyone with an account. You can change this later in the space’s settings, under Permissions.
        </p>
        <SpaceAccessEditor spaceKey={result.key} />
        <Link className="btn btn--primary" to={`/spaces/${result.key}`}>
          Open {result.name}
        </Link>
      </div>
    )
  }

  return (
    <form className="card" onSubmit={submit}>
      <label>
        Pack file
        <input ref={fileRef} type="file" accept=".zip,application/zip" required />
      </label>
      <label>
        Key
        <input
          value={key}
          onChange={(e) => setKey(e.target.value.toUpperCase())}
          placeholder="HANDBOOK"
          required
        />
      </label>
      <label>
        Name <span className="muted small">(optional)</span>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Whatever the pack calls it"
        />
      </label>
      {error && <p className="alert alert--error">{error}</p>}
      <button type="submit" className="btn btn--primary" disabled={busy}>
        {busy ? 'Importing…' : 'Import'}
      </button>
      <p className="muted small">
        The key has to be one nothing else is using: importing the same pack twice gives you two
        separate spaces, never a merge.
      </p>
    </form>
  )
}
