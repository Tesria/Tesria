import { useEffect, useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { NodeSelection } from '@tiptap/pm/state'
import type { BlockParams } from './dynamicBlock'
import { kindOf, type ParamField } from './dynamicBlockKinds'
import { api, type SearchResult } from '../api/client'

/**
 * Edits the selected dynamic block's parameters. One form for every kind,
 * generated from the kind's declared `params`: nobody writes a menu for a
 * new kind (architecture.md, "Dynamic blocks", decision 8).
 */
export function DynamicBlockMenu({ editor }: { editor: TiptapEditor }) {
  const current = useEditorState({
    editor,
    selector: ({ editor }) => {
      const { selection } = editor.state
      if (!(selection instanceof NodeSelection) || selection.node.type.name !== 'dynamicBlock') return null
      return { pos: selection.from, kind: String(selection.node.attrs.kind), params: (selection.node.attrs.params ?? {}) as BlockParams }
    },
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })
  const meta = current ? kindOf(current.kind) : undefined

  function set(key: string, value: string) {
    if (!current) return
    const params = { ...current.params, [key]: value }
    // Re-select the node afterwards: updating its attributes rewrites its
    // markup and a NodeSelection does not survive that (see selectedNode.ts).
    editor.chain().updateAttributes('dynamicBlock', { params }).setNodeSelection(current.pos).run()
  }

  return (
    <BubbleMenu
      className="floating-menu"
      editor={editor}
      pluginKey="dynamicBlockMenu"
      shouldShow={({ editor }) => editor.state.selection instanceof NodeSelection && editor.isActive('dynamicBlock')}
      options={{ placement: 'top-start' }}
    >
      <div className="chip-menu dynamic-block-menu">
        <p className="dynamic-block-menu__title">{meta?.title ?? current?.kind}</p>
        {meta && meta.params.length === 0 && <p className="dynamic-block__note">No options.</p>}
        {meta?.params.map((field) => field.type === 'page' ? (
          // Not a <label>: the picker holds buttons, and a label would send
          // every click on them to the search box instead.
          <div key={field.key} className="dynamic-block-menu__field">
            <span>{field.label}</span>
            <PagePicker value={current?.params[field.key]} onChange={(v) => set(field.key, v)} />
          </div>
        ) : (
          <label key={field.key} className="dynamic-block-menu__field">
            <span>{field.label}</span>
            <Field field={field} value={current?.params[field.key]} onChange={(v) => set(field.key, v)} />
          </label>
        ))}
      </div>
    </BubbleMenu>
  )
}

function Field({ field, value, onChange }: { field: ParamField; value: string | undefined; onChange: (v: string) => void }) {
  if (field.type === 'select') {
    return (
      <select value={value ?? field.default} onChange={(e) => onChange(e.target.value)}>
        {field.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    )
  }
  if (field.type === 'number') {
    return (
      <input type="number" min={field.min} max={field.max} value={value ?? String(field.default)}
        onChange={(e) => onChange(e.target.value)} />
    )
  }
  if (field.type === 'labels') {
    return <input value={value ?? field.default ?? ''} placeholder="release, api" onChange={(e) => onChange(e.target.value)} />
  }
  // Page fields are drawn by PagePicker above, never here.
  const placeholder = field.type === 'text' ? field.placeholder : undefined
  return <input value={value ?? field.default ?? ''} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
}

/**
 * Picks a page by searching for it (dev-plan 10.5 step 1), for Include page
 * and Excerpt include, which asked for a raw page id: something only the
 * address bar knows. The param is still the one id string, so blocks written
 * before this keep working, and an id can still be pasted into the search.
 */
function PagePicker({ value, onChange }: { value: string | undefined; onChange: (v: string) => void }) {
  const [title, setTitle] = useState<string | null>(null)
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])

  // The chosen page's title, rather than its id.
  useEffect(() => {
    setTitle(null)
    if (!value) return
    let cancelled = false
    api.pages.get(value)
      .then((p) => !cancelled && setTitle(p.title))
      .catch(() => !cancelled && setTitle(''))
    return () => { cancelled = true }
  }, [value])

  useEffect(() => {
    const q = query.trim()
    // A pasted id is taken as it is: the old way still works.
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(q)) {
      onChange(q)
      setQuery('')
      return
    }
    if (q.length < 2) { setResults([]); return }
    let cancelled = false
    const timer = window.setTimeout(() => {
      api.search(q)
        .then((r) => !cancelled && setResults(r.slice(0, 8)))
        .catch(() => !cancelled && setResults([]))
    }, 250)
    return () => { cancelled = true; window.clearTimeout(timer) }
    // onChange is a fresh function each render; the query is what matters.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query])

  return (
    <div className="page-picker">
      <p className="page-picker__current">
        {!value ? <span className="muted">No page chosen</span>
          : title === null ? <span className="muted">Loading…</span>
            : title === '' ? <span className="muted">A page you cannot see, or that was deleted</span>
              : <strong>{title}</strong>}
      </p>
      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search for a page"
        aria-label="Search for a page"
      />
      {results.length > 0 && (
        <ul className="page-picker__results" role="listbox">
          {results.map((r) => (
            <li key={r.pageId}>
              <button type="button" role="option" aria-selected={r.pageId === value}
                onClick={() => { onChange(r.pageId); setQuery(''); setResults([]) }}>
                {r.title} <span className="badge">{r.spaceKey}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {query.trim().length >= 2 && results.length === 0 && <p className="muted small">No matching pages.</p>}
    </div>
  )
}
