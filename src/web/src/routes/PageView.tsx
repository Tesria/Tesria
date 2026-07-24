import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { api, ApiError, type PageDetail } from '../api/client'
import { Editor } from '../editor/Editor'
import { useSpaceContext } from './SpacePage'
import { CommentsPanel } from './panels/CommentsPanel'
import { PageLabels } from './panels/PageLabels'
import { RestrictionsPanel } from './panels/RestrictionsPanel'
import { AttachmentsPanel } from './panels/AttachmentsPanel'
import { HistoryPanel } from './panels/HistoryPanel'
import { SaveAsTemplateButton } from '../components/SaveAsTemplateButton'

type Tab = 'comments' | 'attachments' | 'history' | 'restrictions'

export function PageView() {
  const { key = '', pageId = '' } = useParams()
  const navigate = useNavigate()
  const { space, reloadTree } = useSpaceContext()
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

  if (error) return <p className="alert alert--error page-wrap">{error}</p>
  if (!page) return <p className="muted page-wrap">Loading…</p>

  return (
    <article className="page-wrap">
      <div className="row-between page-head">
        <h1>{page.title}</h1>
        <div className="page-actions">
          <Link className="btn btn--ghost" to={`/spaces/${key}/pages/${page.id}/edit`}>
            Edit
          </Link>
          <Link className="btn btn--ghost" to={`/spaces/${key}/new?parent=${page.id}`}>
            + Subpage
          </Link>
          {/* Plain links so the browser downloads the file (auth cookie is sent).
              HTML export is print-ready — use the browser's Print → Save as PDF. */}
          <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=markdown`}>
            ↓ .md
          </a>
          <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=html`}>
            ↓ .html
          </a>
          <SaveAsTemplateButton spaceId={space.id} contentJson={page.contentJson} defaultName={page.title} />
          <button type="button" className="btn btn--danger" onClick={onDelete}>
            Delete
          </button>
        </div>
      </div>
      <p className="muted small">
        Version {page.currentVersionNumber} · updated {new Date(page.updatedAt).toLocaleString()}
      </p>
      <PageLabels pageId={page.id} />

      <div className="page-body">
        <Editor value={page.contentJson} editable={false} />
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
      <p className="muted small">Space: {space.name}</p>
    </article>
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
