import { lazy, Suspense, useCallback, useEffect, useState } from 'react'
import { NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { Link } from 'react-router-dom'
import { api, ApiError, type BlockCell, type BlockItem, type BlockResult } from '../api/client'
import { getDynamicBlockStorage, type BlockParams } from './dynamicBlock'
import { kindOf } from './dynamicBlockKinds'
import { Avatar } from '../components/Avatar'

// A `document`-shaped result is drawn by a nested read-only editor. Lazy,
// because Editor imports the schema, which imports this file: a static
// import would be a cycle at module-evaluation time.
const NestedEditor = lazy(() => import('./Editor').then((m) => ({ default: m.Editor })))

type State =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'loaded'; result: BlockResult }
  | { status: 'error'; message: string }

/**
 * The one renderer for every kind (architecture.md, "Dynamic blocks",
 * decision 2): it draws the three neutral shapes and knows nothing about any
 * particular kind. Fetches on mount and whenever the block's attributes
 * change; never writes anything back into the document.
 */
export function DynamicBlockView({ node, editor, selected }: ReactNodeViewProps) {
  const kind = String(node.attrs.kind ?? '')
  const params = (node.attrs.params ?? {}) as BlockParams
  const meta = kindOf(kind)
  const [state, setState] = useState<State>({ status: 'idle' })
  // Params are an object attr; compare by value so a re-render with an
  // identical object does not refetch.
  const paramsKey = JSON.stringify(params)

  const load = useCallback(async () => {
    const getPageId = getDynamicBlockStorage(editor)?.getPageId
    if (!getPageId) {
      setState({ status: 'idle' })
      return
    }
    setState({ status: 'loading' })
    try {
      const result = await api.blocks.get(await getPageId(), kind, JSON.parse(paramsKey) as BlockParams)
      setState({ status: 'loaded', result })
    } catch (err) {
      setState({ status: 'error', message: err instanceof ApiError ? err.message : 'Could not load this block.' })
    }
  }, [editor, kind, paramsKey])

  useEffect(() => {
    void load()
  }, [load])

  const title = meta?.title ?? kind
  return (
    <NodeViewWrapper className={selected ? 'dynamic-block is-selected' : 'dynamic-block'} data-kind={kind} contentEditable={false}>
      <div className="dynamic-block__head">
        <span className="dynamic-block__kind">{title}</span>
        {state.status === 'loaded' && (
          <button type="button" className="dynamic-block__refresh" onMouseDown={(e) => e.preventDefault()} onClick={() => void load()} title="Refresh">↻</button>
        )}
      </div>
      {state.status === 'idle' && <p className="dynamic-block__note">Shown on the page.</p>}
      {state.status === 'loading' && <p className="dynamic-block__note">Loading…</p>}
      {state.status === 'error' && <p className="dynamic-block__note dynamic-block__note--error">{state.message}</p>}
      {state.status === 'loaded' && <Body result={state.result} />}
    </NodeViewWrapper>
  )
}

function Body({ result }: { result: BlockResult }) {
  if (result.shape === 'document') {
    if (!result.document) return <p className="dynamic-block__note">{result.empty ?? 'Nothing to show.'}</p>
    return (
      <Suspense fallback={<p className="dynamic-block__note">Loading…</p>}>
        {/* No host page is stashed on the nested editor, so any blocks inside
            the included content show as placeholders — depth 1, by construction. */}
        <NestedEditor value={result.document} editable={false} />
      </Suspense>
    )
  }
  if (result.items.length === 0) return <p className="dynamic-block__note">{result.empty ?? 'Nothing to show.'}</p>
  if (result.shape === 'table') {
    const columns = result.columns ?? []
    return (
      <div className="tableWrapper">
        <table>
          <thead><tr>{columns.map((c) => <th key={c.key}>{c.label}</th>)}</tr></thead>
          <tbody>
            {result.items.map((item, i) => (
              <tr key={i}>{columns.map((c) => <td key={c.key}><Cell cell={item.cells?.[c.key]} /></td>)}</tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }
  return <ItemList items={result.items} />
}

function ItemList({ items }: { items: BlockItem[] }) {
  return (
    <ul className="dynamic-block__list">
      {items.map((item, i) => (
        <li key={item.href ?? i}>
          {item.href ? <Link to={item.href}>{item.title}</Link> : item.title}
          {item.subtitle && <span className="dynamic-block__subtitle">{item.subtitle}</span>}
          {item.children && item.children.length > 0 && <ItemList items={item.children} />}
        </li>
      ))}
    </ul>
  )
}

function Cell({ cell }: { cell: BlockCell | undefined }) {
  if (!cell) return null
  if (cell.user) return <span className="dynamic-block__user"><Avatar subject={cell.user} size={18} />{cell.user.displayName}</span>
  if (cell.date) return <>{new Date(cell.date).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })}</>
  if (typeof cell.checked === 'boolean') return <input type="checkbox" checked={cell.checked} readOnly />
  if (cell.href) return cell.href.startsWith('/') ? <Link to={cell.href}>{cell.text}</Link> : <a href={cell.href}>{cell.text}</a>
  return <>{cell.text}</>
}
