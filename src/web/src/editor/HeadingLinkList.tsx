import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { collectHeadingAnchors } from './headingAnchors'

/**
 * The "link to a heading on this page" list under a link popover's URL
 * field. Picking one fills the field with `#slug`; the caller still
 * applies it, so the person can see what they chose.
 */
export function HeadingLinkList({ editor, onPick }: { editor: TiptapEditor; onPick: (href: string) => void }) {
  const headings = useEditorState({
    editor,
    selector: ({ editor }) => collectHeadingAnchors(editor.state.doc).map(({ level, text, id }) => ({ level, text, id })),
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })
  if (headings.length === 0) return null

  return (
    <div className="link-anchors">
      <p className="link-anchors__title">Headings on this page</p>
      {headings.map((h) => (
        <button
          key={h.id}
          type="button"
          className="link-anchors__item"
          style={{ paddingLeft: `${0.5 + (h.level - 1) * 0.6}rem` }}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => onPick(`#${h.id}`)}
        >
          {h.text || 'Untitled heading'}
        </button>
      ))}
    </div>
  )
}
