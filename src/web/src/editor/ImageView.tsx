import { useRef } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'

/**
 * A picture with a size, an alignment and a caption (dev-plan 15.2).
 *
 * The width is a percentage of the column, not pixels, so a picture sized on
 * a wide screen keeps its proportion of the page on a phone and in an
 * export. Null means its own size, up to the column, which is what every
 * picture was before. Alignment places the picture in its column without
 * wrapping text around it: wrapping reflows unpredictably between screens,
 * and a Layout does side-by-side properly.
 */
export function ImageView({ node, updateAttributes, editor, selected }: ReactNodeViewProps) {
  const figureRef = useRef<HTMLElement | null>(null)
  const attrs = node.attrs as {
    src: string; alt: string | null; title: string | null
    border?: boolean; shadow?: boolean
    width?: number | null; align?: string | null; caption?: string | null
  }
  const align = attrs.align ?? 'center'
  const width = align === 'full' ? 100 : attrs.width ?? null
  const editable = editor.isEditable

  // Dragging the corner: the new width is where the pointer is, as a share
  // of the column the figure sits in. Pointer events, so a finger works too.
  function startResize(e: React.PointerEvent) {
    e.preventDefault()
    e.stopPropagation()
    const figure = figureRef.current
    const column = figure?.parentElement
    if (!figure || !column) return
    const columnWidth = column.getBoundingClientRect().width
    const left = figure.getBoundingClientRect().left
    const move = (ev: PointerEvent) => {
      const pct = Math.round(((ev.clientX - left) / columnWidth) * 100)
      figure.style.width = `${Math.min(100, Math.max(10, pct))}%`
    }
    const up = (ev: PointerEvent) => {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', up)
      const pct = Math.round(((ev.clientX - left) / columnWidth) * 100)
      updateAttributes({ width: Math.min(100, Math.max(10, pct)), align: align === 'full' ? 'center' : align })
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', up)
  }

  const imgClass = [attrs.border ? 'img-border' : '', attrs.shadow ? 'img-shadow' : ''].filter(Boolean).join(' ')

  return (
    <NodeViewWrapper
      as="figure"
      ref={figureRef}
      className={`image-figure image-figure--${align}${selected && editable ? ' is-selected' : ''}`}
      style={width ? { width: `${width}%` } : undefined}
      data-drag-handle=""
    >
      <img src={attrs.src} alt={attrs.alt ?? ''} title={attrs.title ?? undefined} className={imgClass || undefined} draggable={false} />
      {attrs.caption && <figcaption className="image-figure__caption">{attrs.caption}</figcaption>}
      {editable && selected && (
        <span
          className="image-figure__resize"
          role="separator"
          aria-label="Drag to resize the picture"
          title="Drag to resize"
          onPointerDown={startResize}
        />
      )}
    </NodeViewWrapper>
  )
}
