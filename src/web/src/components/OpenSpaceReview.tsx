import { useState } from 'react'
import { api, ApiError, type AdminSpace } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { needsReview } from './adminAccess'

type Choice = 'private' | 'edit' | 'keep'

/**
 * The open-space review (dev-plan 21.5), at the top of Admin, Spaces: every
 * space that everyone signed in may administer and nobody chose to leave that
 * way. Before 0.9 a space with no permissions was open like this, so an
 * instance upgraded from 0.8 starts with most of its spaces here.
 *
 * Never automatic (the owner's decision, 2026-10-08): each space is settled
 * by a person choosing one of three things, because narrowing quietly takes
 * administration away from everyone who uses it.
 */
export function OpenSpaceReview({ spaces, signedIn, onChanged, onDetails }: {
  spaces: AdminSpace[]
  /** How many accounts "everyone signed in" is, when known. */
  signedIn: number | null
  onChanged: () => void
  onDetails: (s: AdminSpace) => void
}) {
  const pending = spaces.filter((s) => needsReview(s.access))
  const [open, setOpen] = useState<string | null>(null)
  if (pending.length === 0) return null

  const everyone = signedIn == null ? 'Every account' : `Every account (${signedIn})`
  return (
    <section className="alert alert--warning open-review" aria-labelledby="open-review-title">
      <h2 id="open-review-title" className="open-review__title">
        {pending.length === 1 ? '1 space lets' : `${pending.length} spaces let`} everyone administer {pending.length === 1 ? 'it' : 'them'}
      </h2>
      <p>
        Before 0.9, a space with no permissions was open to everyone signed in, as administrators.{' '}
        {pending.length === 1 ? 'This space is' : 'These spaces are'} still like that because nobody has chosen otherwise.{' '}
        {everyone} can change their settings, choose who else gets in, and permanently delete pages from their trash.
      </p>
      <p>Decide for each one. Nothing changes until you do.</p>
      <ul className="open-review__list">
        {pending.map((s) => (
          <li key={s.id} className="open-review__item">
            <div className="open-review__row">
              <strong>{s.name}</strong> <span className="badge">{s.key}</span>
              {!s.access?.hasExplicitAdmin && (
                <span className="badge badge--warning" title="Nobody is in its Admins group, or given Admin there">no admin of its own</span>
              )}
              <span className="open-review__actions">
                <button type="button" className="link-btn" onClick={() => onDetails(s)}>Who Has Access</button>
                <button type="button" className="link-btn" aria-expanded={open === s.id}
                  onClick={() => setOpen(open === s.id ? null : s.id)}>
                  {open === s.id ? 'Close' : 'Decide'}
                </button>
              </span>
            </div>
            {open === s.id && (
              <ReviewForm space={s} onDone={() => { setOpen(null); onChanged() }} />
            )}
          </li>
        ))}
      </ul>
    </section>
  )
}

function ReviewForm({ space, onDone }: { space: AdminSpace; onDone: () => void }) {
  const { user: me } = useAuth()
  const [choice, setChoice] = useState<Choice | null>(null)
  const hasAdmin = space.access?.hasExplicitAdmin ?? false
  const creatorIsMe = space.createdById === me?.id
  // Its creator by default when it has nobody to administer it: they made
  // it, and it keeps the administrator a person rather than whoever clicked.
  const [creator, setCreator] = useState(!hasAdmin)
  const [self, setSelf] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const narrowing = choice === 'private' || choice === 'edit'
  const admins = [...new Set([
    ...(creator ? [space.createdById] : []),
    ...(self && me ? [me.id] : []),
  ])]
  const missingAdmin = narrowing && !hasAdmin && admins.length === 0

  async function apply() {
    if (!choice || missingAdmin) return
    setBusy(true)
    setError(null)
    try {
      await api.admin.spaces.review(space.key, { choice, admins: narrowing ? admins : [] })
      onDone()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change the space.')
    } finally {
      setBusy(false)
    }
  }

  const option = (value: Choice, title: string, text: string) => (
    <label className="open-review__choice">
      <input type="radio" name={`review-${space.id}`} checked={choice === value} onChange={() => setChoice(value)} />
      <span><strong>{title}</strong> <span className="muted small">{text}</span></span>
    </label>
  )

  return (
    <div className="open-review__form">
      {error && <p className="alert alert--error">{error}</p>}
      <fieldset>
        <legend className="sr-only">What should happen to {space.name}?</legend>
        {option('private', 'Make it private.', 'Only the people in its groups get in. Everyone else loses access.')}
        {option('edit', 'Let everyone edit it.', 'Everyone keeps reading and editing; only its Admins manage it.')}
        {option('keep', 'Keep it as it is.', 'Everyone keeps administering it, and it leaves this list.')}
      </fieldset>
      {narrowing && (
        <fieldset className="open-review__admins">
          <legend>Who administers it afterwards</legend>
          {hasAdmin && <p className="muted small">It has administrators of its own already. You can add to them.</p>}
          <label className="open-review__choice">
            <input type="checkbox" checked={creator} onChange={(e) => setCreator(e.target.checked)} />
            <span>{space.createdByName}{creatorIsMe ? ' (you)' : ''}, who created it</span>
          </label>
          {!creatorIsMe && (
            <label className="open-review__choice">
              <input type="checkbox" checked={self} onChange={(e) => setSelf(e.target.checked)} />
              <span>You <span className="muted small">(like Get Access: recorded, and every administrator is alerted)</span></span>
            </label>
          )}
          {missingAdmin && <p className="small">Choose at least one, or nobody could manage it.</p>}
        </fieldset>
      )}
      <div className="row-gap">
        <button type="button" className="btn btn--primary btn--sm" disabled={!choice || missingAdmin || busy} onClick={() => void apply()}>
          {busy ? 'Saving…' : choice === 'keep' ? 'Keep It' : choice === 'edit' ? 'Let Everyone Edit' : choice === 'private' ? 'Make It Private' : 'Choose One'}
        </button>
      </div>
    </div>
  )
}
