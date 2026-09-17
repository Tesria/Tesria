import { NodeViewWrapper, useEditorState, type ReactNodeViewProps } from '@tiptap/react'
import { collectHeadingAnchors, scrollToAnchor } from './headingAnchors'
import {
  buildTocTree, filterTocHeadings, flattenTocTree, normaliseTocOptions, tocListStyle, type TocEntry,
} from './tocOptions'

/**
 * Renders the live outline with the node's options (tocOptions.ts). Re-renders
 * only when the heading list itself changes (the equality check below), not
 * on every keystroke elsewhere.
 */
export function TocView({ editor, node }: ReactNodeViewProps) {
  const headings = useEditorState({
    editor,
    selector: ({ editor }) => collectHeadingAnchors(editor.state.doc).map(({ level, text, id }) => ({ level, text, id })),
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })
  const options = normaliseTocOptions(node.attrs)
  const tree = buildTocTree(filterTocHeadings(headings, options))

  const link = (e: TocEntry) => (
    <a
      href={`#${e.id}`}
      onClick={(ev) => {
        ev.preventDefault()
        scrollToAnchor(editor.view.dom, e.id)
      }}
    >
      {options.sectionNumbers && <span className="toc__number">{e.number}</span>}
      {e.text || 'Untitled heading'}
    </a>
  )

  const renderList = (entries: TocEntry[], depth: number) => (
    <ul
      className="toc__list"
      style={{
        ...(tocListStyle(options.bulletStyle, depth, options.sectionNumbers) ? { listStyleType: tocListStyle(options.bulletStyle, depth, options.sectionNumbers) } : {}),
        ...(options.indent ? { paddingLeft: options.indent } : {}),
      }}
    >
      {entries.map((e) => (
        <li key={e.id}>
          {link(e)}
          {e.children.length > 0 && renderList(e.children, depth + 1)}
        </li>
      ))}
    </ul>
  )

  const classes = ['toc', `toc--${options.display}`]
  if (options.excludeInPdf) classes.push('toc--exclude-print')
  if (options.cssClass) classes.push(options.cssClass)

  return (
    <NodeViewWrapper className={classes.join(' ')} contentEditable={false}>
      <p className="toc__title">On this page</p>
      {tree.length === 0 ? (
        <p className="toc__empty">
          {headings.length === 0 ? 'Headings on this page will be listed here.' : 'No headings match this table of contents’ settings.'}
        </p>
      ) : options.display === 'horizontal' ? (
        <p className="toc__inline">
          {flattenTocTree(tree).map((e) => <span key={e.id} className="toc__inline-item">{link(e)}</span>)}
        </p>
      ) : (
        renderList(tree, 0)
      )}
    </NodeViewWrapper>
  )
}
