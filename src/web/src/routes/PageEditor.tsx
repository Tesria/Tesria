import { type FormEvent, useEffect, useRef, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import type { Editor as TiptapEditor } from '@tiptap/react'
import { api, ApiError, type CollabToken, type PageTemplate } from '../api/client'
import { Editor } from '../editor/Editor'
import { CollaborativeEditor } from '../editor/CollaborativeEditor'
import { Toolbar } from '../editor/Toolbar'
import { useAuth } from '../auth/AuthContext'
import { useSpaceContext } from './SpacePage'
import { SpaceBreadcrumb } from '../components/SpaceBreadcrumb'

const EMPTY_DOC = '{"type":"doc","content":[]}'

export function PageEditor() {
  const { key = '', pageId } = useParams()
  const [searchParams] = useSearchParams()
  const parentPageId = searchParams.get('parent')
  const navigate = useNavigate()
  const { space, tree, reloadTree } = useSpaceContext()
  const { user } = useAuth()
  const isEdit = Boolean(pageId)
  // Co-editing applies to existing pages only — a new page has no id to share.
  const [collab, setCollab] = useState<CollabToken | null>(null)

  const [title, setTitle] = useState('')
  const [content, setContent] = useState(EMPTY_DOC)
  const [changeComment, setChangeComment] = useState('')
  const [loading, setLoading] = useState(isEdit)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fullWidth, setFullWidth] = useState(false)

  // The formatting toolbar renders in the page-level top action bar (not
  // inside the paper card), same as view mode — the Editor/CollaborativeEditor
  // hand their live TipTap instance up via this callback once created.
  const [editorInstance, setEditorInstance] = useState<TiptapEditor | null>(null)

  // A brand-new page has no id until the user clicks "Create page" — but
  // attachments (and, later, other id-keyed features) need a real one right
  // away. So a hidden draft page is created the moment the editor mounts;
  // it's invisible everywhere until this form's submit "publishes" it.
  // draftIdRef always holds the in-flight/resolved promise so a very early
  // image insert can await it rather than fail; draftId mirrors it in state
  // purely so the JSX can read it without unwrapping a promise.
  const draftIdRef = useRef<Promise<string> | null>(null)
  const [draftId, setDraftId] = useState<string | null>(null)

  // Template picker (new pages only). Selecting a template reseeds the editor
  // by changing its `key`, since an already-mounted editable editor doesn't
  // otherwise react to external content changes.
  const [templates, setTemplates] = useState<PageTemplate[]>([])
  const [templateId, setTemplateId] = useState('')

  useEffect(() => {
    if (isEdit) return
    api.templates.list(space.id).then(setTemplates).catch(() => {})
  }, [isEdit, space.id])

  // Create the invisible draft once, on first mount of a new-page form. Not
  // re-run if space.id/parentPageId happen to change identity, since this
  // must fire exactly once per visit to the "new page" form.
  useEffect(() => {
    if (isEdit) return
    let cancelled = false
    const promise = api.pages
      .createDraft({ spaceId: space.id, parentPageId })
      .then((d) => {
        if (!cancelled) setDraftId(d.id)
        return d.id
      })
    draftIdRef.current = promise
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isEdit])

  function onPickTemplate(id: string) {
    setTemplateId(id)
    const template = templates.find((t) => t.id === id)
    setContent(template ? template.contentJson : EMPTY_DOC)
  }

  useEffect(() => {
    if (!pageId) return
    let cancelled = false
    setLoading(true)
    api.pages
      .get(pageId)
      .then((p) => {
        if (cancelled) return
        setTitle(p.title)
        setContent(p.contentJson)
        setFullWidth(p.fullWidth)
      })
      .catch((err: unknown) => !cancelled && setError(err instanceof Error ? err.message : 'Failed to load page.'))
      .finally(() => !cancelled && setLoading(false))
    return () => {
      cancelled = true
    }
  }, [pageId])

  // Ask whether live co-editing is available for this page. If the server has
  // no shared secret configured, we simply stay on the single-user editor.
  useEffect(() => {
    if (!pageId) return
    let cancelled = false
    api.pages
      .collabToken(pageId)
      .then((t) => !cancelled && setCollab(t.enabled && t.token ? t : null))
      .catch(() => !cancelled && setCollab(null))
    return () => {
      cancelled = true
    }
  }, [pageId])

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      let saved
      if (pageId) {
        saved = await api.pages.update(pageId, { title, contentJson: content, changeComment: changeComment || null })
      } else {
        const id = draftId ?? (await draftIdRef.current)
        if (!id) throw new Error('Still preparing this page — try again in a moment.')
        saved = await api.pages.publish(id, { title, contentJson: content })
      }
      reloadTree()
      navigate(`/spaces/${key}/pages/${saved.id}`)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save the page.')
    } finally {
      setBusy(false)
    }
  }

  /** The page id image attachments should be uploaded against, in either mode. */
  async function resolveUploadPageId(): Promise<string> {
    if (pageId) return pageId
    const id = draftId ?? (await draftIdRef.current)
    if (!id) throw new Error('Still preparing this page — try again in a moment.')
    return id
  }

  async function toggleFullWidth() {
    const next = !fullWidth
    setFullWidth(next) // optimistic — display metadata, not document content
    try {
      const id = await resolveUploadPageId()
      await api.pages.setLayout(id, { fullWidth: next })
    } catch {
      setFullWidth(!next)
    }
  }

  async function onCancel() {
    if (pageId) {
      navigate(`/spaces/${key}/pages/${pageId}`)
      return
    }
    // Best-effort: an abandoned draft is cleaned up immediately, but a failed
    // delete must never block navigating away.
    const id = draftId ?? (await draftIdRef.current?.catch(() => null))
    if (id) api.pages.deleteDraft(id).catch(() => {})
    navigate(`/spaces/${key}`)
  }

  if (loading) return <p className="muted page-wrap">Loading…</p>

  return (
    <>
      <div className="page-actionbar page-actionbar--editor">
        <div className="page-actionbar__primary">
          {editorInstance && (
            <Toolbar editor={editorInstance} getUploadPageId={resolveUploadPageId} onUploadError={setError} />
          )}
        </div>
        <div className="page-actionbar__secondary">
          <button
            type="button"
            className="btn btn--ghost page-actionbar__fullwidth-toggle"
            onClick={toggleFullWidth}
            title={fullWidth ? 'Switch to normal width' : 'Switch to full width'}
          >
            {fullWidth ? '⤡ Normal width' : '⤢ Full width'}
          </button>
          {/* Publish/Update and Close sit on the toolbar row, where
              Confluence keeps them — not under the page. They are outside
              the <form> element, so `form=` ties the submit to it. */}
          <button type="submit" form="page-editor-form" className="btn btn--primary" disabled={busy}>
            {busy ? 'Saving…' : isEdit ? 'Update' : 'Publish'}
          </button>
          <button type="button" className="btn btn--ghost" onClick={onCancel}>
            Close
          </button>
        </div>
      </div>
      {/* Below the toolbar, not above it: the toolbar is the top edge of the
          editing surface and the breadcrumb belongs with the page content
          (SpacePage suppresses its own copy on this route). */}
      <div className="page-column">
      <SpaceBreadcrumb space={space} tree={tree} />
      <form id="page-editor-form" className={fullWidth ? 'page-wrap page-wrap--full editor-form' : 'page-wrap editor-form'} onSubmit={onSubmit}>
      {error && <p className="alert alert--error">{error}</p>}
      {!isEdit && templates.length > 0 && (
        <label className="change-comment">
          Start from a template (optional)
          <select value={templateId} onChange={(e) => onPickTemplate(e.target.value)}>
            <option value="">Blank page</option>
            {templates.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}{t.spaceId ? '' : ' (instance-wide)'}
              </option>
            ))}
          </select>
        </label>
      )}
      <div className="paper">
        <input
          className="title-input"
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          placeholder="Page title"
          required
          autoFocus={!isEdit}
        />
        {collab && pageId ? (
          <CollaborativeEditor
            key={pageId}
            pageId={pageId}
            token={collab.token}
            initialContent={content}
            displayName={user?.displayName ?? 'Anonymous'}
            onChange={setContent}
            getUploadPageId={resolveUploadPageId}
            onUploadError={setError}
            onEditorReady={setEditorInstance}
          />
        ) : (
          <Editor
            key={pageId ?? `new-${templateId || 'blank'}`}
            value={content}
            editable
            onChange={setContent}
            getUploadPageId={resolveUploadPageId}
            onUploadError={setError}
            onEditorReady={setEditorInstance}
          />
        )}
      </div>
      {isEdit && (
        <label className="change-comment">
          What changed? (optional)
          <input value={changeComment} onChange={(e) => setChangeComment(e.target.value)} placeholder="e.g. fixed typo" />
        </label>
      )}
      </form>
      </div>
    </>
  )
}
