import { useEffect, useMemo, useRef, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { api, attachmentDownloadUrl, attachmentViewUrl, type Attachment } from '../api/client'
import { getDynamicBlockStorage } from './dynamicBlock'

/** What to draw, decided from the file's own content type. */
function kindOf(contentType: string): 'video' | 'audio' | 'pdf' | 'image' | 'file' {
  if (contentType.startsWith('video/')) return 'video'
  // The raster types the server serves inline. An image chosen here used to
  // show as a file card (found 2026-09-23); SVG stays a card, because the
  // server only ever sends SVG as a download.
  if (/^image\/(png|jpeg|gif|webp)$/.test(contentType)) return 'image'
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
  const animation = node.attrs.playback === 'animation'
  const [options, setOptions] = useState<Attachment[] | null>(null)
  const [current, setCurrent] = useState<Attachment | null>(null)
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement | null>(null)

  // Upload from the block itself (dev-plan 15.2), rather than a trip to the
  // Attachments tab, which a page being written for the first time does not
  // have. The file lands on the same page, drafts included, and is chosen.
  async function upload(file: File) {
    const getPageId = getDynamicBlockStorage(editor)?.getPageId
    if (!getPageId) return
    setUploading(true)
    setUploadError(null)
    try {
      const added = await api.attachments.upload(await getPageId(), file)
      setOptions((list) => [...(list ?? []), added])
      updateAttributes({ attachmentId: added.id })
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Upload failed.')
    } finally {
      setUploading(false)
      if (fileInput.current) fileInput.current.value = ''
    }
  }

  // The page's attachments, for the picker and to resolve the chosen id.
  // Same host-page stash the dynamic blocks use: an attachment belongs to a
  // page, so there is nothing to show without one.
  useEffect(() => {
    const getPageId = getDynamicBlockStorage(editor)?.getPageId
    if (!getPageId) return
    let canceled = false
    getPageId()
      .then((pageId) => api.attachments.listForPage(pageId))
      .then((list) => !canceled && setOptions(list))
      .catch(() => !canceled && setOptions([]))
    return () => { canceled = true }
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
          <button type="button" className="btn btn--sm" disabled={uploading}
            onClick={() => fileInput.current?.click()}>
            {uploading ? 'Uploading…' : 'Upload'}
          </button>
          <input ref={fileInput} type="file" hidden
            onChange={(e) => { const f = e.target.files?.[0]; if (f) void upload(f) }} />
        </label>
      )}
      {uploadError && <p className="attachment-block__note alert alert--error">{uploadError}</p>}
      {editor.isEditable && (!current || kindOf(current.contentType) === 'video') && (
        <label className="attachment-block__picker">
          <span>Show as</span>
          <select value={animation ? 'animation' : 'player'}
            onChange={(e) => updateAttributes({ playback: e.target.value })}>
            <option value="player">A video with controls</option>
            <option value="animation">An animation: silent, looping, no controls</option>
          </select>
        </label>
      )}
      {!attachmentId && <p className="attachment-block__note">No file chosen.</p>}
      {attachmentId && !current && options !== null && (
        <p className="attachment-block__note">That file is no longer attached to this page.</p>
      )}
      {current && href && <Body attachment={current} href={href} animation={animation} />}
    </NodeViewWrapper>
  )
}

function Body({ attachment, href, animation }: { attachment: Attachment; href: string; animation: boolean }) {
  const kind = kindOf(attachment.contentType)
  if (kind === 'video' && animation) return <Animation href={href} label={attachment.filename} />
  // #t=0.1: Safari on iOS draws a paused video as a black box until played;
  // a start time makes it load and show that frame instead.
  if (kind === 'video') return <video className="attachment-block__video" src={`${href}#t=0.1`} controls preload="metadata" />
  if (kind === 'image') return <img className="attachment-block__image" src={href} alt={attachment.filename} />
  if (kind === 'audio') return <audio className="attachment-block__audio" src={href} controls preload="metadata" />
  if (kind === 'pdf') {
    // The browser's own viewer, framed same-origin: no PDF.js in the bundle.
    // `frame-src 'self'` is what lets this through the CSP.
    return (
      <div className="attachment-block__pdf">
        <iframe src={attachmentViewUrl(attachment.id)} title={attachment.filename} />
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

/**
 * A video shown as an animation (dev-plan 10.5 step 2): silent, looping,
 * starting by itself, no player controls, the way a GIF behaves but at a
 * fraction of a GIF's size.
 *
 * Two rules it keeps. Anything that moves for more than five seconds needs
 * a way to stop it (WCAG 2.2.2), hence the pause button. And a reader whose
 * system asks for reduced motion gets it paused on its first frame, to start
 * if they choose. An exported page does the same through the one script it
 * keeps (SiteChrome.ThemeScript), driven by the same data attributes, since
 * none of this component survives an export.
 *
 * `muted` is set as an attribute by hand: React sets only the property, so
 * the serialized page an export captures would lose it, and browsers refuse
 * to start a video with sound by itself. `#t=0.1` makes Safari show the
 * first frame of a paused one instead of a black box.
 */
function Animation({ href, label }: { href: string; label: string }) {
  const ref = useRef<HTMLVideoElement>(null)
  const [paused, setPaused] = useState(false)

  useEffect(() => {
    const video = ref.current
    if (!video) return
    video.muted = true
    video.setAttribute('muted', '')
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) {
      video.removeAttribute('autoplay')
      video.pause()
      setPaused(true)
    }
  }, [href])

  function toggle() {
    const video = ref.current
    if (!video) return
    if (video.paused) { void video.play(); setPaused(false) } else { video.pause(); setPaused(true) }
  }

  return (
    <div className="animation" data-animation>
      <video
        ref={ref}
        className="attachment-block__video animation__video"
        src={`${href}#t=0.1`}
        autoPlay
        muted
        loop
        playsInline
        preload="auto"
        disablePictureInPicture
        aria-label={label}
        data-animation-video
      />
      <button type="button" className="animation__toggle" data-animation-toggle
        onMouseDown={(e) => e.preventDefault()} onClick={toggle}
        aria-label={paused ? 'Play the animation' : 'Pause the animation'} aria-pressed={paused}>
        {paused
          ? <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5v14l12-7z" fill="currentColor" /></svg>
          : <svg width="14" height="14" viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5h4v14H7zM13 5h4v14h-4z" fill="currentColor" /></svg>}
      </button>
    </div>
  )
}
