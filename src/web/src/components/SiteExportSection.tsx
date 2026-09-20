import { useState } from 'react'
import { api, ApiError } from '../api/client'

/**
 * Space settings → Export as a site (dev-plan 12.2).
 *
 * The audience is the only real choice, and it is the one that decides
 * whether a private page can end up on the internet, so it is a choice rather
 * than a setting buried somewhere: the wording says what each option renders
 * as, not what it is called.
 */
export function SiteExportSection({ spaceKey }: { spaceKey: string }) {
  const [audience, setAudience] = useState<'anonymous' | 'me'>('anonymous')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run() {
    setBusy(true)
    setError(null)
    try {
      const zip = await api.spaces.exportSite(spaceKey, audience)
      // A download rather than a navigation: the response is a file, and the
      // page should stay where it is.
      const url = URL.createObjectURL(zip)
      const link = document.createElement('a')
      link.href = url
      link.download = `${spaceKey.toLowerCase()}-site.zip`
      link.click()
      URL.revokeObjectURL(url)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The site could not be built.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <p className="muted small">
        Every page as a static HTML file, in a zip: no server, no editor, nothing
        to sign in to. Unpack it onto Cloudflare Pages, GitHub Pages or any static
        host and it reads like the wiki does, light and dark included.
      </p>
      {error && <p className="alert alert--error">{error}</p>}

      <div className="setup__cards">
        <button type="button" className={`setup__card${audience === 'anonymous' ? ' is-chosen' : ''}`}
          onClick={() => setAudience('anonymous')}>
          <strong>As the public sees it</strong>
          <span className="muted small">
            Only what a reader with no account can already read. The space has to be
            published. Nothing private can get in by accident, whatever you can see.
          </span>
        </button>
        <button type="button" className={`setup__card${audience === 'me' ? ' is-chosen' : ''}`}
          onClick={() => setAudience('me')}>
          <strong>As me</strong>
          <span className="muted small">
            Everything you can read, including restricted pages. For a site you will
            put behind your own access control.
          </span>
        </button>
      </div>

      <div className="row-gap" style={{ marginTop: '0.75rem' }}>
        <button type="button" className="btn btn--primary" disabled={busy} onClick={run}>
          {busy ? 'Building the site…' : 'Export as a site'}
        </button>
      </div>
      <p className="muted small">
        A page takes about a second to render, so a large space takes a minute or
        two. Live blocks are frozen as they were at the moment of export.
      </p>
    </>
  )
}
