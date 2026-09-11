import { useEffect, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { api, type LinkPreview } from '../api/client'

/**
 * Renders a link with the target's own title, fetched server-side and
 * cached. A failure is never fatal: the URL itself is always a working
 * link, which is what the author wrote in the first place.
 */
export function SmartLinkView({ node, selected }: ReactNodeViewProps) {
  const url = String(node.attrs.url ?? '')
  const inline = node.attrs.display === 'inline'
  const [preview, setPreview] = useState<LinkPreview | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!url) return
    let cancelled = false
    setLoading(true)
    api.embeds.unfurl(url)
      .then((p) => !cancelled && setPreview(p))
      .catch(() => {})
      .finally(() => !cancelled && setLoading(false))
    return () => { cancelled = true }
  }, [url])

  const title = preview?.title ?? url
  const className = [inline ? 'smart-link smart-link--inline' : 'smart-link', selected ? 'is-selected' : '']
    .filter(Boolean).join(' ')

  return (
    <NodeViewWrapper className={className} contentEditable={false}>
      <a href={url} target="_blank" rel="noreferrer noopener" className="smart-link__body">
        {preview?.imageUrl && !inline && <img className="smart-link__image" src={preview.imageUrl} alt="" />}
        <span className="smart-link__text">
          <span className="smart-link__title">{loading && !preview ? url : title}</span>
          {!inline && preview?.description && <span className="smart-link__desc">{preview.description}</span>}
          {!inline && <span className="smart-link__site">{preview?.siteName ?? new URL(url || 'https://x').host}</span>}
        </span>
      </a>
    </NodeViewWrapper>
  )
}
