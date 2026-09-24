import { useEffect, useState } from 'react'
import { api, type TailscaleStatus } from '../../api/client'

/**
 * Administration → Settings → Tailscale (dev-plan 19.1): whether the
 * optional Tailscale sidecar is running, the address it serves Tesria at on
 * the tailnet, and when its device key expires. Read-only: the sidecar is
 * set up in .env and started with its Compose profile, which the docs
 * page walks through.
 */
export function TailscaleCard() {
  const [status, setStatus] = useState<TailscaleStatus | null>(null)

  useEffect(() => {
    api.admin.tailscale().then(setStatus).catch(() => setStatus(null))
  }, [])

  const keyDays = status?.keyExpiry ? (new Date(status.keyExpiry).getTime() - Date.now()) / 86_400_000 : null

  return (
    <section className="profile__section" id="tailscale">
      <h2 className="tailscale-card__title">
        <span className="visually-hidden">Tailscale</span>
        <img className="tailscale-card__logo tailscale-card__logo--light" src="/brands/tailscale/tailscale-logo-black.svg" alt="" />
        <img className="tailscale-card__logo tailscale-card__logo--dark" src="/brands/tailscale/tailscale-logo-white.svg" alt="" />
      </h2>
      {!status || !status.configured ? (
        <>
          <p className="muted small">
            Optional. If your devices already use Tailscale, Tesria can join your tailnet and be reached from
            anywhere at a <code>ts.net</code> address with a real certificate, without opening it to the internet.
          </p>
          <p className="muted small">
            Not running. Set it up with the Tesria docs’ <em>Reaching Tesria from anywhere with Tailscale</em>.
          </p>
        </>
      ) : (
        <>
          <ul className="tailscale-card__facts">
            <li>
              <strong>Status:</strong>{' '}
              {status.stale
                ? <span className="badge badge--danger">not reporting</span>
                : status.state === 'Running' && status.online
                  ? <span className="badge badge--ok">connected</span>
                  : <span className="badge badge--danger">{status.state === 'NeedsLogin' ? 'needs a new auth key' : status.state ?? 'unknown'}</span>}
              {status.checkedAt && <span className="muted small"> checked {new Date(status.checkedAt).toLocaleTimeString()}</span>}
            </li>
            {status.address && (
              <li>
                <strong>Address on your tailnet:</strong>{' '}
                <a href={status.address} target="_blank" rel="noreferrer">{status.address}</a>
              </li>
            )}
            <li>
              <strong>Device key:</strong>{' '}
              {status.keyExpiry
                ? <>expires {new Date(status.keyExpiry).toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric' })}</>
                : 'does not expire'}
            </li>
            {status.version && <li className="muted small">Tailscale {status.version}</li>}
          </ul>
          {status.stale && (
            <p className="alert alert--error">
              The Tailscale container has stopped reporting. Start it again with{' '}
              <code>docker compose --profile tailscale up -d</code>.
            </p>
          )}
          {status.keyExpiry && (
            <p className={keyDays !== null && keyDays < 14 ? 'alert alert--error' : 'alert alert--warning'}>
              When the device key expires, Tesria drops off your tailnet until it is signed in again with a new
              auth key. To keep it connected, open the Tailscale admin console, find <strong>{status.address?.replace('https://', '').split('.')[0] ?? 'tesria'}</strong>{' '}
              under <strong>Machines</strong>, and choose <strong>Disable key expiry</strong> from its menu.
            </p>
          )}
        </>
      )}
      <p className="muted small tailscale-card__trademark">
        Tailscale and the Tailscale logo are trademarks of Tailscale Inc. Tesria is not affiliated with or endorsed by Tailscale.
      </p>
    </section>
  )
}
