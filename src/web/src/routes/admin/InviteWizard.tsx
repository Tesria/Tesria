import { useEffect, useState } from 'react'
import { api, ApiError, GlobalGroupId, Permission, UserRole, type InviteSpace } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { Wizard, type WizardStep } from '../../components/Wizard'
import { filterSpaces, inviteGroupIds, needsAddress, PLACE_ORDER, PLACE_WORDS, placeSummary } from './invitePlaces'

const STEPS: WizardStep[] = [
  { key: 'who', title: 'Who', blurb: 'Address and role' },
  { key: 'global', title: 'Global Groups', blurb: 'Optional' },
  { key: 'spaces', title: 'Spaces', blurb: 'Optional' },
  { key: 'review', title: 'Review and Send', blurb: '' },
]

const GLOBALS = [
  { id: GlobalGroupId.Viewers, name: 'Global Viewers', what: 'Can read every space, archived ones included.' },
  { id: GlobalGroupId.Reviewers, name: 'Global Reviewers', what: 'Can read every space, as Global Viewers can. Reviewing changes arrives with review mode.' },
]

export type CreatedInvite = Awaited<ReturnType<typeof api.admin.invites.create>>

/**
 * Inviting someone (dev-plan 21.3): who and as what, the global groups, a
 * place in each space the inviter administers, then the invite, emailed or
 * not. What it carries is checked by the server when it is made and again
 * when the account is created, so a step only offers what this person may
 * give; it says so where something is not theirs to give.
 */
export function InviteWizard({ onCreated }: { onCreated: (invite: CreatedInvite, emailing: boolean) => void }) {
  const { can } = useAuth()
  const mayAdmin = can(Permission.UsersPromoteAdmins) || can(Permission.RolesAssignTier)
  const mayGlobal = can(Permission.GroupsManage)
  const [at, setAt] = useState('who')
  const [reached, setReached] = useState(0)
  const [email, setEmail] = useState('')
  const [admin, setAdmin] = useState(false)
  const [days, setDays] = useState(7)
  const [globals, setGlobals] = useState<string[]>([])
  const [spaces, setSpaces] = useState<InviteSpace[] | null>(null)
  const [places, setPlaces] = useState<Record<string, number | undefined>>({})
  const [filter, setFilter] = useState('')
  // Emailing the invite (requested 2026-09-24): offered once an address is
  // typed and the server sends email. The token is only known when the
  // invite is made, so the email goes out then or not at all.
  const [mail, setMail] = useState<{ enabled: boolean; subject: string; message: string } | null>(null)
  const [sendEmail, setSendEmail] = useState(true)
  const [message, setMessage] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    api.admin.invites.email()
      .then((m) => { setMail(m); setMessage(m.message) })
      .catch(() => setMail(null))
    api.admin.invites.spaces().then(setSpaces).catch(() => setSpaces([]))
  }, [])

  const index = STEPS.findIndex((s) => s.key === at)
  const address = email.trim()
  const addressNeeded = needsAddress(admin, globals)
  const addressLooksWrong = address !== '' && !/^[^\s@]+@[^\s@]+$/.test(address)
  const whoOk = !addressLooksWrong && days >= 1 && days <= 90 && (!addressNeeded || address !== '')
  const offerEmail = Boolean(mail?.enabled && address)
  const emailing = offerEmail && sendEmail
  const placed = placeSummary(places, spaces ?? [])

  function go(next: string) {
    setReached((r) => Math.max(r, STEPS.findIndex((s) => s.key === next)))
    setAt(next)
    setError(null)
  }

  async function create() {
    setBusy(true)
    setError(null)
    try {
      const invite = await api.admin.invites.create({
        email: address || undefined,
        expiresInDays: days,
        role: admin ? UserRole.Admin : UserRole.Member,
        groupIds: inviteGroupIds(globals, places, spaces ?? []),
        ...(emailing ? { sendEmail: true, message } : {}),
      })
      onCreated(invite, emailing)
    } catch (err) {
      if (err instanceof ApiError && (err.fieldErrors.email || err.fieldErrors.expiresInDays)) setAt('who')
      setError(err instanceof ApiError ? err.message : 'Could not create an invite.')
      setBusy(false)
    }
  }

  const next = STEPS[index + 1]
  const actions = (
    <>
      {at === 'review'
        ? <button type="button" className="btn btn--primary" disabled={busy || !whoOk} onClick={() => void create()}>
            {busy ? 'Creating…' : emailing ? 'Create and Email Invite' : 'Create Invite'}
          </button>
        : <button type="button" className="btn btn--primary" disabled={at === 'who' && !whoOk}
            onClick={() => next && go(next.key)}>Continue</button>}
      {index > 0 && <button type="button" className="btn" disabled={busy} onClick={() => go(STEPS[index - 1].key)}>Back</button>}
    </>
  )

  const shownSpaces = filterSpaces(spaces ?? [], filter)

  return (
    <Wizard
      title="Invite Someone"
      steps={STEPS}
      at={at}
      onGo={go}
      reachable={(_, i) => i === 0 || (whoOk && i <= reached)}
      done={(k) => STEPS.findIndex((s) => s.key === k) < reached}
      actions={actions}
    >
      {error && <p className="alert alert--error">{error}</p>}

      {at === 'who' && (
        <>
          <label>
            <span>Email{addressNeeded ? '' : ' (Optional)'}</span>
            <input type="email" value={email} autoFocus onChange={(e) => setEmail(e.target.value)}
              aria-invalid={addressLooksWrong || (addressNeeded && address === '')} />
            <span className={`small${addressLooksWrong || (addressNeeded && address === '') ? ' wizard__problem' : ' muted'}`}>
              {addressLooksWrong
                ? 'That does not look like an email address.'
                : addressNeeded && address === ''
                  ? 'An invite that makes an administrator, or gives a global group, names the one address that may use it.'
                  : 'Binds the invite to one address. Leave it empty for a link anyone you give it to can use once.'}
            </span>
          </label>
          <div className="setup__cards">
            <button type="button" aria-pressed={!admin} className={`setup__card${!admin ? ' is-chosen' : ''}`}
              onClick={() => setAdmin(false)}>
              <strong>User</strong>
              <span className="muted small">Reads and writes in the spaces they are given. Most people.</span>
            </button>
            <button type="button" aria-pressed={admin} disabled={!mayAdmin}
              className={`setup__card${admin ? ' is-chosen' : ''}`} onClick={() => setAdmin(true)}>
              <strong>Administrator</strong>
              <span className="muted small">
                {mayAdmin
                  ? 'Also runs this instance: users, settings, backups. Needs your password again.'
                  : 'Your role cannot make administrators.'}
              </span>
            </button>
          </div>
          <label className="wizard__days">
            <span>Expires in (Days)</span>
            <input type="number" min={1} max={90} value={days} onChange={(e) => setDays(Number(e.target.value))} />
          </label>
        </>
      )}

      {at === 'global' && (
        mayGlobal ? (
          <>
            <p className="muted">
              Each of these can read every space. Page restrictions still apply, and drafts stay hidden. Choosing
              either asks for your password again, and every administrator is told when the account is made.
            </p>
            {GLOBALS.map((g) => (
              <label key={g.id} className="setup__check">
                <input type="checkbox" checked={globals.includes(g.id)}
                  onChange={(e) => setGlobals((now) => e.target.checked ? [...now, g.id] : now.filter((id) => id !== g.id))} />
                <span><strong>{g.name}</strong><span className="muted small">{g.what}</span></span>
              </label>
            ))}
          </>
        ) : (
          <p className="muted">
            Global Viewers and Global Reviewers can read every space. Your role cannot choose who is in them, so
            this invite gives neither. An administrator can add the person later, in Administration, Groups.
          </p>
        )
      )}

      {at === 'spaces' && (
        spaces === null ? <p className="muted">Loading…</p>
          : spaces.length === 0 ? (
            <p className="muted">
              You are not an admin of any space, so this invite cannot give a place in one. A space’s admins can add
              the person in its Permissions once they have an account.
            </p>
          ) : (
            <>
              <p className="muted">
                The spaces you are an admin of. Choose a place in any of them; the others stay as they are.
              </p>
              {spaces.length > 8 && (
                <input type="search" value={filter} placeholder="Find a space" aria-label="Find a space"
                  onChange={(e) => setFilter(e.target.value)} />
              )}
              <ul className="wizard__spaces">
                {shownSpaces.map((s) => (
                  <li key={s.id} className="wizard__space">
                    <span>{s.name} <span className="muted small">{s.key}</span></span>
                    <span className="glass-select-wrap"><select className="glass-select" aria-label={`Place in ${s.name}`}
                      value={places[s.id] ?? ''}
                      onChange={(e) => setPlaces((p) => ({ ...p, [s.id]: e.target.value === '' ? undefined : Number(e.target.value) }))}>
                      <option value="">No Place</option>
                      {PLACE_ORDER.map((role) => <option key={role} value={role}>{PLACE_WORDS[role]}</option>)}
                    </select></span>
                  </li>
                ))}
                {shownSpaces.length === 0 && <li className="muted small">No space matches.</li>}
              </ul>
            </>
          )
      )}

      {at === 'review' && (
        <>
          <dl className="wizard__summary">
            <div className="wizard__summary-row">
              <dt>For</dt>
              <dd>{address || <span className="muted">Anyone with the link, once</span>}</dd>
            </div>
            <div className="wizard__summary-row"><dt>Role</dt><dd>{admin ? 'Administrator' : 'User'}</dd></div>
            <div className="wizard__summary-row">
              <dt>Global groups</dt>
              <dd>{globals.length === 0 ? <span className="muted">None</span>
                : GLOBALS.filter((g) => globals.includes(g.id)).map((g) => g.name).join(', ')}</dd>
            </div>
            <div className="wizard__summary-row">
              <dt>Spaces</dt>
              <dd>{placed.length === 0 ? <span className="muted">None</span> : placed.join('; ')}</dd>
            </div>
            <div className="wizard__summary-row"><dt>Expires</dt><dd>In {days} {days === 1 ? 'day' : 'days'}</dd></div>
          </dl>
          {(admin || placed.length > 0 || globals.length > 0) && (
            <p className="muted small">
              These are given when the account is made, as far as you may still give them then.
            </p>
          )}
          {offerEmail && (
            <div className="invite-email">
              <label className="setup__check">
                <input type="checkbox" checked={sendEmail} onChange={(e) => setSendEmail(e.target.checked)} />
                <span>Email the Invite to {address}</span>
              </label>
              {sendEmail && (
                <>
                  <label>
                    <span>Message</span>
                    <textarea rows={8} maxLength={2000} value={message} onChange={(e) => setMessage(e.target.value)} />
                  </label>
                  <p className="muted small">
                    Subject: {mail!.subject}. Tesria adds the link below your message, with the date it
                    expires. Leave the message empty to send the usual one.
                  </p>
                </>
              )}
            </div>
          )}
          {!whoOk && <p className="alert alert--error">The first step needs finishing: {addressNeeded && !address ? 'add the address.' : 'check the address and the expiry.'}</p>}
        </>
      )}
    </Wizard>
  )
}
