import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError, type AccessPersonRef, type AdminSpaceAccess } from '../api/client'
import { levelWord } from './adminAccess'
import { everyoneSummary } from './spaceAccess'

/**
 * One space's access in full, opened from Admin, Spaces (dev-plan 21.5): in
 * the order the check makes it, so it reads as the answer to "who can get
 * in, and why". Everyone signed in, the public, the global groups, the
 * space's own groups with their people, other grants, then the page
 * restrictions that narrow all of it. Get Access lives here, beside what it
 * would change.
 */
export function SpaceAccessPanel({ spaceKey, refresh = 0, onClose, onRecover }: {
  spaceKey: string
  /** Bumped by the page when the list reloads (a review decided), so the panel never shows what was. */
  refresh?: number
  onClose: () => void
  /** Get Access; the page asks first and says what happened. */
  onRecover: (key: string, name: string) => Promise<void>
}) {
  const [access, setAccess] = useState<AdminSpaceAccess | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [version, setVersion] = useState(0)

  useEffect(() => {
    let live = true
    api.admin.spaces.access(spaceKey)
      .then((a) => { if (live) { setAccess(a); setError(null) } })
      .catch((err: unknown) => { if (live) setError(err instanceof ApiError ? err.message : 'Could not load who has access.') })
    return () => { live = false }
  }, [spaceKey, version, refresh])

  return (
    <section className="card access-panel" aria-label={`Who has access to ${access?.name ?? spaceKey}`}>
      <div className="access-panel__head">
        <h2>{access ? <>Who Has Access to {access.name}</> : 'Who Has Access'}</h2>
        <button type="button" className="btn btn--ghost btn--sm" onClick={onClose}>Close</button>
      </div>
      {error && <p className="alert alert--error">{error}</p>}
      {!access && !error && <p className="muted">Loading…</p>}
      {access && (
        <>
          <p className="muted small">
            Someone gets in if any line below lets them. Being a Tesria administrator does not, by itself.
          </p>

          <h3>Everyone signed in{access.signedInAccounts > 0 && ` (${access.signedInAccounts} account${access.signedInAccounts === 1 ? '' : 's'})`}</h3>
          <p>
            <strong className={access.everyoneAccess === 2 ? 'access-panel__warn' : undefined}>
              {access.everyoneAccess == null ? 'No access' : levelWord(access.everyoneAccess)}
            </strong>
            {access.everyoneAccess === 2 && !access.everyoneAdminConfirmed && (
              <> <span className="badge badge--warning">nobody chose this</span></>
            )}
          </p>
          <p className="muted small">{everyoneSummary(access.everyoneAccess)}</p>
          {access.publiclyReadable && <p><span className="badge badge--public">public</span> Anyone on the internet can read it, without an account.</p>}
          {access.global.length > 0 && (
            <p className="small">
              {access.global.map((g) => `${g.name} (${g.members})`).join(' and ')} can read it, as they can every space.
            </p>
          )}

          <h3>This space’s groups</h3>
          <ul className="access-panel__groups">
            {access.groups.map((g) => (
              <li key={g.groupId}>
                <span className="access-panel__group">{g.name}</span>{' '}
                <span className="muted small">{levelWord(g.level)}</span>
                <div className="access-panel__people">
                  {g.members.length === 0 ? <span className="muted small">Nobody</span> : g.members.map((m) => <PersonChip key={m.id} person={m} />)}
                </div>
              </li>
            ))}
          </ul>
          {!access.hasExplicitAdmin && (
            <p className="small">
              Nobody administers it explicitly, so nobody can lift its page restrictions or choose its Admins
              {access.everyoneAccess !== 2 && ', and nobody can manage it without Get Access'}.
            </p>
          )}

          {access.other.length > 0 && (
            <>
              <h3>Other access</h3>
              <ul className="access-panel__groups">
                {access.other.map((o) => (
                  <li key={o.principalId}>
                    <span className="access-panel__group">{o.name}</span>{' '}
                    <span className="muted small">
                      {levelWord(o.level)}
                      {o.kind === 'group' && o.members != null && ` · ${o.members} member${o.members === 1 ? '' : 's'}`}
                      {o.kind === 'person' && ' · by name'}
                      {o.kind === 'person' && !o.active && ' · suspended'}
                    </span>
                  </li>
                ))}
              </ul>
            </>
          )}

          {access.restrictedPages > 0 && (
            <p className="small">
              {access.restrictedPages} page{access.restrictedPages === 1 ? ' is' : 's are'} restricted to particular people,
              which narrows all of the above for {access.restrictedPages === 1 ? 'that page and its children' : 'those pages and their children'}.
              That binds its administrators too; its explicit administrators may lift a restriction, which tells the page’s author.
            </p>
          )}

          {access.createdBy && (
            <p className="muted small">
              Created by {access.createdBy.displayName}. Creating a space gives nothing by itself: since 0.9 its creator starts in its Admins group.
            </p>
          )}

          <div className="row-gap access-panel__actions">
            {access.youCanAdminister && (
              <Link className="btn btn--ghost btn--sm" to={`/spaces/${encodeURIComponent(access.key)}/settings/permissions`}>
                Open Its Permissions
              </Link>
            )}
            {!access.youAreExplicitAdmin && (
              <button type="button" className="btn btn--ghost btn--sm"
                onClick={() => void onRecover(access.key, access.name).then(() => setVersion((v) => v + 1))}>
                Get Access
              </button>
            )}
          </div>
        </>
      )}
    </section>
  )
}

function PersonChip({ person }: { person: AccessPersonRef }) {
  return (
    <span className="access-panel__person" title={person.email ?? undefined}>
      {person.displayName}
      {!person.active && <span className="badge" title="Suspended accounts keep their groups but get nothing from them">suspended</span>}
    </span>
  )
}
