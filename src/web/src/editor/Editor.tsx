import { useEditor, EditorContent, type Editor as TiptapEditor } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import { useEffect } from 'react'

type Props = {
  /** ProseMirror document as a JSON string. */
  value: string
  editable?: boolean
  /** Called with the updated document JSON string on every edit. */
  onChange?: (json: string) => void
}

function parseDoc(value: string): object | undefined {
  if (!value) return undefined
  try {
    return JSON.parse(value) as object
  } catch {
    return undefined
  }
}

/**
 * Block WYSIWYG editor (TipTap/ProseMirror). Used both for editing pages and,
 * with `editable={false}`, for rendering stored content read-only.
 */
export function Editor({ value, editable = true, onChange }: Props) {
  const editor = useEditor({
    extensions: [StarterKit],
    content: parseDoc(value),
    editable,
    onUpdate: ({ editor }) => onChange?.(JSON.stringify(editor.getJSON())),
  })

  // Keep the editor in sync when the source changes externally (e.g. switching
  // which version is previewed). Guarded so it never clobbers active typing.
  useEffect(() => {
    if (!editor || editable) return
    editor.commands.setContent(parseDoc(value) ?? { type: 'doc', content: [] })
  }, [editor, editable, value])

  useEffect(() => {
    editor?.setEditable(editable)
  }, [editor, editable])

  return (
    <div className={editable ? 'editor editor--editable' : 'editor'}>
      {editable && editor && <Toolbar editor={editor} />}
      <EditorContent editor={editor} className="editor__content" />
    </div>
  )
}

function Toolbar({ editor }: { editor: TiptapEditor }) {
  const btn = (label: string, isActive: boolean, onClick: () => void, title: string) => (
    <button
      type="button"
      className={isActive ? 'toolbar__btn is-active' : 'toolbar__btn'}
      onMouseDown={(e) => e.preventDefault()} // keep the editor selection
      onClick={onClick}
      title={title}
    >
      {label}
    </button>
  )
  return (
    <div className="toolbar">
      {btn('B', editor.isActive('bold'), () => editor.chain().focus().toggleBold().run(), 'Bold')}
      {btn('I', editor.isActive('italic'), () => editor.chain().focus().toggleItalic().run(), 'Italic')}
      {btn('S', editor.isActive('strike'), () => editor.chain().focus().toggleStrike().run(), 'Strikethrough')}
      {btn('Code', editor.isActive('code'), () => editor.chain().focus().toggleCode().run(), 'Inline code')}
      <span className="toolbar__sep" />
      {btn('H1', editor.isActive('heading', { level: 1 }), () => editor.chain().focus().toggleHeading({ level: 1 }).run(), 'Heading 1')}
      {btn('H2', editor.isActive('heading', { level: 2 }), () => editor.chain().focus().toggleHeading({ level: 2 }).run(), 'Heading 2')}
      {btn('H3', editor.isActive('heading', { level: 3 }), () => editor.chain().focus().toggleHeading({ level: 3 }).run(), 'Heading 3')}
      <span className="toolbar__sep" />
      {btn('• List', editor.isActive('bulletList'), () => editor.chain().focus().toggleBulletList().run(), 'Bullet list')}
      {btn('1. List', editor.isActive('orderedList'), () => editor.chain().focus().toggleOrderedList().run(), 'Ordered list')}
      {btn('❝', editor.isActive('blockquote'), () => editor.chain().focus().toggleBlockquote().run(), 'Blockquote')}
      {btn('{ }', editor.isActive('codeBlock'), () => editor.chain().focus().toggleCodeBlock().run(), 'Code block')}
    </div>
  )
}
