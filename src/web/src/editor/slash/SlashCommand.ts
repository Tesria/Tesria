import { Extension, type Editor } from '@tiptap/core'
import Suggestion from '@tiptap/suggestion'
import { PluginKey } from '@tiptap/pm/state'
import { ReactRenderer } from '@tiptap/react'
import type { SuggestionProps } from '@tiptap/suggestion'
import { SlashMenu, type SlashMenuRef } from './SlashMenu'
import { filterSlashItems, type SlashItem, type SlashCommandStorage } from './items'

/**
 * Notion/Confluence-style "/" block-insertion menu. Built on TipTap's
 * `Suggestion` primitive (the same one `@tiptap/extension-mention` uses) —
 * there is no pre-built importable slash-command extension.
 */
export const SlashCommand = Extension.create({
  name: 'slashCommand',

  addStorage(): SlashCommandStorage {
    // Populated per-render by Editor.tsx/CollaborativeEditor.tsx, since the
    // Image item needs the current getUploadPageId/onUploadError callbacks
    // but this extension is only configured once in the shared extension list.
    return {}
  },

  addProseMirrorPlugins() {
    return [
      Suggestion({
        editor: this.editor,
        // Every Suggestion plugin needs its own key: the default is a single
        // shared `suggestion$`, so a second one throws "Adding different
        // instances of a keyed plugin" and takes the whole editor down.
        pluginKey: new PluginKey('slashSuggestion'),
        char: '/',
        startOfLine: false,
        command: ({ editor, range, props }: { editor: Editor; range: { from: number; to: number }; props: SlashItem }) => {
          props.command(editor, range)
        },
        items: ({ query }: { query: string }) => filterSlashItems(query),
        render: () => {
          let component: ReactRenderer<SlashMenuRef, SuggestionProps<SlashItem>>
          let unmount: (() => void) | undefined

          return {
            onStart: (props) => {
              component = new ReactRenderer(SlashMenu, { editor: props.editor, props })
              unmount = props.mount(component.element)
            },
            onUpdate: (props) => {
              component.updateProps(props)
            },
            onKeyDown: (props) => {
              if (props.event.key === 'Escape') {
                unmount?.()
                return true
              }
              return component.ref?.onKeyDown(props) ?? false
            },
            onExit: () => {
              unmount?.()
              component.destroy()
            },
          }
        },
      }),
    ]
  },
})
