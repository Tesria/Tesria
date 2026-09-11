import { NodeViewWrapper, useEditorState, type ReactNodeViewProps } from '@tiptap/react'
import { collectHeadingAnchors, scrollToAnchor } from './headingAnchors'

type Entry = { level: number; text: string; id: string }
type TreeNode = Entry & { children: TreeNode[] }

/** Nest a flat, document-ordered heading list by level — an H3 under the H2 before it, and so on. */
function toTree(entries: Entry[]): TreeNode[] {
  const roots: TreeNode[] = []
  const stack: TreeNode[] = []
  for (const e of entries) {
    const node: TreeNode = { ...e, children: [] }
    while (stack.length > 0 && stack[stack.length - 1].level >= e.level) stack.pop()
    if (stack.length === 0) roots.push(node)
    else stack[stack.length - 1].children.push(node)
    stack.push(node)
  }
  return roots
}

/**
 * Renders the live outline. Re-renders only when the heading list itself
 * changes (the equality check below), not on every keystroke elsewhere.
 */
export function TocView({ editor }: ReactNodeViewProps) {
  const headings = useEditorState({
    editor,
    selector: ({ editor }) => collectHeadingAnchors(editor.state.doc).map(({ level, text, id }) => ({ level, text, id })),
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })

  const renderList = (nodes: TreeNode[]) => (
    <ul className="toc__list">
      {nodes.map((n) => (
        <li key={n.id}>
          <a
            href={`#${n.id}`}
            onClick={(e) => {
              e.preventDefault()
              scrollToAnchor(editor.view.dom, n.id)
            }}
          >
            {n.text || 'Untitled heading'}
          </a>
          {n.children.length > 0 && renderList(n.children)}
        </li>
      ))}
    </ul>
  )

  return (
    <NodeViewWrapper className="toc" contentEditable={false}>
      <p className="toc__title">On this page</p>
      {headings.length === 0 ? (
        <p className="toc__empty">Headings on this page will be listed here.</p>
      ) : (
        renderList(toTree(headings))
      )}
    </NodeViewWrapper>
  )
}
