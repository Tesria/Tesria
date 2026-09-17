import { useEffect, useState } from 'react'
import { BubbleMenu } from '@tiptap/react/menus'
import { useEditorState, type Editor as TiptapEditor } from '@tiptap/react'
import { NodeSelection } from '@tiptap/pm/state'
import { normaliseTocOptions, TOC_BULLET_LABELS, TOC_BULLET_STYLES, type TocOptions } from './tocOptions'

const LEVELS = [1, 2, 3, 4, 5, 6]

/**
 * Settings for the selected table of contents, laid out like Confluence's
 * macro editor: the basic options, then the advanced ones behind a toggle.
 */
export function TocMenu({ editor }: { editor: TiptapEditor }) {
  const current = useEditorState({
    editor,
    selector: ({ editor }) => {
      const { selection } = editor.state
      if (!(selection instanceof NodeSelection) || selection.node.type.name !== 'tableOfContents') return null
      return { pos: selection.from, options: normaliseTocOptions(selection.node.attrs) }
    },
    equalityFn: (a, b) => b !== null && JSON.stringify(a) === JSON.stringify(b),
  })
  const [advanced, setAdvanced] = useState(false)
  const [drafts, setDrafts] = useState({ indent: '', include: '', exclude: '', cssClass: '' })

  // Text fields edit a local draft and commit on blur or Enter: committing per
  // keystroke would rewrite the node (and redraw the list) on every letter.
  useEffect(() => {
    if (!current) return
    setDrafts({
      indent: current.options.indent, include: current.options.include,
      exclude: current.options.exclude, cssClass: current.options.cssClass,
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [current?.pos, current?.options.indent, current?.options.include, current?.options.exclude, current?.options.cssClass])

  function set(patch: Partial<TocOptions>) {
    if (!current) return
    // Re-select afterwards: updating attributes rewrites the node, and a
    // NodeSelection does not survive that (see selectedNode.ts).
    editor.chain().updateAttributes('tableOfContents', patch).setNodeSelection(current.pos).run()
  }

  const o = current?.options
  const text = (key: keyof typeof drafts, placeholder: string) => (
    <input
      value={drafts[key]}
      placeholder={placeholder}
      onChange={(e) => setDrafts((d) => ({ ...d, [key]: e.target.value }))}
      onBlur={() => o && drafts[key] !== o[key] && set({ [key]: drafts[key] })}
      onKeyDown={(e) => {
        if (e.key === 'Enter') { e.preventDefault(); set({ [key]: drafts[key] }) }
      }}
    />
  )

  return (
    <BubbleMenu
      editor={editor}
      pluginKey="tocMenu"
      shouldShow={({ editor }) => editor.state.selection instanceof NodeSelection && editor.isActive('tableOfContents')}
      // Below, not above: a table of contents usually sits near the top of
      // a page, and a panel above it went under the sticky toolbar.
      options={{ placement: 'bottom-start' }}
    >
      {o && (
        <div className="chip-menu dynamic-block-menu toc-menu">
          <p className="dynamic-block-menu__title">Table of contents</p>
          <label className="dynamic-block-menu__field">
            <span>Display as</span>
            <select value={o.display} onChange={(e) => set({ display: e.target.value as TocOptions['display'] })}>
              <option value="vertical">Vertical list</option>
              <option value="horizontal">Horizontal list</option>
            </select>
          </label>
          <label className="dynamic-block-menu__field">
            <span>Bullet style</span>
            <select
              value={o.bulletStyle}
              disabled={o.display === 'horizontal'}
              onChange={(e) => set({ bulletStyle: e.target.value as TocOptions['bulletStyle'] })}
            >
              {TOC_BULLET_STYLES.map((s) => <option key={s} value={s}>{TOC_BULLET_LABELS[s]}</option>)}
            </select>
          </label>
          <div className="dynamic-block-menu__field toc-menu__levels">
            <span>Heading levels</span>
            <span className="toc-menu__range">
              <select aria-label="From heading level" value={o.minLevel} onChange={(e) => set({ minLevel: Number(e.target.value), maxLevel: Math.max(Number(e.target.value), o.maxLevel) })}>
                {LEVELS.map((l) => <option key={l} value={l}>{l}</option>)}
              </select>
              <span>to</span>
              <select aria-label="To heading level" value={o.maxLevel} onChange={(e) => set({ maxLevel: Number(e.target.value), minLevel: Math.min(Number(e.target.value), o.minLevel) })}>
                {LEVELS.map((l) => <option key={l} value={l}>{l}</option>)}
              </select>
            </span>
          </div>
          <label className="toc-menu__check">
            <input type="checkbox" checked={o.sectionNumbers} onChange={(e) => set({ sectionNumbers: e.target.checked })} />
            <span>Include section numbers</span>
          </label>

          <button type="button" className="link-btn toc-menu__advanced" onClick={() => setAdvanced((v) => !v)} aria-expanded={advanced}>
            {advanced ? 'Hide advanced' : 'Advanced'}
          </button>
          {advanced && (
            <>
              <label className="dynamic-block-menu__field">
                <span>Indent headings</span>
                {text('indent', '10px')}
              </label>
              <label className="dynamic-block-menu__field">
                <span>Include headings with</span>
                {text('include', 'Step*|Setup')}
              </label>
              <label className="dynamic-block-menu__field">
                <span>Exclude headings with</span>
                {text('exclude', 'Appendix*')}
              </label>
              <p className="toc-menu__hint">Case sensitive. <code>*</code> matches anything, <code>|</code> separates alternatives.</p>
              <label className="dynamic-block-menu__field">
                <span>CSS class name</span>
                {text('cssClass', 'my-toc')}
              </label>
              <label className="toc-menu__check">
                <input type="checkbox" checked={o.excludeInPdf} onChange={(e) => set({ excludeInPdf: e.target.checked })} />
                <span>Exclude in PDF export</span>
              </label>
            </>
          )}
        </div>
      )}
    </BubbleMenu>
  )
}
