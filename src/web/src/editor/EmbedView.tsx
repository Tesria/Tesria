import { useCallback, useEffect, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { api, ApiError, type EmbedResolution } from '../api/client'
import { normalizeWebAddress } from './webAddress'

type State =
  | { status: 'empty' }
  | { status: 'loading' }
  | { status: 'ok'; embed: EmbedResolution }
  | { status: 'refused'; reason: string }

/**
 * Asks the server what (if anything) this URL may become, then frames
 * exactly what it was told to, never the URL as typed.
 */
export function EmbedView({ node, editor, selected, updateAttributes }: ReactNodeViewProps) {
  const url = String(node.attrs.url ?? '')
  const [state, setState] = useState<State>({ status: 'empty' })
  const [draft, setDraft] = useState(url)

  const resolve = useCallback(async (value: string) => {
    if (!value.trim()) {
      setState({ status: 'empty' })
      return
    }
    setState({ status: 'loading' })
    try {
      const embed = await api.embeds.resolve(value)
      setState(embed.allowed && embed.url
        ? { status: 'ok', embed }
        : { status: 'refused', reason: embed.reason ?? 'That address cannot be embedded here.' })
    } catch (err) {
      setState({ status: 'refused', reason: err instanceof ApiError ? err.message : 'Could not check that address.' })
    }
  }, [])

  useEffect(() => {
    setDraft(url)
    void resolve(url)
  }, [url, resolve])

  return (
    <NodeViewWrapper className={selected ? 'embed is-selected' : 'embed'} contentEditable={false}>
      {editor.isEditable && (
        <form
          className="embed__form"
          onSubmit={(e) => {
            // A popover form inside the page's own save form: see the
            // architecture doc's editor gotcha.
            e.preventDefault()
            e.stopPropagation()
            updateAttributes({ url: normalizeWebAddress(draft) })
          }}
        >
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder="Paste a YouTube, Vimeo, Figma… address"
            aria-label="Embed address"
          />
          <button type="submit" className="link-btn">Embed</button>
        </form>
      )}
      {state.status === 'loading' && <p className="embed__note">Checking that address…</p>}
      {state.status === 'empty' && !editor.isEditable && <p className="embed__note">No embed set.</p>}
      {state.status === 'refused' && (
        <p className="embed__note embed__note--refused">
          {state.reason}{' '}
          {url && <a href={url} target="_blank" rel="noreferrer noopener">Open it instead</a>}
        </p>
      )}
      {state.status === 'ok' && (
        <div className="embed__frame" style={{ aspectRatio: state.embed.aspectRatio ?? '16 / 9' }}>
          <iframe
            src={state.embed.url!}
            title={state.embed.provider ?? 'Embedded content'}
            allowFullScreen
            // The frame gets the minimum it needs to play media and go
            // fullscreen, never same-origin, never top-level navigation.
            sandbox="allow-scripts allow-same-origin allow-presentation allow-popups"
            referrerPolicy="strict-origin-when-cross-origin"
            loading="lazy"
          />
        </div>
      )}
    </NodeViewWrapper>
  )
}
