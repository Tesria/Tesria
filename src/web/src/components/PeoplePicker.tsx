import { useId, useRef, useState } from 'react'
import type { Directory } from '../api/client'
import { matchPeople, personLabel } from './peopleMatch'

/**
 * Chooses several people at once (dev-plan 21.2): the ones chosen as chips
 * that can be taken off, a search box, and the people it matches as buttons
 * that add them. `fixed` people are shown as chosen and cannot be taken off
 * (the creator of a space, in its Admins).
 */
export function PeoplePicker({ people, chosen, onChange, fixed = [], label }: {
  people: Directory[]
  chosen: string[]
  onChange: (ids: string[]) => void
  fixed?: string[]
  /** What the search box is for, for a screen reader: "Add people to Editors". */
  label: string
}) {
  const [query, setQuery] = useState('')
  // The matches show once the box is used, so four pickers on one step are
  // four search boxes rather than four lists of everyone.
  const [browsing, setBrowsing] = useState(false)
  const listId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const byId = new Map(people.map((p) => [p.id, p]))
  const taken = new Set([...fixed, ...chosen])
  const { shown, more } = matchPeople(people, query, taken)

  function add(id: string) {
    onChange([...chosen, id])
    setQuery('')
    // Back to the box, for the next name: the button just used is gone.
    inputRef.current?.focus()
  }

  return (
    // The matches stay open once shown: folding them when focus leaves
    // moved the buttons below between press and release, so a click on
    // Continue landed on nothing (found in the live check).
    <div className="people-picker">
      {taken.size > 0 && (
        <ul className="people-picker__chosen">
          {[...fixed, ...chosen.filter((id) => !fixed.includes(id))].map((id) => {
            const p = byId.get(id)
            const name = p?.displayName ?? 'Someone'
            return (
              <li key={id} className="people-picker__chip">
                <span>{name}</span>
                {fixed.includes(id)
                  ? <span className="muted small">(you)</span>
                  : (
                    <button type="button" className="people-picker__remove" aria-label={`Take ${name} off`}
                      onClick={() => onChange(chosen.filter((c) => c !== id))}>×</button>
                  )}
              </li>
            )
          })}
        </ul>
      )}
      <input
        ref={inputRef}
        type="search"
        value={query}
        placeholder="Find people by name"
        aria-label={label}
        aria-controls={listId}
        onFocus={() => setBrowsing(true)}
        onChange={(e) => setQuery(e.target.value)}
        onKeyDown={(e) => {
          // Enter takes the first match, rather than submitting anything.
          if (e.key === 'Enter') {
            e.preventDefault()
            if (shown[0]) add(shown[0].id)
          }
        }}
      />
      {(browsing || query) && <ul id={listId} className="people-picker__matches">
        {shown.map((p) => (
          <li key={p.id}>
            <button type="button" className="people-picker__match" onClick={() => add(p.id)}>
              <span aria-hidden="true">+</span> {personLabel(p)}
            </button>
          </li>
        ))}
        {shown.length === 0 && (
          <li className="muted small">{query ? 'Nobody else matches.' : 'Everyone is chosen already.'}</li>
        )}
        {more > 0 && <li className="muted small">And {more} more: type part of a name to narrow the list.</li>}
      </ul>}
    </div>
  )
}
