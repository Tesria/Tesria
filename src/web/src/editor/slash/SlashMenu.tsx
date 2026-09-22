import { forwardRef, useEffect, useImperativeHandle, useState } from 'react'
import type { SuggestionKeyDownProps, SuggestionProps } from '@tiptap/suggestion'
import type { SlashItem } from './items'

export type SlashMenuRef = {
  onKeyDown: (props: SuggestionKeyDownProps) => boolean
}

/** The "/" command popup: a filtered list with keyboard navigation. */
export const SlashMenu = forwardRef<SlashMenuRef, SuggestionProps<SlashItem>>((props, ref) => {
  const { items, command } = props
  const [selected, setSelected] = useState(0)

  // The item list changes as the user types the query: keep the highlighted
  // index in range rather than pointing at a since-filtered-out item.
  useEffect(() => {
    setSelected(0)
  }, [items])

  useImperativeHandle(ref, () => ({
    onKeyDown: ({ event }) => {
      if (items.length === 0) return false
      if (event.key === 'ArrowDown') {
        setSelected((s) => (s + 1) % items.length)
        return true
      }
      if (event.key === 'ArrowUp') {
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
    <div className="slash-menu">
      {items.map((item, i) => (
        <button
          key={item.title}
          type="button"
          className={i === selected ? 'slash-menu__item is-selected' : 'slash-menu__item'}
          onMouseDown={(e) => e.preventDefault()}
          onMouseEnter={() => setSelected(i)}
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
