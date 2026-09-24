import { type FormEvent, useEffect, useState } from 'react'
import { api, ApiError, Permission, type Invite } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { useConfirm } from '../../components/ConfirmDialog'

/**
 * Admin → Invites (dev-plan 1.4's management surface), and the "Invite
 * people" page for anyone else holding "Create invite links".
 *
 * Two rights, and the page shows what each allows: creating a link needs
 * invites.create, seeing and revoking the existing ones needs
 * invites.manage. Someone with only the first can hand out links but cannot
 * see anybody else's.
 */
export function AdminInvitesPage() {
  const { can } = useAuth()
  const canCreate = can(Permission.InvitesCreate)
  const canManage = can(Permission.InvitesManage)
  const { ask, dialog } = useConfirm()
  const [invites, setInvites] = useState<Invite[] | null>(null)
  const [email, setEmail] = useState('')
  const [days, setDays] = useState(7)
  const [issued, setIssued] = useState<string | null>(null)
  // Emailing the invite (the owner's request, 2026-09-24): offered once an
  // address is typed and the server sends email. The token is only known
  // when the invite is made, so the email goes out then or not at all.
  const [mail, setMail] = useState<{ enabled: boolean; subject: string; message: string } | null>(null)
  const [sendEmail, setSendEmail] = useState(true)
  const [message, setMessage] = useState('')
  const [emailed, setEmailed] = useState<{ to: string } | { failed: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function load() {
    if (canManage) setInvites(await api.admin.invites.list())
  }

  useEffect(() => {
    if (!canCreate) return
    api.admin.invites.email()
      .then((m) => { setMail(m); setMessage(m.message) })
      .catch(() => setMail(null))
  }, [canCreate])

  const offerEmail = Boolean(mail?.enabled && email.trim())

  useEffect(() => {
    load().catch(() => setError('Could not load invites.'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canManage])

  async function create(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const emailing = offerEmail && sendEmail
      const invite = await api.admin.invites.create({
        email: email.trim() || undefined,
        expiresInDays: days,
        ...(emailing ? { sendEmail: true, message } : {}),
      })
      // Built from the current origin: the server is behind a proxy and does
      // not reliably know its own public address.
      setIssued(`${window.location.origin}${invite.path}`)
      setEmailed(!emailing ? null : invite.emailed ? { to: invite.email ?? email.trim() } : { failed: invite.emailError ?? 'the mail server did not say why' })
      setEmail('')
      if (mail) setMessage(mail.message)
      await load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not create an invite.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="muted small">
        Single-use registration links: the way to add someone while public registration
        is closed. Copy the link to send it yourself, or, when this server sends email,
        have Tesria email it with a note from you.
      </p>

      {canCreate && <form className="form-inline invite-form" onSubmit={create}>
        <label>
          Email (optional)
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Binds the invite to one address"
          />
        </label>
        <label>
          Expires in (days)
          <input
            type="number"
            min={1}
            max={90}
            value={days}
            onChange={(e) => setDays(Number(e.target.value))}
          />
        </label>
        {offerEmail && (
          <div className="invite-email">
            <label className="admin__toggle admin__toggle--inline">
              <input type="checkbox" checked={sendEmail} onChange={(e) => setSendEmail(e.target.checked)} />
              <span>Email the invite to {email.trim()}</span>
            </label>
            {sendEmail && (
              <>
                <label>
                  Message
                  <textarea
                    rows={8}
                    maxLength={2000}
                    value={message}
                    onChange={(e) => setMessage(e.target.value)}
                  />
                </label>
                <p className="muted small">
                  Subject: {mail!.subject}. Tesria adds the link below your message, with the date it
                  expires. Leave the message empty to send the usual one.
                </p>
              </>
            )}
          </div>
        )}
        {/* After the message, so it is written before it is sent; without
            the email it takes the third column of the first row. */}
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Creating…' : offerEmail && sendEmail ? 'Create and email invite' : 'Create invite'}
        </button>
      </form>}

      {error && <p className="alert alert--error">{error}</p>}

      {emailed && 'to' in emailed && (
        <p className="alert alert--success">Invite emailed to {emailed.to}. The link is below as well, if you want to send it another way too.</p>
      )}
      {emailed && 'failed' in emailed && (
        <p className="alert alert--error">
          The invite was made, but the email was not sent: {emailed.failed}. Copy the link below and send it yourself.
        </p>
      )}
      {issued && (
        <div className="admin__notice">
          <p>Invite link, shown once:</p>
          <code className="admin__link">{issued}</code>
          <div className="row-gap">
            <button
              type="button"
              className="btn btn--ghost btn--sm"
              onClick={() => navigator.clipboard.writeText(issued).catch(() => {})}
            >
              Copy
            </button>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => { setIssued(null); setEmailed(null) }}>
              Dismiss
            </button>
          </div>
        </div>
      )}

      {!canManage && (
        <p className="muted small">
          Your role can create invite links but not list or revoke them, so copy each link when it
          is shown. An administrator can revoke one from Administration → Invites.
        </p>
      )}

      {canManage && <table className="admin-table">
        <thead>
          <tr>
            <th>For</th>
            <th>Status</th>
            <th>Expires</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {invites?.map((i) => (
            <tr key={i.id}>
              <td>{i.email ?? <span className="muted">Anyone</span>}</td>
              <td>
                {i.usedAt
                  ? <>
                      <span className="badge">used</span>{' '}
                      <span className="muted small">
                        {i.usedByName ? `by ${i.usedByName}, ` : ''}{new Date(i.usedAt).toLocaleDateString()}
                      </span>
                    </>
                  : new Date(i.expiresAt) < new Date()
                    ? <span className="badge badge--danger">expired</span>
                    : 'Unused'}
              </td>
              <td className="muted small">{new Date(i.expiresAt).toLocaleDateString()}</td>
              <td>
                {!i.usedAt && (
                  <button
                    type="button"
                    className="link-btn link-btn--danger"
                    onClick={async () => {
                      // Asked first (dev-plan 15.4): the link may already be in someone's inbox.
                      if (!await ask({ title: 'Revoke this invite?', confirmLabel: 'Revoke the invite', danger: true,
                        body: <p>The link stops working. Anyone you sent it to will need a new one.</p> })) return
                      api.admin.invites.revoke(i.id).then(load).catch(() => setError('Could not revoke that invite.'))
                    }}
                  >
                    Revoke
                  </button>
                )}
              </td>
            </tr>
          ))}
          {invites?.length === 0 && (
            <tr><td colSpan={4} className="muted">No invites yet.</td></tr>
          )}
        </tbody>
      </table>}
      {dialog}
    </>
  )
}
