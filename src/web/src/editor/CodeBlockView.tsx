import { useState } from 'react'
import { NodeViewContent, NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { codeLanguages } from './lowlight'

/** Code-block node view: a language picker + copy button above the code. */
export function CodeBlockView({ node, updateAttributes }: ReactNodeViewProps) {
  const [copied, setCopied] = useState(false)
  const language = (node.attrs.language as string | null) ?? 'plaintext'

  async function copyCode() {
    await navigator.clipboard.writeText(node.textContent)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <NodeViewWrapper className="code-block">
      <div className="code-block__header" contentEditable={false}>
        <select
          className="code-block__lang"
          value={language}
          onChange={(e) => updateAttributes({ language: e.target.value })}
        >
          {codeLanguages.map((l) => (
            <option key={l.value} value={l.value}>{l.label}</option>
          ))}
        </select>
        <button type="button" className="code-block__copy" onMouseDown={(e) => e.preventDefault()} onClick={copyCode}>
          {copied ? 'Copied!' : 'Copy'}
        </button>
      </div>
      <pre>
        <NodeViewContent<'code'> as="code" />
      </pre>
    </NodeViewWrapper>
  )
}
