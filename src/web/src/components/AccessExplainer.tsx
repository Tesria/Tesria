import { type FormEvent, useEffect, useMemo, useState } from 'react'
import { api, ApiError, type AccessExplanation, type Directory, type PageTreeNode, type Space } from '../api/client'
import { levelName, pageChoices, reasonText, spaceAnswer } from './groupsList'

/**
 * "Why can this person see this space?" (dev-plan 21.4). Pick a person and a
 * space, and optionally a page in it, and the server answers with the real
 * permission check made as that person, and every reason behind it.
 *
 * On the Groups page the space is chosen here; in a space's Permissions tab
 * it is that space (`space`).
 */
export function AccessExplainer({ space }: { space?: Pick<Space, 'id' | 'key' | 'name'> }) {
  const [people, setPeople] = useState<Directory[]>([])
  const [spaces, setSpaces] = useState<Space[]>([])
  const [userId, setUserId] = useState('')
  const [spaceKey, setSpaceKey] = useState(space?.key ?? '')
  const [tree, setTree] = useState<PageTreeNode[]>([])
  const [pageFilter, setPageFilter] = useState('')
  const [pageId, setPageId] = useState('')
  const [answer, setAnswer] = useState<AccessExplanation | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.users.list().then(setPeople).catch(() => {})
    if (!space) api.spaces.list(true).then(setSpaces).catch(() => {})
  }, [space])

  const chosenSpace = space ?? spaces.find((s) => s.key === spaceKey)
  const chosenSpaceId = chosenSpace?.id
  useEffect(() => {
    if (!chosenSpaceId) return
    let live = true
    api.pages.tree(chosenSpaceId).then((t) => { if (live) setTree(t) }).catch(() => { if (live) setTree([]) })
    return () => { live = false }
  }, [chosenSpaceId])

  const choices = useMemo(() => pageChoices(tree, pageFilter).slice(0, 300), [tree, pageFilter])

  async function check(e: FormEvent) {
    e.preventDefault()
    if (!userId || !spaceKey) return
    setBusy(true)
    setError(null)
    try {
      setAnswer(await api.access.explain({ userId, space: spaceKey, pageId: pageId || undefined }))
    } catch (err) {
      setAnswer(null)
      setError(err instanceof ApiError ? err.message : 'Could not check their access.')
    } finally {
      setBusy(false)
    }
  }

  return (
    // In a space's Permissions tab it is one more of the tab's sections.
    <section className={`${space ? 'profile__section profile__section--wide' : 'card'} access-explainer`}>
      <h2 className="access-explainer__title">Why can someone see {space ? 'this space' : 'a space'}?</h2>
      <p className="muted small">
        Choose a person{space ? '' : ' and a space'}, and a page if you like, to see what they may do there and every
        reason why. The answer is the same check Tesria makes when they open it.
      </p>
      <form className="audit-filters" onSubmit={check}>
        <label className="audit-filters__field audit-filters__field--action">
          Person
          <span className="glass-select-wrap"><select className="glass-select" value={userId}
            onChange={(e) => { setUserId(e.target.value); setAnswer(null) }} required>
            <option value="">Choose a person…</option>
            {people.map((p) => (
              <option key={p.id} value={p.id}>{p.displayName}{p.email ? ` (${p.email})` : ''}</option>
            ))}
          </select></span>
        </label>
        {!space && (
          <label className="audit-filters__field audit-filters__field--action">
            Space
            <span className="glass-select-wrap"><select className="glass-select" value={spaceKey}
              onChange={(e) => { setSpaceKey(e.target.value); setPageId(''); setPageFilter(''); setTree([]); setAnswer(null) }} required>
              <option value="">Choose a space…</option>
              {spaces.map((s) => <option key={s.id} value={s.key}>{s.name}{s.archived ? ' (archived)' : ''}</option>)}
            </select></span>
          </label>
        )}
        {chosenSpace && tree.length > 0 && (
          <>
            <label className="audit-filters__field">
              Find a page
              <input value={pageFilter} onChange={(e) => setPageFilter(e.target.value)} placeholder="Title" />
            </label>
            <label className="audit-filters__field audit-filters__field--action">
              Page (optional)
              <span className="glass-select-wrap"><select className="glass-select" value={pageId}
                onChange={(e) => { setPageId(e.target.value); setAnswer(null) }}>
                <option value="">The whole space</option>
                {choices.map((p) => (
                  <option key={p.id} value={p.id}>{'  '.repeat(p.depth)}{p.title}</option>
                ))}
              </select></span>
            </label>
          </>
        )}
        <button type="submit" className="btn btn--primary audit-filters__clear" disabled={!userId || !spaceKey || busy}>
          {busy ? 'Checking…' : 'Check Access'}
        </button>
      </form>
      {error && <p className="alert alert--error">{error}</p>}
      {answer && <Answer answer={answer} />}
    </section>
  )
}

function Answer({ answer }: { answer: AccessExplanation }) {
  const counting = answer.reasons.filter((r) => r.counts)
  const notCounting = answer.reasons.filter((r) => !r.counts)
  const page = answer.page
  return (
    <div className="access-explainer__answer" aria-live="polite">
      <p className="access-explainer__headline">
        {spaceAnswer(answer)}
        {!answer.person.active && <span className="badge">suspended</span>}
      </p>
      {!answer.person.active && (
        <p className="muted small">
          Their account is suspended, so they cannot sign in and none of this reaches them now. Below is what they would
          have if it were active again.
        </p>
      )}

      {counting.length > 0 ? (
        <ul className="access-explainer__reasons">
          {counting.map((r, i) => (
            <li key={i}>
              <span className="badge">{levelName(r.level)}</span>
              <span>
                {reasonText(r)}
                {r.kind === 'group' && r.groupKind === 'space' && <span className="muted small"> (one of this space’s own groups)</span>}
                {r.recoveredAt && (
                  <span className="muted small"> They added themselves with Get Access on {new Date(r.recoveredAt).toLocaleDateString()}.</span>
                )}
              </span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="muted small">
          Nothing gives them access here: everyone signed in gets {answer.space.everyoneAccess == null ? 'nothing' : levelName(answer.space.everyoneAccess)},
          and no group they are in, nor any grant by name, reaches this space.
        </p>
      )}
      {notCounting.length > 0 && (
        <p className="muted small">
          Not counted while their account is suspended: {notCounting.map((r) => r.kind === 'everyone' ? 'what everyone signed in gets' : r.label).join(', ')}.
        </p>
      )}
      {answer.explicitAdmin && (
        <p className="muted small">They hold Admin here themselves, so they may lift page restrictions, though restrictions still bind them.</p>
      )}
      {answer.canRecoverAccess && (
        <p className="muted small">
          As an administrator who may manage spaces, they could also make themselves an administrator of this space with
          Get Access in Administration, Spaces. That is recorded in the audit log.
        </p>
      )}
      {answer.publiclyReadable && (
        <p className="muted small">Anyone can read this space’s unrestricted pages without an account.</p>
      )}

      {page && (
        <div className="access-explainer__page">
          <p className="access-explainer__headline">
            On the page “{page.title}”{' '}
            {page.canView ? (page.canEdit ? 'they can read and edit it.' : 'they can read it but not edit it.') : 'they cannot read it.'}
          </p>
          {page.draft && (
            <p className="muted small">
              It is a draft: only {page.isAuthor ? 'they (they started it) and ' : 'its author and '}the space’s editors can read it.
            </p>
          )}
          {page.restrictions.length === 0 ? (
            <p className="muted small">No restrictions on this page or the pages above it, so it follows the space.</p>
          ) : (
            <>
              <p className="muted small">
                Restrictions let in only the people and groups they name, the space’s administrators included. To read,
                they must be named in a reading restriction if there is one; to edit, in an editing one too.
                {page.canLift && ' As an administrator of the space they may lift these restrictions, in its Permissions tab.'}
              </p>
              <ul className="access-explainer__reasons">
                {page.restrictions.map((r, i) => (
                  <li key={i}>
                    <span className={`badge${r.matches ? '' : ' badge--warn'}`}>{r.matches ? 'Allows' : 'Not them'}</span>
                    <span>
                      {r.operation === 0 ? 'Reading' : 'Editing'}{' '}
                      {r.inherited ? `“${r.pageTitle}” and the pages under it` : 'this page'} is allowed for {r.principalName}.{' '}
                      <span className="muted small">
                        {r.matches
                          ? (r.principalType === 0 ? 'That is them.' : 'They are in it.')
                          : (r.principalType === 0 ? 'That is someone else.' : 'They are not in it.')}
                      </span>
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      )}
    </div>
  )
}
