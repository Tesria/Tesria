import { useEditor, EditorContent } from '@tiptap/react'
import StarterKit from '@tiptap/starter-kit'
import { useEffect } from 'react'
import { Toolbar } from './Toolbar'

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

