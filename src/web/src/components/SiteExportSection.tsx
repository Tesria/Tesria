import { useState } from 'react'
import { ExportJobView } from './ExportJobView'
import { useSpaceExport } from './useSpaceExport'

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
  const exporting = useSpaceExport(spaceKey, 'site')

  return (
    <>
      <p className="muted small">
        Every page as a static HTML file, in a zip: no server, no editor, nothing
        to sign in to. Unpack it onto Cloudflare Pages, GitHub Pages or any static
        host and it reads like the wiki does, light and dark included. It also
        works straight from your computer: unzip it and open index.html.
      </p>
      {exporting.error && <p className="alert alert--error">{exporting.error}</p>}

      <div className="setup__cards">
        <button type="button" className={`setup__card${audience === 'anonymous' ? ' is-chosen' : ''}`}
          onClick={() => setAudience('anonymous')}>
          <strong>As the Public Sees It</strong>
          <span className="muted small">
            Only what a reader with no account can already read. The space has to be
            published. Nothing private can get in by accident, whatever you can see.
          </span>
        </button>
        <button type="button" className={`setup__card${audience === 'me' ? ' is-chosen' : ''}`}
          onClick={() => setAudience('me')}>
          <strong>As Me</strong>
          <span className="muted small">
            Everything you can read, including restricted pages. For a site you will
            put behind your own access control.
          </span>
        </button>
      </div>

      <div className="row-gap" style={{ marginTop: '0.75rem' }}>
        <button type="button" className="btn btn--primary" disabled={exporting.preparing}
          onClick={() => void exporting.start({ audience })}>
          {exporting.preparing ? 'Preparing the Site…' : 'Prepare the Site'}
        </button>
      </div>
      {exporting.latest && <ExportJobView job={exporting.latest} />}
      <p className="muted small">
        A page takes about a second to render, so a large space takes a minute or
        two. It keeps going if you leave this page: when it is ready, download it
        here or from <strong>Downloads</strong> in your notifications (the bell). Live
        blocks are frozen as they were at the moment of export.
      </p>
    </>
  )
}
