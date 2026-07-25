import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'

/** A small controls bar shown only while the cursor is inside a table. */
export function TableControls({ editor }: { editor: TiptapEditor }) {
  const inTable = useEditorState({
    editor,
    selector: ({ editor }) => editor.isActive('table'),
  })
  if (!inTable) return null

  const act = (label: string, onClick: () => void, title: string, danger = false) => (
    <button
      type="button"
      className={danger ? 'toolbar__btn link-btn--danger' : 'toolbar__btn'}
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      title={title}
    >
      {label}
    </button>
  )

  return (
    <div className="table-controls">
      {act('+ Row above', () => editor.chain().focus().addRowBefore().run(), 'Add row above')}
      {act('+ Row below', () => editor.chain().focus().addRowAfter().run(), 'Add row below')}
      {act('− Row', () => editor.chain().focus().deleteRow().run(), 'Delete row')}
      <span className="toolbar__sep" />
      {act('+ Col left', () => editor.chain().focus().addColumnBefore().run(), 'Add column before')}
      {act('+ Col right', () => editor.chain().focus().addColumnAfter().run(), 'Add column after')}
      {act('− Col', () => editor.chain().focus().deleteColumn().run(), 'Delete column')}
      <span className="toolbar__sep" />
      {act('Delete table', () => editor.chain().focus().deleteTable().run(), 'Delete table', true)}
    </div>
  )
}
