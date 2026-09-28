import { useEffect, useRef, useState } from 'react'

let counter = 0

/**
 * Renders a Mermaid diagram from its source.
 *
 * Mermaid is ~500KB, so it is imported dynamically: a page with no diagram
 * never downloads it, and Vite splits it into its own chunk. The import
 * promise is cached at module scope so ten diagrams on a page share one
 * load.
 */
let mermaidPromise: Promise<typeof import('mermaid').default> | null = null

/** The page's theme right now: the editor's own, else the system's. */
function pageTheme(): 'dark' | 'default' {
  return document.documentElement.dataset.theme === 'dark'
    || (!document.documentElement.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches)
    ? 'dark' : 'default'
}

/**
 * Mermaid, set to the page's theme at the moment of drawing. Told before
 * every drawing, not once at load (the owner, 2026-09-28): a diagram drawn
 * in the dark theme stayed dark after switching to light, its titles pale on
 * a white panel. The editor's own theme decides, since Mermaid cannot see
 * our CSS variables.
 */
function loadMermaid() {
  mermaidPromise ??= import('mermaid').then((m) => m.default)
  return mermaidPromise.then((mermaid) => {
    mermaid.initialize({ startOnLoad: false, theme: pageTheme(), securityLevel: 'strict' })
    return mermaid
  })
}

/** The page's theme, as state that changes when the reader switches it. */
function usePageTheme() {
  const [theme, setTheme] = useState(pageTheme)
  useEffect(() => {
    const update = () => setTheme(pageTheme())
    const observer = new MutationObserver(update)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    const media = matchMedia('(prefers-color-scheme: dark)')
    media.addEventListener('change', update)
    return () => { observer.disconnect(); media.removeEventListener('change', update) }
  }, [])
  return theme
}

export function MermaidDiagram({ source }: { source: string }) {
  const [svg, setSvg] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const id = useRef(`mermaid-${++counter}`)
  // Redrawn when the reader switches between light and dark.
  const theme = usePageTheme()

  useEffect(() => {
    let canceled = false
    const text = source.trim()
    if (!text) {
      setSvg(null)
      setError(null)
      return
    }
    loadMermaid()
      .then((mermaid) => mermaid.render(id.current, text))
      .then(({ svg }) => {
        if (canceled) return
        setSvg(svg)
        setError(null)
      })
      .catch((err: unknown) => {
        if (canceled) return
        setSvg(null)
        // Mermaid's own parse errors name the line, which is the useful part.
        setError(err instanceof Error ? err.message : 'That diagram could not be drawn.')
      })
    return () => { canceled = true }
  }, [source, theme])

  if (error) return <p className="mermaid__error">{error}</p>
  if (!svg) return <p className="mermaid__note">Diagram will appear here.</p>
  // Mermaid is initialized with securityLevel 'strict', which strips scripts
  // and event handlers from the SVG it produces.
  return <div className="mermaid__svg" dangerouslySetInnerHTML={{ __html: svg }} />
}
