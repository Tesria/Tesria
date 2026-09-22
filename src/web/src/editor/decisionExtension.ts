import { Node, mergeAttributes } from '@tiptap/core'

declare module '@tiptap/core' {
  interface Commands<ReturnType> {
    decision: {
      /** Wrap the current block(s) in a decision. */
      setDecision: () => ReturnType
      unsetDecision: () => ReturnType
    }
  }
}

/**
 * Decision: a block that records something agreed, with a fixed check icon.
 * Same construction as a panel (index.css `.decision`, icon as a `::before`
 * mask), and no attributes: a decision has no type to choose.
 */
export const Decision = Node.create({
  name: 'decision',
  group: 'block',
  content: 'block+',
  defining: true,

  parseHTML() {
    return [{ tag: 'div[data-type="decision"]' }]
  },

  renderHTML({ HTMLAttributes }) {
    return ['div', mergeAttributes(HTMLAttributes, { 'data-type': 'decision', class: 'decision' }), 0]
  },

  addCommands() {
    return {
      setDecision:
        () =>
        ({ commands }) =>
          commands.wrapIn(this.name),
      unsetDecision:
        () =>
        ({ commands }) =>
          commands.lift(this.name),
    }
  },
})
