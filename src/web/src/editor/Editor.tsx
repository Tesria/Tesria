import { useEditor, EditorContent, type Editor as TiptapEditor } from '@tiptap/react'
import { useEffect, useRef } from 'react'
import { Toolbar } from './Toolbar'
import { TableControls } from './TableControls'
import { getSharedExtensions } from './extensions'
import { handleImageDrop, handleImagePaste } from './imageUpload'

type Props = {
  /** ProseMirror document as a JSON string. */
  value: string
  editable?: boolean
  /** Called with the updated document JSON string on every edit. */
  onChange?: (json: string) => void
  /** Resolves the page id image attachments should be uploaded against (editable mode only). */
  getUploadPageId?: () => Promise<string>
  /** Reports an image upload failure (paste/drop/toolbar), e.g. into a form's error banner. */
  onUploadError?: (message: string) => void
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
export function Editor({ value, editable = true, onChange, getUploadPageId, onUploadError }: Props) {
  // editorProps' handlers close over this ref rather than `editor` directly,
  // since they're set at useEditor's initial options and `editor` doesn't
  // exist yet at that point.
  const editorRef = useRef<TiptapEditor | null>(null)
  const editor = useEditor({
    extensions: getSharedExtensions(),
    content: parseDoc(value),
    editable,
    onUpdate: ({ editor }) => onChange?.(JSON.stringify(editor.getJSON())),
    editorProps: {
      handlePaste: (_view, event) => handleImagePaste(editorRef.current, event, getUploadPageId, onUploadError),
      handleDrop: (_view, event) => handleImageDrop(editorRef.current, event, getUploadPageId, onUploadError),
    },
  })
  useEffect(() => {
    editorRef.current = editor
  }, [editor])

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
      {editable && editor && <Toolbar editor={editor} getUploadPageId={getUploadPageId} onUploadError={onUploadError} />}
      {editable && editor && <TableControls editor={editor} />}
      <EditorContent editor={editor} className="editor__content" />
    </div>
  )
}

