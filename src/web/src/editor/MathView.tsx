import { useEffect, useRef, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'

/**
 * KaTeX is ~280KB with its fonts, so it loads on demand and the promise is
 * shared — a page of equations pays for it once.
 */
let katexPromise: Promise<typeof import('katex').default> | null = null

function loadKatex() {
  katexPromise ??= Promise.all([import('katex'), import('katex/dist/katex.min.css')]).then(([m]) => m.default)
  return katexPromise
}

export function MathView({ node, editor, selected, updateAttributes }: ReactNodeViewProps) {
  const latex = String(node.attrs.latex ?? '')
  const display = Boolean(node.attrs.display)
  const [html, setHtml] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(latex)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    setDraft(latex)
    if (!latex.trim()) {
      setHtml(null)
      setError(null)
      return
    }
    let cancelled = false
    loadKatex()
      .then((katex) => {
        if (cancelled) return
        // throwOnError: false would render its own error markup; catching it
        // here keeps the message in this app's voice and styling.
        setHtml(katex.renderToString(latex, { displayMode: display, throwOnError: true, strict: false }))
        setError(null)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setHtml(null)
        setError(err instanceof Error ? err.message : 'That expression could not be rendered.')
      })
    return () => { cancelled = true }
  }, [latex, display])

  useEffect(() => {
    if (editing) inputRef.current?.focus()
  }, [editing])

  function commit() {
    updateAttributes({ latex: draft })
    setEditing(false)
  }

  const className = [
    'math',
    display ? 'math--display' : 'math--inline',
    selected ? 'is-selected' : '',
  ].filter(Boolean).join(' ')

  return (
    <NodeViewWrapper as={display ? 'div' : 'span'} className={className} contentEditable={false}>
      {editing && editor.isEditable ? (
        <input
          ref={inputRef}
          className="math__input"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onBlur={commit}
          onKeyDown={(e) => {
            if (e.key === 'Enter') { e.preventDefault(); commit() }
            if (e.key === 'Escape') { setDraft(latex); setEditing(false) }
          }}
          placeholder="e = mc^2"
          aria-label="LaTeX"
        />
      ) : (
        <span
          className="math__rendered"
          onDoubleClick={() => editor.isEditable && setEditing(true)}
          title={editor.isEditable ? 'Double-click to edit' : latex}
        >
          {error && <span className="math__error">{error}</span>}
          {!error && html && <span dangerouslySetInnerHTML={{ __html: html }} />}
          {!error && !html && <span className="math__empty">{editor.isEditable ? 'Double-click to add maths' : ''}</span>}
        </span>
      )}
    </NodeViewWrapper>
  )
}
