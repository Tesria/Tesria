import { useEffect, useState } from 'react'
import { api, Permission, UserRole, type Invite } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { useConfirm } from '../../components/ConfirmDialog'
import { InviteWizard, type CreatedInvite } from './InviteWizard'

/**
 * Admin → Invites (dev-plan 1.4's management surface), and the "Invite
 * people" page for anyone else holding "Create invite links".
 *
 * Two rights, and the page shows what each allows: creating a link needs
 * invites.create, seeing and revoking the existing ones needs
 * invites.manage. Someone with only the first can hand out links but cannot
 * see anybody else's. Since 21.3 an invite is made with a wizard that can
 * give a role and groups, and the list says what each waiting one gives.
 */
export function AdminInvitesPage() {
  const { can } = useAuth()
  const canCreate = can(Permission.InvitesCreate)
  const canManage = can(Permission.InvitesManage)
  const { ask, dialog } = useConfirm()
  const [invites, setInvites] = useState<Invite[] | null>(null)
  // One link, or two when Tesria is also on a tailnet (the second for
  // people who reach it through Tailscale rather than this address).
  const [issued, setIssued] = useState<{ label: string | null; url: string }[] | null>(null)
  const [emailed, setEmailed] = useState<{ to: string } | { failed: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  // A new wizard, empty, for the next invite once one is made.
  const [round, setRound] = useState(0)

  async function load() {
    if (canManage) setInvites(await api.admin.invites.list())
  }

  useEffect(() => {
    load().catch(() => setError('Could not load invites.'))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canManage])

  function created(invite: CreatedInvite, emailing: boolean) {
    // Built from the current origin: the server is behind a proxy and does
    // not reliably know its own public address. The tailnet address it
    // does know, from the Tailscale sidecar.
    const here = `${window.location.origin}${invite.path}`
    setIssued(invite.tailnetUrl && invite.tailnetUrl !== here
      ? [{ label: 'At This Address', url: here }, { label: 'Through Tailscale', url: invite.tailnetUrl }]
      : [{ label: null, url: here }])
    setEmailed(!emailing ? null : invite.emailed ? { to: invite.email ?? '' } : { failed: invite.emailError ?? 'the mail server did not say why' })
    setRound((r) => r + 1)
    load().catch(() => setError('Could not load invites.'))
  }

  return (
    <>
      <p className="muted small">
        Single-use registration links: the way to add someone while public registration
        is closed. Copy the link to send it yourself, or, when this server sends email,
        have Tesria email it with a note from you. An invite can also make the person an
        administrator and give them groups, which they get when they create their account.
      </p>

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
          <p>{issued.length > 1 ? 'Invite link, shown once. Send whichever address the person can reach; both are the same invite, and it works once:' : 'Invite link, shown once:'}</p>
          {issued.map((link) => (
            <div key={link.url} className="invite-link">
              {link.label && <p className="invite-link__label">{link.label}</p>}
              <code className="admin__link">{link.url}</code>
              <button
                type="button"
                className="btn btn--ghost btn--sm"
                onClick={() => navigator.clipboard.writeText(link.url).catch(() => {})}
              >
                Copy{link.label ? ` the ${link.label === 'Through Tailscale' ? 'Tailscale' : 'Usual'} Link` : ''}
              </button>
            </div>
          ))}
          <div className="row-gap">
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => { setIssued(null); setEmailed(null) }}>
              Invite Someone Else
            </button>
          </div>
        </div>
      )}

      {/* The link replaces the wizard until it is dismissed, so it is not
          left below a fresh, empty form where nobody looks. */}
      {canCreate && !issued && <InviteWizard key={round} onCreated={created} />}

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
              <td>
                {i.email ?? <span className="muted">Anyone</span>}
                {/* What a waiting invite gives when it is used (21.3). */}
                {!i.usedAt && (i.role === UserRole.Admin || (i.groups?.length ?? 0) > 0) && (
                  <span className="muted small invite-gives">
                    {[...(i.role === UserRole.Admin ? ['Administrator'] : []), ...(i.groups ?? []).map((g) => g.name)].join(', ')}
                  </span>
                )}
              </td>
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
                      if (!await ask({ title: 'Revoke This Invite?', confirmLabel: 'Revoke the Invite', danger: true,
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
