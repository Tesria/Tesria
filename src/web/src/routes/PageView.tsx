import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { api, ApiError, type PageDetail, Permission } from '../api/client'
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
import { SpaceBreadcrumb } from '../components/SpaceBreadcrumb'
import { useConfirm } from '../components/ConfirmDialog'
import { noteOpenPage, notePageVisit } from '../onboarding/signals'
import { useTitlePage } from '../components/DocumentTitle'
import { ResolvedCommentStyles } from '../components/ResolvedCommentStyles'
import { MoveCopyDialog } from '../components/MoveCopyDialog'
import { PageEmoji } from '../components/PageEmoji'

type Tab = 'comments' | 'attachments' | 'history' | 'restrictions'

export function PageView() {
  const { key = '', pageId = '' } = useParams()
  const navigate = useNavigate()
  const { hash } = useLocation()
  const { space, tree, reloadTree } = useSpaceContext()
  const { user, can } = useAuth()
  const [page, setPage] = useState<PageDetail | null>(null)
  useTitlePage(page?.title)
  const [error, setError] = useState<string | null>(null)
  const [tab, setTab] = useState<Tab>('comments')
  const { ask, dialog } = useConfirm()
  const [moveCopy, setMoveCopy] = useState<null | 'move' | 'copy'>(null)

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

  // Signals for the tips (dev-plan 10.3): which page is open, who wrote it,
  // and how many times this person has been here.
  useEffect(() => {
    if (!page) return
    noteOpenPage({ id: page.id, createdById: page.createdById })
    notePageVisit(page.id)
    return () => noteOpenPage(null)
  }, [page])

  // `/pages/{id}#setup` lands on that heading once the content has rendered.
  // Looked up inside the page body only: a heading called "Root" must not
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
    // The trash is reversible, so this asks plainly rather than as a danger.
    const ok = await ask({
      title: `Move ${page.title} to the trash?`,
      confirmLabel: 'Move to the trash',
      body: <p>Any sub-pages go with it. Anyone who can edit it can restore the lot from the space&rsquo;s Trash.</p>,
    })
    if (!ok) return
    try {
      await api.pages.remove(page.id)
      reloadTree()
      navigate(`/spaces/${key}`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not delete the page.')
    }
  }

  /** Sets the page's emoji (dev-plan 15.7), and redraws the tree, which shows it too. */
  async function setEmoji(emoji: string | null) {
    if (!page) return
    await api.pages.setEmoji(page.id, emoji)
    setPage({ ...page, emoji })
    reloadTree()
  }

  async function toggleFullWidth() {
    if (!page) return
    const fullWidth = !page.fullWidth
    setPage({ ...page, fullWidth }) // optimistic: this is display metadata, not content
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

  // Only the downloads this space allows (dev-plan 12.3). The server refuses
  // the rest anyway; offering a button that can only fail is worse than none.
  const spaceAllows = space.exports ?? { markdown: true, html: true, pdf: true, site: true, pack: true }
  // And only if your role may export at all: without the right each link
  // opened a raw error page (found 2026-09-23). Anonymous readers are judged
  // by the server alone, since a signed-out browser holds no rights list.
  const mayExport = !user || can(Permission.PagesExport)
  const allows = {
    markdown: mayExport && spaceAllows.markdown,
    html: mayExport && spaceAllows.html,
    pdf: mayExport && spaceAllows.pdf,
  }

  // Anonymous readers (dev-plan 5.3): the action bar collapses to Export;
  // comments only where the space allows them, read-only.
  if (!user) {
    return (
      <>
        <div className="page-actionbar">
          <div className="page-actionbar__secondary">
            {allows.markdown && <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=markdown`}>↓ Markdown</a>}
            {allows.html && <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=html`}>↓ HTML</a>}
            {allows.pdf && <a className="btn btn--ghost" href={`/api/pages/${page.id}/export?format=pdf`}>↓ PDF</a>}
          </div>
        </div>
        <div className="page-column">
        <SpaceBreadcrumb space={space} tree={tree} />
        <article className={page.fullWidth ? 'page-wrap page-wrap--full' : 'page-wrap'}>
          <div className="paper">
            <div className="page-head"><PageEmoji emoji={page.emoji} canEdit={false} onChange={async () => {}} /><h1>{page.title}</h1></div>
            <p className="muted small">Updated {new Date(page.updatedAt).toLocaleDateString()}</p>
            <PageLabels pageId={page.id} readOnly />
            <ResolvedCommentStyles pageId={page.id} />
          <div className="page-body">
              <Editor value={page.contentJson} editable={false} getPageId={() => Promise.resolve(page.id)} />
            </div>
          </div>
          {space.publicComments && (
            <>
              <div className="tabs"><span className="tab is-active">Comments</span></div>
              <div className="tab-panel"><CommentsPanel pageId={page.id} readOnly /></div>
            </>
          )}
        </article>
        </div>
      </>
    )
  }

  return (
    <>
      <div className="page-actionbar">
        <div className="page-actionbar__secondary">
          {page.canEdit !== false && (
            <>
              <Link className="btn btn--primary" to={`/spaces/${key}/pages/${page.id}/edit`}>
                Edit
              </Link>
              {/* Mobile only: desktop already has this in the always-visible
                  sidebar (.sidebar's "+ New page"); showing it here too would
                  just duplicate it right next to Edit for no reason. */}
              <Link className="btn btn--primary page-actionbar__new-subpage" to={`/spaces/${key}/new?parent=${page.id}`}>
                + New
              </Link>
            </>
          )}
          <button
            type="button"
            className="btn btn--ghost page-actionbar__fullwidth-toggle"
            onClick={toggleFullWidth}
            title={page.fullWidth ? 'Switch to normal width' : 'Switch to full width'}
          >
            {page.fullWidth ? '⤡ Normal width' : '⤢ Full width'}
          </button>
          <OverflowMenu>
            {/* Plain links so the browser downloads the file (auth cookie is sent). */}
            {allows.markdown && (
              <a className="btn" href={`/api/pages/${page.id}/export?format=markdown`}>
                ↓ Export as Markdown
              </a>
            )}
            {allows.html && (
              <a className="btn" href={`/api/pages/${page.id}/export?format=html`}>
                ↓ Export as HTML
              </a>
            )}
            {/* Rendered by the PDF sidecar (dev-plan 8.1). Where no sidecar
                is configured the API answers 503 with the "print the HTML"
                advice, which is what this used to be. */}
            {allows.pdf && (
              <a className="btn" href={`/api/pages/${page.id}/export?format=pdf`}>
                ↓ Export as PDF
              </a>
            )}
            <WatchToggle
              watchKey={page.id}
              fetchStatus={() => api.pageWatch.status(page.id)}
              watch={() => api.pageWatch.watch(page.id)}
              unwatch={() => api.pageWatch.unwatch(page.id)}
            />
            <SaveAsTemplateButton spaceId={space.id} contentJson={page.contentJson} defaultName={page.title} />
            {page.canEdit !== false && (
              <button type="button" className="btn" data-menu-close onClick={() => setMoveCopy('move')}>Move…</button>
            )}
            <button type="button" className="btn" data-menu-close onClick={() => setMoveCopy('copy')}>Copy…</button>
            {/* Hidden when the role does not allow it; the server refuses it
                either way (dev-plan 11.1). */}
            {(can(Permission.PagesDeleteAny)
              || (can(Permission.PagesDeleteOwn) && page.createdById === user?.id)) && (
              <button type="button" className="btn btn--danger" onClick={onDelete}>
                Delete
              </button>
            )}
          </OverflowMenu>
        </div>
      </div>
      {/* Below the action bar, not above it: the bar is the top edge of the
          page surface and the breadcrumb belongs with the content. SpacePage
          suppresses its own copy on this route. */}
      {/* The size container for the content (container queries, 100cqw in
          the full-width layout rules) starts here, below the sticky bar:
          WebKit has been seen to lose position: sticky on a descendant of a
          size container, which is how the bar came to scroll away on iOS. */}
      <div className="page-column">
      <SpaceBreadcrumb space={space} tree={tree} />
      <article className={page.fullWidth ? 'page-wrap page-wrap--full' : 'page-wrap'}>
        <div className="paper">
          <div className="page-head">
            <PageEmoji emoji={page.emoji} canEdit={page.canEdit !== false} onChange={setEmoji} />
            <h1>{page.title}</h1>
          </div>
          <p className="muted small">
            Version {page.currentVersionNumber} · updated {new Date(page.updatedAt).toLocaleString()}
          </p>
          <PageLabels pageId={page.id} />

          <ResolvedCommentStyles pageId={page.id} />
          <div className="page-body">
            <Editor value={page.contentJson} editable={false} getPageId={() => Promise.resolve(page.id)} />
          </div>
        </div>

        <div className="tabs">
          <TabButton current={tab} value="comments" onClick={setTab}>Comments</TabButton>
          <TabButton current={tab} value="attachments" onClick={setTab}>Attachments</TabButton>
          <TabButton current={tab} value="history" onClick={setTab}>History</TabButton>
          <TabButton current={tab} value="restrictions" onClick={setTab}>Restrictions</TabButton>
        </div>
        <div className="tab-panel">
          {tab === 'comments' && <CommentsPanel pageId={page.id} canEdit={page.canEdit === true} />}
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
      </div>

      {moveCopy && (
        <MoveCopyDialog
          mode={moveCopy}
          pageId={page.id}
          pageTitle={page.title}
          spaceId={space.id}
          onClose={() => setMoveCopy(null)}
          onDone={({ spaceKey, pageId: target }) => {
            setMoveCopy(null)
            reloadTree()
            navigate(`/spaces/${spaceKey}/pages/${target}`)
          }}
        />
      )}
      {dialog}
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
