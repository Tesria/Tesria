import { useState } from 'react'
import { NodeViewContent, NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { codeLanguages } from './lowlight'

/** Code-block node view: a language picker + line-number/copy buttons above the code. */
export function CodeBlockView({ node, updateAttributes }: ReactNodeViewProps) {
  const [copied, setCopied] = useState(false)
  const language = (node.attrs.language as string | null) ?? 'plaintext'
  const lineNumbers = Boolean(node.attrs.lineNumbers)
  const lineCount = node.textContent ? node.textContent.split('\n').length : 1

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
        <div className="code-block__header-actions">
          <button
            type="button"
            className={lineNumbers ? 'code-block__linenum-toggle is-active' : 'code-block__linenum-toggle'}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => updateAttributes({ lineNumbers: !lineNumbers })}
            title={lineNumbers ? 'Hide line numbers' : 'Show line numbers'}
          >
            #
          </button>
          <button type="button" className="code-block__copy" onMouseDown={(e) => e.preventDefault()} onClick={copyCode}>
            {copied ? 'Copied!' : 'Copy'}
          </button>
        </div>
      </div>
      <div className="code-block__body">
        {lineNumbers && (
          <div className="code-block__gutter" contentEditable={false} aria-hidden="true">
            {Array.from({ length: lineCount }, (_, i) => (
              <span key={i}>{i + 1}</span>
            ))}
          </div>
        )}
        <pre>
          <NodeViewContent<'code'> as="code" />
        </pre>
      </div>
    </NodeViewWrapper>
  )
}
