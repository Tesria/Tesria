import { useState } from 'react'
import { NodeViewContent, NodeViewWrapper, type ReactNodeViewProps } from '@tiptap/react'
import { codeLanguages, MERMAID_LANGUAGE } from './lowlight'
import { MermaidDiagram } from './MermaidView'
import { AppearancePicker } from './AppearancePicker'
import { appearanceData } from './appearance'
import { CODE_SCHEMES, CODE_SCHEME_LABELS, codeSchemeData, isCodeScheme, preferredCodeScheme, setPreferredCodeScheme } from './codeSchemes'
import { api } from '../api/client'

/** Code-block node view: a language picker + line-number/copy buttons above the code. */
export function CodeBlockView({ node, updateAttributes, editor }: ReactNodeViewProps) {
  // Readers get the language as a label, not a picker: a change there went
  // nowhere, since the page is not being edited (found 2026-09-23).
  const editable = editor.isEditable
  const [copied, setCopied] = useState(false)
  const language = (node.attrs.language as string | null) ?? 'plaintext'
  const lineNumbers = Boolean(node.attrs.lineNumbers)
  const lineCount = node.textContent ? node.textContent.split('\n').length : 1
  // Mermaid is a code block whose *output* is a diagram: the source stays
  // editable and exportable as an ordinary fenced block, and the drawing is
  // a view of it. A second node type would have meant a second export case
  // and a second thing to paste into.
  const isMermaid = language === MERMAID_LANGUAGE
  const [showSource, setShowSource] = useState(false)

  async function copyCode() {
    await navigator.clipboard.writeText(node.textContent)
    setCopied(true)
    setTimeout(() => setCopied(false), 1500)
  }

  return (
    <NodeViewWrapper
      className={isMermaid ? 'code-block code-block--diagram' : 'code-block'}
      {...appearanceData(node.attrs.appearance)}
      // A diagram draws rather than highlights, so it has no colors to choose.
      {...(isMermaid ? {} : codeSchemeData(node.attrs.colorScheme))}
    >
      <div className="code-block__header" contentEditable={false}>
        {editable ? (
          // The name and a chevron beside it, with the real select laid over
          // them unseen. A select is as wide as its longest option (Mermaid
          // Diagram), which left the chevron far from a short name like
          // Python, most visibly on an iPhone (the owner, 2026-09-29). A tap
          // still lands on the select, so each device opens its own picker.
          <span className="code-block__lang code-block__lang--picker">
            <span aria-hidden="true">{codeLanguages.find((l) => l.value === language)?.label ?? language}</span>
            <svg className="code-block__lang-chevron" viewBox="0 0 10 6" width="10" height="6" aria-hidden="true">
              <path d="M1 1l4 4 4-4" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
            </svg>
            <select
              className="code-block__lang-select"
              aria-label="Language"
              value={language}
              onChange={(e) => updateAttributes({ language: e.target.value })}
            >
              {codeLanguages.map((l) => (
                <option key={l.value} value={l.value}>{l.label}</option>
              ))}
            </select>
          </span>
        ) : (
          <span className="code-block__lang code-block__lang--label">
            {codeLanguages.find((l) => l.value === language)?.label ?? language}
          </span>
        )}
        <div className="code-block__header-actions">
          {editable && !isMermaid && (
            <label className="appearance-picker code-block__appearance" title="This block's colors">
              <span>Colors</span>
              <select
                value={isCodeScheme(node.attrs.colorScheme) ? node.attrs.colorScheme : 'default'}
                onChange={(e) => {
                  const scheme = e.target.value
                  updateAttributes({ colorScheme: scheme })
                  // Remembered for this person's next new block. A failure
                  // costs only that: this block has its colors either way.
                  if (scheme !== preferredCodeScheme()) {
                    setPreferredCodeScheme(scheme)
                    api.auth.setEditorPreference(scheme).catch(() => {})
                  }
                }}
              >
                {CODE_SCHEMES.map((c) => <option key={c} value={c}>{CODE_SCHEME_LABELS[c]}</option>)}
              </select>
            </label>
          )}
          {editable && (
            <AppearancePicker
              className="code-block__appearance"
              value={node.attrs.appearance}
              onChange={(appearance) => updateAttributes({ appearance })}
            />
          )}
          {isMermaid && (
            <button
              type="button"
              className={showSource ? 'code-block__linenum-toggle is-active' : 'code-block__linenum-toggle'}
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => setShowSource((v) => !v)}
              title={showSource ? 'Show the diagram' : 'Edit the source'}
            >
              {showSource ? 'Diagram' : 'Source'}
            </button>
          )}
          {!isMermaid && editable && (
          <button
            type="button"
            className={lineNumbers ? 'code-block__linenum-toggle is-active' : 'code-block__linenum-toggle'}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => updateAttributes({ lineNumbers: !lineNumbers })}
            title={lineNumbers ? 'Hide Line Numbers' : 'Show Line Numbers'}
          >
            #
          </button>
          )}
          <button type="button" className="code-block__copy" onMouseDown={(e) => e.preventDefault()} onClick={copyCode}>
            {copied ? 'Copied' : 'Copy'}
          </button>
        </div>
      </div>
      {/* The source is always in the document and always mounted: hiding it
          with `hidden` rather than unmounting it keeps ProseMirror's view of
          the node intact, which it needs to stay editable. */}
      {isMermaid && !showSource && (
        <div className="mermaid" contentEditable={false}>
          <MermaidDiagram source={node.textContent} />
        </div>
      )}
      <div className="code-block__body" hidden={isMermaid && !showSource}>
        {lineNumbers && !isMermaid && (
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
