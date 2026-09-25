import { useEffect, useState } from 'react'
import { api, type CertificateStatus } from '../../api/client'

/**
 * Administration → Settings → Certificate (dev-plan 14.4, the review's
 * SEC-01): the fingerprint of the certificate authority this server made for
 * itself, which people compare before a device trusts it. The server gives
 * it only on a connection nobody could have altered: this computer itself,
 * or Tailscale at its own name. Anywhere else the card says where to read it
 * instead, because a page that could be anyone's is not where people should
 * learn to look.
 */
export function CertificateCard() {
  const [status, setStatus] = useState<CertificateStatus | null>(null)
  const [copied, setCopied] = useState<string | null>(null)

  useEffect(() => {
    api.admin.certificate().then(setStatus).catch(() => setStatus(null))
  }, [])

  if (!status || !status.ownCertificate) return null

  function copy(label: string, value: string) {
    navigator.clipboard?.writeText(value).then(() => {
      setCopied(label)
      setTimeout(() => setCopied(null), 1500)
    }, () => {})
  }

  return (
    <section className="profile__section" id="certificate">
      <h2>Certificate</h2>
      <p className="muted small">
        This server makes its own certificate, so each device trusts it once, with the steps at{' '}
        <code>/trust</code>. Before trusting it, a device compares its fingerprint with this one.
      </p>
      {!status.known ? (
        <p className="muted small">Not read yet. The certificate is made the first time Tesria starts; look again in a minute.</p>
      ) : status.sha256 && status.channel ? (
        <>
          <dl className="certificate-card__prints">
            <dt>SHA-256</dt>
            <dd>
              <code className="certificate-card__print">{status.sha256}</code>
              <button type="button" className="btn btn--sm" onClick={() => copy('sha256', status.sha256!)}>
                {copied === 'sha256' ? 'Copied' : 'Copy'}
              </button>
            </dd>
            <dt>SHA-1</dt>
            <dd>
              <code className="certificate-card__print">{status.sha1}</code>
              <span className="muted small"> Only for Windows’ certificate window, which shows no other.</span>
            </dd>
          </dl>
          <p className="muted small">
            Shown because you opened Tesria {status.channel === 'server'
              ? 'on the server computer itself'
              : 'through Tailscale, whose certificate your browser has already checked'}, so nothing on the network could
            have changed it. Give it to people by a way you trust, such as a message they already know is from you.
          </p>
        </>
      ) : (
        <>
          <p className="small">
            The fingerprint is shown only where nobody could have changed this page on its way to you: on the server
            computer itself (open <code>https://localhost</code> there), or through Tailscale. This connection is
            neither.
          </p>
          <p className="muted small">
            Or read it on the server, in the Tesria folder: <code>docker compose logs app | grep -i fingerprint</code>{' '}
            (on Windows, <code>| Select-String fingerprint</code>).
          </p>
        </>
      )}
    </section>
  )
}
