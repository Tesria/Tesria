import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useAuth } from '../auth/AuthContext'
import { Clip } from '../components/Clip'

/**
 * The welcome tour (dev-plan 10.3): five screens, each a clip from the
 * onboarding media beside three sentences.
 *
 * Leaving it at all counts as skipping, so somebody who closes the tab is not
 * shown it again on their next visit. Finishing it is recorded separately,
 * and the profile can reopen it either way.
 */

type Screen = {
  clip: string
  title: string
  lines: string[]
}

const SCREENS: Screen[] = [
  {
    clip: 'spaces',
    title: 'Spaces and pages',
    lines: [
      'A space is a home for related pages: a team, a project, a handbook.',
      'Pages nest inside each other as deep as you like.',
      'The tree on the left is the map, and it is always there.',
    ],
  },
  {
    clip: 'new-page',
    title: 'Writing',
    lines: [
      'New page, a title, and start typing. Publish when it is worth reading.',
      'The toolbar is along the top, and selecting text brings up its own.',
      'A page you have not published yet is a draft, and drafts are yours alone.',
    ],
  },
  {
    clip: 'inline-comment',
    title: 'Working together',
    lines: [
      'Two people can edit the same page at once and see each other do it.',
      'Select any text to comment on exactly that, rather than the whole page.',
      'Type @ to bring someone in, and Watch a page to hear when it changes.',
    ],
  },
  {
    clip: 'search',
    title: 'Finding things',
    lines: [
      'Search covers everything you are allowed to see, and nothing else.',
      'It looks at titles, the text itself, and labels.',
      'Labels group pages across spaces, which is how a topic stays together.',
    ],
  },
  {
    clip: 'profile',
    title: 'You',
    lines: [
      'Your profile holds your avatar, your password and two-factor.',
      'It also decides which emails this place is allowed to send you.',
      'And it is where these tips can be turned off, or turned back on.',
    ],
  },
]

export function WelcomePage() {
  const { refresh } = useAuth()
  const navigate = useNavigate()
  const [at, setAt] = useState(0)
  const [tips, setTips] = useState(true)
  const [leaving, setLeaving] = useState(false)

  const screen = SCREENS[at]
  const last = at === SCREENS.length - 1

  // Leaving the tour any other way counts as skipping it: closing the tab is
  // an answer, and it should not be asked again.
  useEffect(() => {
    const onLeave = () => {
      if (leaving) return
      // A keepalive fetch rather than sendBeacon: a beacon can only POST and
      // cannot carry the X-Requested-With header the CSRF check wants, so it
      // was refused and the tour came back every session (2026-09-23).
      void fetch('/api/auth/me/onboarding', {
        method: 'PUT',
        keepalive: true,
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'Tesria' },
        body: JSON.stringify({ tourSkipped: true }),
      }).catch(() => {})
    }
    window.addEventListener('pagehide', onLeave)
    return () => window.removeEventListener('pagehide', onLeave)
  }, [leaving])

  async function leave(completed: boolean) {
    setLeaving(true)
    try {
      await api.auth.updateOnboarding(
        completed ? { tourCompleted: true, tipsEnabled: tips } : { tourSkipped: true },
      )
      await refresh()
    } catch {
      // The tour is not worth failing over; go on to the app either way.
    }
    navigate('/spaces', { replace: true })
  }

  return (
    <div className="tour">
      <div className="tour__card">
        <Clip name={screen.clip} className="tour__clip" />

        <div className="tour__body">
          <p className="tour__count muted small">Step {at + 1} of {SCREENS.length}</p>
          <h1>{screen.title}</h1>
          {screen.lines.map((line) => <p key={line}>{line}</p>)}

          {last && (
            <label className="tour__tips">
              <input type="checkbox" checked={tips} onChange={(e) => setTips(e.target.checked)} />
              Show me tips as I go
            </label>
          )}

          <div className="tour__actions">
            {at > 0 && (
              <button type="button" className="btn btn--ghost" onClick={() => setAt(at - 1)}>Back</button>
            )}
            {last ? (
              <button type="button" className="btn btn--primary" onClick={() => leave(true)}>Done</button>
            ) : (
              <button type="button" className="btn btn--primary" onClick={() => setAt(at + 1)}>Next</button>
            )}
            <button type="button" className="link-btn tour__skip" onClick={() => leave(false)}>
              Skip the tour
            </button>
          </div>

          <ol className="tour__dots" aria-hidden="true">
            {SCREENS.map((s, i) => (
              <li key={s.clip} className={i === at ? 'is-at' : i < at ? 'is-past' : ''} />
            ))}
          </ol>
        </div>
      </div>
    </div>
  )
}
