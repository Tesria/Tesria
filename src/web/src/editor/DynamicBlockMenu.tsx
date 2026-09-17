import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { NodeSelection } from '@tiptap/pm/state'
import type { BlockParams } from './dynamicBlock'
import { kindOf, type ParamField } from './dynamicBlockKinds'

/**
 * Edits the selected dynamic block's parameters. One form for every kind,
 * generated from the kind's declared `params` — nobody writes a menu for a
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
        {meta?.params.map((field) => (
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
  if (field.type === 'page') {
    // A page id today. The obvious upgrade is a picker; the contract does not
    // change when it lands, because the param is still one string.
    return <input value={value ?? field.default ?? ''} placeholder="Page id" onChange={(e) => onChange(e.target.value)} />
  }
  return <input value={value ?? field.default ?? ''} placeholder={field.placeholder} onChange={(e) => onChange(e.target.value)} />
}
