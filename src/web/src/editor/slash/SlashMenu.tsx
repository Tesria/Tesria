import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react'
import type { SuggestionKeyDownProps, SuggestionProps } from '@tiptap/suggestion'
import type { SlashItem } from './items'
import { keepHighlightInView } from '../suggest/keepInView'
import { pointerMoved } from '../suggest/pointerMoved'

export type SlashMenuRef = {
  onKeyDown: (props: SuggestionKeyDownProps) => boolean
}

/** The "/" command popup: a filtered list with keyboard navigation. */
export const SlashMenu = forwardRef<SlashMenuRef, SuggestionProps<SlashItem>>((props, ref) => {
  const { items, command } = props
  const [selected, setSelected] = useState(0)
  const listRef = useRef<HTMLDivElement>(null)
  // The last pointer position seen over the menu (t4-R04).
  const lastPointer = useRef<{ x: number; y: number } | null>(null)
  // Set by the arrow keys only: a row picked by hovering is already under
  // the pointer, and scrolling for it would slide the next row under it.
  const fromKeys = useRef(false)

  // The item list changes as the user types the query: keep the highlighted
  // index in range rather than pointing at a since-filtered-out item.
  useEffect(() => {
    setSelected(0)
  }, [items])

  useEffect(() => {
    if (!fromKeys.current) return
    fromKeys.current = false
    keepHighlightInView(listRef.current)
  }, [selected])

  useImperativeHandle(ref, () => ({
    onKeyDown: ({ event }) => {
      if (items.length === 0) return false
      if (event.key === 'ArrowDown') {
        fromKeys.current = true
        setSelected((s) => (s + 1) % items.length)
        return true
      }
      if (event.key === 'ArrowUp') {
        fromKeys.current = true
        setSelected((s) => (s - 1 + items.length) % items.length)
        return true
      }
      if (event.key === 'Enter') {
        const item = items[selected]
        if (item) command(item)
        return true
      }
      return false
    },
  }), [items, selected, command])

  if (items.length === 0) {
    return <div className="slash-menu slash-menu--empty">No matching blocks</div>
  }

  return (
    <div className="slash-menu" ref={listRef}>
      {items.map((item, i) => (
        <button
          key={item.title}
          type="button"
          className={i === selected ? 'slash-menu__item is-selected' : 'slash-menu__item'}
          onMouseDown={(e) => e.preventDefault()}
          onMouseMove={(e) => {
            const now = { x: e.clientX, y: e.clientY }
            if (pointerMoved(lastPointer.current, now)) setSelected(i)
            lastPointer.current = now
          }}
          onClick={() => command(item)}
        >
          <span className="slash-menu__title">{item.title}</span>
          <span className="slash-menu__desc">{item.description}</span>
        </button>
      ))}
    </div>
  )
})
SlashMenu.displayName = 'SlashMenu'
