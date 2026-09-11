import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { api, ApiError, type PageDetail } from '../api/client'
import { Editor } from '../editor/Editor'
import { scrollToAnchor } from '../editor/headingAnchors'
import { useAuth } from '../auth/AuthContext'
import { useSpaceContext } from './SpacePage'
import { CommentsPanel } from './panels/CommentsPanel'
import { PageLabels } from './panels/PageLabels'
import { RestrictionsPanel } from './panels/RestrictionsPanel'
import { AttachmentsPanel } from './panels/AttachmentsPanel'
import { HistoryPanel } from './panels/HistoryPanel'
import { SaveAsTemplateButton } from '../components/SaveAsTemplateButton'
import { WatchToggle } from '../components/WatchToggle'
import { OverflowMenu } from '../components/OverflowMenu'

type Tab = 'comments' | 'attachments' | 'history' | 'restrictions'

export function PageView() {
  const { key = '', pageId = '' } = useParams()
  const navigate = useNavigate()
  const { hash } = useLocation()
  const { space, reloadTree } = useSpaceContext()
  const { user } = useAuth()
  const [page, setPage] = useState<PageDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('comments')

  const load = useCallback(() => {
    setError(null)
    api.pages
      .get(pageId)
      .then(setPage)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : 'Failed to load page.'))
  }, [pageId])

  useEffect(() => {
    setPage(null)
    load()
  }, [load])

  // `/pages/{id}#setup` lands on that heading once the content has rendered.
  // Looked up inside the page body only — a heading called "Root" must not
  // resolve to the app's own #root (headingAnchors.ts).
  useEffect(() => {
    if (!page || !hash) return
    const id = decodeURIComponent(hash.slice(1))
    const timer = setTimeout(() => {
      const body = document.querySelector('.page-body')
      if (body) scrollToAnchor(body, id)
    }, 50)
    return () => clearTimeout(timer)
  }, [page, hash])

  async function onDelete() {
    if (!page) return
    if (!confirm(`Move "${page.title}" and any sub-pages to the trash?`)) return
    try {
      await api.pages.remove(page.id)
      reloadTree()
      navigate(`/spaces/${key}`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the page.')
    }
  }

  async function toggleFullWidth() {
    if (!page) return
    const fullWidth = !page.fullWidth
    setPage({ ...page, fullWidth }) // optimistic — this is display metadata, not content
    try {
      await api.pages.setLayout(page.id, { fullWidth })
    } catch {
      setPage((p) => (p ? { ...p, fullWidth: !fullWidth } : p))
    }
  }

  if (error) {
    return (
      <div className="page-wrap">
        <p className="alert alert--error">{error}</p>
        {!user && (
          <p className="muted">
            This page may exist but not be public.{' '}
            <Link to="/login" state={{ from: `/spaces/${key}/pages/${pageId}` }}>Sign in</Link> to see it.
          </p>
        )}
      </div>
    )
  }
  if (!page) return <p className="muted page-wrap">Loading…</p>

  // Anonymous readers (dev-plan 5.3): the action bar collapses to Export;
  // comments only where the space allows them, read-only.
  if (!user) {
    return (
      <>
        <div className="page-actionbar">
          <div className="page-actionbar__secondary">
            <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=markdown`}>↓ Markdown</a>
            <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=html`}>↓ HTML</a>
          </div>
        </div>
        <article className={page.fullWidth ? 'page-wrap page-wrap--full' : 'page-wrap'}>
          <div className="paper">
            <div className="page-head"><h1>{page.title}</h1></div>
            <p className="muted small">Updated {new Date(page.updatedAt).toLocaleDateString()}</p>
            <PageLabels pageId={page.id} readOnly />
            <div className="page-body">
              <Editor value={page.contentJson} editable={false} />
            </div>
          </div>
          {space.publicComments && (
            <>
              <div className="tabs"><span className="tab is-active">Comments</span></div>
              <div className="tab-panel"><CommentsPanel pageId={page.id} readOnly /></div>
            </>
          )}
        </article>
      </>
    )
  }

  return (
    <>
      <div className="page-actionbar">
        <div className="page-actionbar__secondary">
          <Link className="btn btn--primary" to={`/spaces/${key}/pages/${page.id}/edit`}>
            Edit
          </Link>
          {/* Mobile only — desktop already has this in the always-visible
              sidebar (.sidebar's "+ New page"); showing it here too would
              just duplicate it right next to Edit for no reason. */}
          <Link className="btn btn--primary page-actionbar__new-subpage" to={`/spaces/${key}/new?parent=${page.id}`}>
            + New
          </Link>
          <button
            type="button"
            className="btn btn--ghost page-actionbar__fullwidth-toggle"
            onClick={toggleFullWidth}
            title={page.fullWidth ? 'Switch to normal width' : 'Switch to full width'}
          >
            {page.fullWidth ? '⤡ Normal width' : '⤢ Full width'}
          </button>
          <OverflowMenu>
            {/* Plain links so the browser downloads the file (auth cookie is sent).
                HTML export is print-ready — use the browser's Print → Save as PDF. */}
            <a className="btn" href={`/api/pages/${page.id}/export?format=markdown`}>
              ↓ Export as Markdown
            </a>
            <a className="btn" href={`/api/pages/${page.id}/export?format=html`}>
              ↓ Export as HTML
            </a>
            <WatchToggle
              watchKey={page.id}
              fetchStatus={() => api.pageWatch.status(page.id)}
              watch={() => api.pageWatch.watch(page.id)}
              unwatch={() => api.pageWatch.unwatch(page.id)}
            />
            <SaveAsTemplateButton spaceId={space.id} contentJson={page.contentJson} defaultName={page.title} />
            <button type="button" className="btn btn--danger" onClick={onDelete}>
              Delete
            </button>
          </OverflowMenu>
        </div>
      </div>
      <article className={page.fullWidth ? 'page-wrap page-wrap--full' : 'page-wrap'}>
        <div className="paper">
          <div className="page-head">
            <h1>{page.title}</h1>
          </div>
          <p className="muted small">
            Version {page.currentVersionNumber} · updated {new Date(page.updatedAt).toLocaleString()}
          </p>
          <PageLabels pageId={page.id} />

          <div className="page-body">
            <Editor value={page.contentJson} editable={false} />
          </div>
        </div>

        <div className="tabs">
          <TabButton current={tab} value="comments" onClick={setTab}>Comments</TabButton>
          <TabButton current={tab} value="attachments" onClick={setTab}>Attachments</TabButton>
          <TabButton current={tab} value="history" onClick={setTab}>History</TabButton>
          <TabButton current={tab} value="restrictions" onClick={setTab}>Restrictions</TabButton>
        </div>
        <div className="tab-panel">
          {tab === 'comments' && <CommentsPanel pageId={page.id} />}
          {tab === 'attachments' && <AttachmentsPanel pageId={page.id} />}
          {tab === 'restrictions' && <RestrictionsPanel pageId={page.id} />}
          {tab === 'history' && (
            <HistoryPanel
              pageId={page.id}
              currentVersion={page.currentVersionNumber}
              onRestored={() => {
                reloadTree()
                load()
                setTab('comments')
              }}
            />
          )}
        </div>
      </article>
    </>
  )
}

function TabButton({
  current,
  value,
  onClick,
  children,
}: {
  current: Tab
  value: Tab
  onClick: (t: Tab) => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      className={current === value ? 'tab is-active' : 'tab'}
      onClick={() => onClick(value)}
    >
      {children}
    </button>
  )
}
