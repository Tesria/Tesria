import { useEffect, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { api, type LinkPreview } from '../api/client'

/** An address typed without a scheme is a web address, as in the link dialog. */
function normalize(value: string): string {
  const v = value.trim()
  if (!v) return ''
  return /^[a-z][a-z0-9+.-]*:/i.test(v) ? v : `https://${v}`
}

/**
 * Renders a link with the target's own title, fetched server-side and
 * cached. A failure is never fatal: the URL itself is always a working
 * link, which is what the author wrote in the first place.
 *
 * While editing, an empty or selected smart link shows its address and the
 * card/inline choice (dev-plan 10.5 step 1). Until then it was inserted with
 * no address and nothing could give it one, so the element did nothing.
 */
export function SmartLinkView({ node, editor, selected, updateAttributes }: ReactNodeViewProps) {
  const url = String(node.attrs.url ?? '')
  const inline = node.attrs.display === 'inline'
  const [preview, setPreview] = useState<LinkPreview | null>(null)
  const [loading, setLoading] = useState(false)
  const [draft, setDraft] = useState(url)

  useEffect(() => setDraft(url), [url])

  useEffect(() => {
    setPreview(null)
    if (!url) return
    let canceled = false
    setLoading(true)
    api.embeds.unfurl(url)
      .then((p) => !canceled && setPreview(p))
      .catch(() => {})
      .finally(() => !canceled && setLoading(false))
    return () => { canceled = true }
  }, [url])

  const editing = editor.isEditable && (selected || !url)
  const title = preview?.title ?? url
  const className = [inline ? 'smart-link smart-link--inline' : 'smart-link', selected ? 'is-selected' : '']
    .filter(Boolean).join(' ')
  let host = ''
  try { host = url ? new URL(url).host : '' } catch { host = url }

  return (
    <NodeViewWrapper className={className} contentEditable={false}>
      {editing && (
        <form
          className="embed__form smart-link__form"
          onSubmit={(e) => {
            // A form inside the page's own save form: see the architecture
            // doc's editor gotcha.
            e.preventDefault()
            e.stopPropagation()
            updateAttributes({ url: normalize(draft) })
          }}
        >
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder="Paste an address to link to"
            aria-label="Smart link address"
            autoFocus={!url}
          />
          <button type="submit" className="link-btn">{url ? 'Update' : 'Show'}</button>
          <span className="smart-link__display" role="group" aria-label="Show as">
            <button type="button" className={inline ? 'link-btn' : 'link-btn is-active'} aria-pressed={!inline}
              onClick={() => updateAttributes({ display: 'card' })}>Card</button>
            <button type="button" className={inline ? 'link-btn is-active' : 'link-btn'} aria-pressed={inline}
              onClick={() => updateAttributes({ display: 'inline' })}>Inline</button>
          </span>
        </form>
      )}
      {!url ? (
        !editor.isEditable && <p className="embed__note">No link set.</p>
      ) : (
        <a href={url} target="_blank" rel="noreferrer noopener" className="smart-link__body">
          {preview?.imageUrl && !inline && <img className="smart-link__image" src={preview.imageUrl} alt="" />}
          <span className="smart-link__text">
            <span className="smart-link__title">{loading && !preview ? url : title}</span>
            {!inline && preview?.description && <span className="smart-link__desc">{preview.description}</span>}
            {!inline && <span className="smart-link__site">{preview?.siteName ?? host}</span>}
          </span>
        </a>
      )}
    </NodeViewWrapper>
  )
}
