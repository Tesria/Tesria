import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/AuthContext'

/**
 * Profile → Tour and tips (dev-plan 10.3). Everything the onboarding does to
 * somebody can be turned off or asked for again from here, which is what
 * makes it acceptable to do any of it uninvited.
 */
export function TourAndTipsSection() {
  const { user, refresh } = useAuth()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const enabled = user?.onboarding?.tipsEnabled ?? true
  const dismissed = user?.onboarding?.dismissedTips?.length ?? 0

  async function run(work: () => Promise<unknown>, done: string) {
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      await work()
      await refresh()
      setStatus(done)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That did not save.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      <label className="setup__check">
        <input
          type="checkbox"
          checked={enabled}
          disabled={busy}
          onChange={(e) => run(
            () => api.auth.updateOnboarding({ tipsEnabled: e.target.checked }),
            e.target.checked ? 'Tips are on.' : 'Tips are off.',
          )}
        />
        <span>
          <strong>Show tips as I go</strong>
          <span className="muted small">
            One at a time, at most three a day, and each one only once.
          </span>
        </span>
      </label>

      <div className="row-gap" style={{ marginTop: '0.75rem' }}>
        <button
          type="button"
          className="btn btn--ghost"
          disabled={busy}
          onClick={async () => {
            await run(() => api.auth.updateOnboarding({ resetTour: true }), '')
            // sessionStorage marks the tour as offered once per tab; asking
            // for it again has to clear that or the gate will not act.
            try { sessionStorage.removeItem('tesria-tour-offered') } catch { /* nothing */ }
            navigate('/welcome')
          }}
        >
          Show the tour again
        </button>
        <button
          type="button"
          className="btn btn--ghost"
          disabled={busy || dismissed === 0}
          title={dismissed === 0 ? 'Nothing has been dismissed yet' : undefined}
          onClick={() => run(
            () => api.auth.updateOnboarding({ resetTips: true }),
            'Dismissed tips will appear again.',
          )}
        >
          Reset dismissed tips{dismissed > 0 ? ` (${dismissed})` : ''}
        </button>
      </div>
    </>
  )
}
