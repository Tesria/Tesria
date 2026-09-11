import { useEffect, useMemo, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { api, attachmentDownloadUrl, type Attachment } from '../api/client'
import { getDynamicBlockStorage } from './dynamicBlock'

/** What to draw, decided from the file's own content type. */
function kindOf(contentType: string): 'video' | 'audio' | 'pdf' | 'file' {
  if (contentType.startsWith('video/')) return 'video'
  if (contentType.startsWith('audio/')) return 'audio'
  if (contentType === 'application/pdf') return 'pdf'
  return 'file'
}

function humanSize(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB']
  let size = bytes
  let unit = 0
  while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit++ }
  return unit === 0 ? `${bytes} B` : `${size.toFixed(size < 10 ? 1 : 0)} ${units[unit]}`
}

export function AttachmentView({ node, editor, selected, updateAttributes }: ReactNodeViewProps) {
  const attachmentId = (node.attrs.attachmentId as string | null) ?? null
  const [options, setOptions] = useState<Attachment[] | null>(null)
  const [current, setCurrent] = useState<Attachment | null>(null)

  // The page's attachments, for the picker and to resolve the chosen id.
  // Same host-page stash the dynamic blocks use — an attachment belongs to a
  // page, so there is nothing to show without one.
  useEffect(() => {
    const getPageId = getDynamicBlockStorage(editor)?.getPageId
    if (!getPageId) return
    let cancelled = false
    getPageId()
      .then((pageId) => api.attachments.listForPage(pageId))
      .then((list) => !cancelled && setOptions(list))
      .catch(() => !cancelled && setOptions([]))
    return () => { cancelled = true }
  }, [editor])

  useEffect(() => {
    setCurrent(options?.find((a) => a.id === attachmentId) ?? null)
  }, [options, attachmentId])

  const href = useMemo(() => (attachmentId ? attachmentDownloadUrl(attachmentId) : null), [attachmentId])

  return (
    <NodeViewWrapper className={selected ? 'attachment-block is-selected' : 'attachment-block'} contentEditable={false}>
      {editor.isEditable && (
        <label className="attachment-block__picker">
          <span>File</span>
          <select
            value={attachmentId ?? ''}
            onChange={(e) => updateAttributes({ attachmentId: e.target.value || null })}
          >
            <option value="">Choose an attachment…</option>
            {(options ?? []).map((a) => <option key={a.id} value={a.id}>{a.filename}</option>)}
          </select>
        </label>
      )}
      {!attachmentId && <p className="attachment-block__note">No file chosen.</p>}
      {attachmentId && !current && options !== null && (
        <p className="attachment-block__note">That file is no longer attached to this page.</p>
      )}
      {current && href && <Body attachment={current} href={href} />}
    </NodeViewWrapper>
  )
}

function Body({ attachment, href }: { attachment: Attachment; href: string }) {
  const kind = kindOf(attachment.contentType)
  if (kind === 'video') return <video className="attachment-block__video" src={href} controls preload="metadata" />
  if (kind === 'audio') return <audio className="attachment-block__audio" src={href} controls preload="metadata" />
  if (kind === 'pdf') {
    // The browser's own viewer, framed same-origin — no PDF.js in the bundle.
    // `frame-src 'self'` is what lets this through the CSP.
    return (
      <div className="attachment-block__pdf">
        <iframe src={href} title={attachment.filename} />
        <a href={href} className="attachment-block__download">↓ {attachment.filename}</a>
      </div>
    )
  }
  return (
    <a className="attachment-block__file" href={href}>
      <span className="attachment-block__name">{attachment.filename}</span>
      <span className="attachment-block__meta">{humanSize(attachment.size)}</span>
    </a>
  )
}
