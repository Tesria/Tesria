import { Extension } from '@tiptap/core'
import { Plugin, PluginKey } from '@tiptap/pm/state'

const key = new PluginKey('taskAssignee')

/**
 * An action item's assignee is the first person mentioned inside it: type
 * "@Ana" in a task and it becomes hers, which is exactly how Confluence
 * assigns action items.
 *
 * The mention is the source of truth; `assigneeId`/`assigneeName` on the
 * `taskItem` are a denormalized copy kept in step by this plugin. Wave D's
 * Task report wants to query "tasks assigned to me" without walking every
 * page's document tree looking for mention nodes inside task items, and a
 * stored attribute is the difference between an indexable query and a full
 * scan. Deriving it rather than asking for it twice means the two can never
 * disagree about who a task belongs to.
 */
export const TaskAssignee = Extension.create({
  name: 'taskAssignee',

  addProseMirrorPlugins() {
    return [
      new Plugin({
        key,
        appendTransaction(transactions, _oldState, newState) {
          if (!transactions.some((t) => t.docChanged)) return null

          const tr = newState.tr
          let changed = false

          newState.doc.descendants((node, pos) => {
            if (node.type.name !== 'taskItem') return
            let id: string | null = null
            let name: string | null = null
            node.descendants((child) => {
              if (id !== null || child.type.name !== 'mention') return
              id = (child.attrs.userId as string | null) ?? null
              name = (child.attrs.label as string | null) ?? null
            })
            if (node.attrs.assigneeId === id && node.attrs.assigneeName === name) return
            tr.setNodeMarkup(pos, undefined, { ...node.attrs, assigneeId: id, assigneeName: name })
            changed = true
          })

          // Returning a transaction unconditionally would re-enter this on
          // its own output, forever.
          return changed ? tr : null
        },
      }),
    ]
  },
})
