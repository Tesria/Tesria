import { useEffect, useRef, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { api, ApiError, type PageDetail, type Space } from '../api/client'
import { Editor } from '../editor/Editor'
import { publishReady, waitForSettled } from '../export/ready'

/**
 * The page an export is captured from (dev-plan 12.1).
 *
 * The reason exports used to look nothing like the page is that they were
 * rendered a second time, by a second renderer, against a fifteen-line
 * stylesheet. This route removes the second renderer: it is the same
 * `<Editor editable={false}>` in the same `.paper` under the same
 * `index.css` that the reading view uses, with the application's furniture
 * taken away. What a reader sees is what is captured, because it is the same
 * code.
 *
 * It is not linked from anywhere. The sidecar loads it with a render token;
 * a person who reaches it sees their own page with no chrome, which is
 * harmless.
 */
export function ExportPage() {
  const { id = '' } = useParams()
  const [params] = useSearchParams()
  const chrome = params.get('chrome')
  const [page, setPage] = useState<PageDetail | null>(null)
  const [space, setSpace] = useState<Space | null>(null)
  const [failed, setFailed] = useState<string | null>(null)
  const root = useRef<HTMLDivElement>(null)

  // A PDF is paper: it is light, whatever the capturing browser or the
  // account would otherwise prefer. The site export (12.2) overrides this
  // again from its own script, which is why the attribute is set rather than
  // the stylesheet being changed.
  useEffect(() => {
    const html = document.documentElement
    const previous = html.getAttribute('data-theme')
    if (chrome !== 'site') html.setAttribute('data-theme', 'light')
    return () => {
      if (previous === null) html.removeAttribute('data-theme')
      else html.setAttribute('data-theme', previous)
    }
  }, [chrome])

  useEffect(() => {
    let cancelled = false
    api.pages.get(id)
      .then(async (p) => {
        if (cancelled) return
        setPage(p)
        // The space is only needed for the site chrome's breadcrumb.
        if (chrome === 'site') {
          try { setSpace(await api.spaces.get(p.spaceId)) } catch { /* breadcrumb is optional */ }
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return
        // A capture of a page the token may not see must end, not hang: the
        // sidecar is waiting on the ready attribute either way.
        setFailed(err instanceof ApiError && err.status === 404
          ? 'That page does not exist, or this export may not see it.'
          : 'That page could not be loaded.')
      })
    return () => { cancelled = true }
  }, [id, chrome])

  // The signal the sidecar waits for. Published exactly once, whatever
  // happens, including when the page failed to load at all.
  useEffect(() => {
    if (failed) { publishReady('error'); return }
    if (!page || !root.current) return
    let cancelled = false
    void waitForSettled(root.current).then((state) => {
      if (!cancelled) publishReady(state)
    })
    return () => { cancelled = true }
  }, [page, failed])

  if (failed) {
    return <div className="export export--failed"><p>{failed}</p></div>
  }
  if (!page) return <div className="export" />

  return (
    <div className="export" ref={root}>
      <article className="paper paper--export">
        {chrome === 'site' && space && (
          <nav className="export__crumb"><span>{space.name}</span></nav>
        )}
        <h1>{page.title}</h1>
        <Editor value={page.contentJson} editable={false} getPageId={() => Promise.resolve(page.id)} />
      </article>
    </div>
  )
}
