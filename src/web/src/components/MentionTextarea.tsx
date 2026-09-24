import { useEffect, useRef, useState, type TextareaHTMLAttributes } from 'react'
import { api, type Directory } from '../api/client'

/**
 * A comment box that offers people when you type @ (dev-plan 15.3). Picking
 * someone writes `@[Their Name](user:<id>)` into the text: the name for
 * anyone reading it raw, the id so the server knows exactly whom to tell.
 * CommentBody shows it as "@Their Name".
 */
export function MentionTextarea({ value, onValueChange, ...rest }: {
  value: string
  onValueChange: (value: string) => void
} & Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'value' | 'onChange'>) {
  const ref = useRef<HTMLTextAreaElement | null>(null)
  const [people, setPeople] = useState<Directory[] | null>(null)
  const [query, setQuery] = useState<string | null>(null)
  const [active, setActive] = useState(0)

  useEffect(() => {
    if (query === null || people) return
    api.users.list().then(setPeople).catch(() => setPeople([]))
  }, [query, people])

  // What is being typed after an @ that starts a word, up to the caret.
  function detect(text: string, caret: number) {
    const before = text.slice(0, caret)
    const m = /(^|\s)@([^\s@[\]()]{0,30})$/.exec(before)
    setQuery(m ? m[2] : null)
    setActive(0)
  }

  const q = (query ?? '').toLowerCase()
  const matches = query === null ? [] : (people ?? [])
    .filter((p) => p.displayName.toLowerCase().includes(q) || p.email.split('@')[0].toLowerCase().startsWith(q))
    .slice(0, 8)

  function pick(person: Directory) {
    const el = ref.current
    if (!el) return
    const caret = el.selectionStart
    const before = value.slice(0, caret).replace(/@[^\s@[\]()]{0,30}$/, '')
    const token = `@[${person.displayName.replace(/[\][]/g, '')}](user:${person.id}) `
    const next = before + token + value.slice(caret)
    onValueChange(next)
    setQuery(null)
    requestAnimationFrame(() => {
      el.focus()
      const at = (before + token).length
      el.setSelectionRange(at, at)
    })
  }

  return (
    <div className="mention-textarea">
      <textarea
        {...rest}
        ref={ref}
        value={value}
        onChange={(e) => { onValueChange(e.target.value); detect(e.target.value, e.target.selectionStart) }}
        onKeyDown={(e) => {
          if (matches.length > 0) {
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive((i) => (i + 1) % matches.length); return }
            if (e.key === 'ArrowUp') { e.preventDefault(); setActive((i) => (i - 1 + matches.length) % matches.length); return }
            if (e.key === 'Enter' || e.key === 'Tab') { e.preventDefault(); pick(matches[active]); return }
            if (e.key === 'Escape') { e.preventDefault(); setQuery(null); return }
          }
          rest.onKeyDown?.(e)
        }}
        onBlur={(e) => { setTimeout(() => setQuery(null), 150); rest.onBlur?.(e) }}
      />
      {matches.length > 0 && (
        <ul className="suggest-menu mention-textarea__menu" role="listbox" aria-label="People">
          {matches.map((p, i) => (
            <li key={p.id}>
              <button type="button" className={i === active ? 'suggest-menu__item is-selected' : 'suggest-menu__item'}
                role="option" aria-selected={i === active}
                onMouseDown={(e) => { e.preventDefault(); pick(p) }}>
                <span className="suggest-menu__label">{p.displayName}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

const TOKEN = /@\[([^\]\n]{1,200})\]\(user:[0-9a-fA-F-]{36}\)/g

/** A comment's text, with mention tokens shown as "@Name". */
export function CommentBody({ text }: { text: string }) {
  const parts: (string | { name: string })[] = []
  let last = 0
  for (const m of text.matchAll(TOKEN)) {
    if (m.index! > last) parts.push(text.slice(last, m.index))
    parts.push({ name: m[1] })
    last = m.index! + m[0].length
  }
  if (last < text.length) parts.push(text.slice(last))
  return <>{parts.map((p, i) => (typeof p === 'string' ? p : <span key={i} className="mention">@{p.name}</span>))}</>
}
