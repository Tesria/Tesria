import { useState } from 'react'
import { EMOJI, type Emoji } from '../editor/emoji'
import { useDismissable } from '../hooks/useDismissable'

/**
 * Markers for pages that are read in order, or listed rather than
 * illustrated (2026-09-23): numbered steps in the tree, or a
 * plain bullet where a picture would be noise. All are single characters
 * the server already accepts as a page's emoji.
 */
const NUMBERS: Emoji[] = [
  ['1️⃣', 'one'], ['2️⃣', 'two'], ['3️⃣', 'three'], ['4️⃣', 'four'], ['5️⃣', 'five'],
  ['6️⃣', 'six'], ['7️⃣', 'seven'], ['8️⃣', 'eight'], ['9️⃣', 'nine'], ['🔟', 'ten'], ['0️⃣', 'zero'],
].map(([char, name]) => ({ char, name }))
const BULLETS: Emoji[] = [
  ['•', 'bullet'], ['◦', 'hollow bullet'], ['▪', 'small square'], ['▫', 'hollow square'], ['▸', 'small triangle'],
  ['➤', 'arrow'], ['◆', 'diamond'], ['◇', 'hollow diamond'], ['🔹', 'blue diamond'], ['🔸', 'orange diamond'],
  ['★', 'star'], ['✔', 'check'],
].map(([char, name]) => ({ char, name }))

/**
 * A page's emoji, before its title (dev-plan 15.7). Readers see just the
 * emoji. Someone who may edit the page gets it as a button, or an
 * "Add emoji" button when there is none, which opens a picker: the editor's
 * emoji, searchable by name, and a box to paste any other.
 *
 * Saves on every choice, like a space's icon: picking an emoji is the
 * decision, and a Save button after it would be a second click for nothing.
 */
export function PageEmoji({ emoji, canEdit, onChange }: {
  emoji: string | null | undefined
  canEdit: boolean
  onChange: (emoji: string | null) => Promise<void>
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [pasted, setPasted] = useState('')
  const [error, setError] = useState<string | null>(null)
  const ref = useDismissable<HTMLDivElement>(open, () => setOpen(false))

  if (!canEdit) return emoji ? <span className="page-emoji" aria-hidden="true">{emoji}</span> : null

  const q = query.trim().toLowerCase()
  // A search covers every group, so "three" or "arrow" finds a marker too.
  const shown = q
    ? [...EMOJI, ...NUMBERS, ...BULLETS].filter((e) => e.name.includes(q) || e.keywords?.some((k) => k.includes(q)))
    : EMOJI

  async function choose(value: string | null) {
    setError(null)
    try {
      await onChange(value)
      setOpen(false)
      setQuery('')
      setPasted('')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save the emoji.')
    }
  }

  return (
    <div className="page-emoji-picker" ref={ref}>
      {emoji ? (
        <button type="button" className="page-emoji page-emoji--button" title="Change the emoji" aria-label={`Emoji ${emoji}: change it`} onClick={() => setOpen((v) => !v)}>
          {emoji}
        </button>
      ) : (
        <button type="button" className="link-btn page-emoji__add" onClick={() => setOpen((v) => !v)}>
          Add emoji
        </button>
      )}
      {open && (
        <div className="page-emoji__panel" role="dialog" aria-label="Choose an emoji for this page">
          <input
            className="page-emoji__search"
            placeholder="Search, such as rocket or book"
            value={query}
            autoFocus
            onChange={(e) => setQuery(e.target.value)}
          />
          <div className="page-emoji__sections">
            {q && <Choices label="Matches" items={shown} current={emoji} onPick={choose} empty="No match. Paste one below." />}
            {!q && (
              <>
                <Choices label="Emoji" items={shown} current={emoji} onPick={choose} />
                <Choices label="Numbers" items={NUMBERS} current={emoji} onPick={choose} />
                <Choices label="Bullets" items={BULLETS} current={emoji} onPick={choose} />
              </>
            )}
          </div>
          <form
            className="page-emoji__paste"
            onSubmit={(e) => { e.preventDefault(); e.stopPropagation(); if (pasted.trim()) void choose(pasted.trim()) }}
          >
            <input placeholder="Or paste any emoji" value={pasted} onChange={(e) => setPasted(e.target.value)} aria-label="Paste any emoji" />
            <button type="submit" className="btn btn--sm" disabled={!pasted.trim()}>Use</button>
          </form>
          {error && <p className="alert alert--error">{error}</p>}
          {emoji && <button type="button" className="link-btn link-btn--danger" onClick={() => void choose(null)}>Remove the emoji</button>}
        </div>
      )}
    </div>
  )
}

/** One labeled group of choices in the picker. */
function Choices({ label, items, current, onPick, empty }: {
  label: string
  items: { char: string; name: string }[]
  current: string | null | undefined
  onPick: (value: string) => Promise<void>
  empty?: string
}) {
  return (
    <section className="page-emoji__section" aria-label={label}>
      <h3 className="page-emoji__label">{label}</h3>
      <div className="page-emoji__grid">
        {items.map((e) => (
          <button key={e.char} type="button" className={e.char === current ? 'page-emoji__choice is-active' : 'page-emoji__choice'} title={e.name} aria-label={e.name} onClick={() => void onPick(e.char)}>
            {e.char}
          </button>
        ))}
      </div>
      {items.length === 0 && empty && <p className="muted small">{empty}</p>}
    </section>
  )
}
