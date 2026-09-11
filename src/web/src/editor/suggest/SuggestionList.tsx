import { forwardRef, useEffect, useImperativeHandle, useState, type ReactNode } from 'react'
import type { SuggestionKeyDownProps } from '@tiptap/suggestion'

export type SuggestionListRef = {
  onKeyDown: (props: SuggestionKeyDownProps) => boolean
}

type Props<T> = {
  items: T[]
  onSelect: (item: T) => void
  /** How one row is drawn. Keyed by the caller, which knows what makes an item unique. */
  render: (item: T) => ReactNode
  keyOf: (item: T) => string
  emptyLabel: string
}

/**
 * The keyboard-navigable popup shared by the `@` mention and `:` emoji
 * suggestions. The slash menu predates it and keeps its own two-line layout
 * (title + description); these two are one line each, so the list behaviour —
 * arrow keys, Enter, keeping the highlight in range as the query narrows —
 * is the only part worth sharing.
 */
function SuggestionListInner<T>(
  { items, onSelect, render, keyOf, emptyLabel }: Props<T>,
  ref: React.Ref<SuggestionListRef>,
) {
  const [selected, setSelected] = useState(0)

  // The list changes as the query narrows — never leave the highlight
  // pointing at an item that has been filtered away.
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
      if (event.key === 'Enter' || event.key === 'Tab') {
        const item = items[selected]
        if (item) onSelect(item)
        return true
      }
      return false
    },
  }), [items, selected, onSelect])

  if (items.length === 0) return <div className="slash-menu slash-menu--empty">{emptyLabel}</div>

  return (
    <div className="slash-menu suggest-menu">
      {items.map((item, i) => (
        <button
          key={keyOf(item)}
          type="button"
          className={i === selected ? 'suggest-menu__item is-selected' : 'suggest-menu__item'}
          onMouseDown={(e) => e.preventDefault()}
          onMouseEnter={() => setSelected(i)}
          onClick={() => onSelect(item)}
        >
          {render(item)}
        </button>
      ))}
    </div>
  )
}

export const SuggestionList = forwardRef(SuggestionListInner) as <T>(
  props: Props<T> & { ref?: React.Ref<SuggestionListRef> },
) => ReturnType<typeof SuggestionListInner>
