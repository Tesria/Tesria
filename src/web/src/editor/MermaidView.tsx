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

function loadMermaid() {
  mermaidPromise ??= import('mermaid').then((m) => {
    m.default.initialize({
      startOnLoad: false,
      // The editor's own theme decides light/dark, so Mermaid is told rather
      // than left to sniff: it cannot see our CSS variables.
      theme: document.documentElement.dataset.theme === 'dark'
        || (!document.documentElement.dataset.theme && matchMedia('(prefers-color-scheme: dark)').matches)
        ? 'dark' : 'default',
      securityLevel: 'strict',
    })
    return m.default
  })
  return mermaidPromise
}

export function MermaidDiagram({ source }: { source: string }) {
  const [svg, setSvg] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const id = useRef(`mermaid-${++counter}`)

  useEffect(() => {
    let cancelled = false
    const text = source.trim()
    if (!text) {
      setSvg(null)
      setError(null)
      return
    }
    loadMermaid()
      .then((mermaid) => mermaid.render(id.current, text))
      .then(({ svg }) => {
        if (cancelled) return
        setSvg(svg)
        setError(null)
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setSvg(null)
        // Mermaid's own parse errors name the line, which is the useful part.
        setError(err instanceof Error ? err.message : 'That diagram could not be drawn.')
      })
    return () => { cancelled = true }
  }, [source])

  if (error) return <p className="mermaid__error">{error}</p>
  if (!svg) return <p className="mermaid__note">Diagram will appear here.</p>
  // Mermaid is initialised with securityLevel 'strict', which strips scripts
  // and event handlers from the SVG it produces.
  return <div className="mermaid__svg" dangerouslySetInnerHTML={{ __html: svg }} />
}
