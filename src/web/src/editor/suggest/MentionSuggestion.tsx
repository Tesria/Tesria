import { Extension } from '@tiptap/core'
import Suggestion from '@tiptap/suggestion'
import { PluginKey } from '@tiptap/pm/state'
import type { SuggestionProps } from '@tiptap/suggestion'
import { forwardRef } from 'react'
import { api, type Directory } from '../../api/client'
import { Avatar } from '../../components/Avatar'
import { SuggestionList, type SuggestionListRef } from './SuggestionList'
import { renderSuggestion } from './renderSuggestion'

/**
 * The directory, fetched once per page load and filtered in memory.
 *
 * A self-hosted wiki's user list is small (this is not a public social
 * network), and a per-keystroke request would make the popup lag behind the
 * typing it is meant to keep up with. The promise is cached, not the result,
 * so concurrent first keystrokes share one request.
 */
let directory: Promise<Directory[]> | null = null

function loadDirectory(): Promise<Directory[]> {
  directory ??= api.users.list().catch(() => {
    // Let the next keystroke retry rather than caching a failure forever.
    directory = null
    return []
  })
  return directory
}

function matches(users: Directory[], query: string): Directory[] {
  const q = query.toLowerCase()
  return users
    .filter((u) => u.displayName.toLowerCase().includes(q) || u.email.toLowerCase().split('@')[0].includes(q))
    .slice(0, 8)
}

const MentionList = forwardRef<SuggestionListRef, SuggestionProps<Directory>>((props, ref) => (
  <SuggestionList
    ref={ref}
    items={props.items}
    onSelect={(user) => props.command(user)}
    keyOf={(user) => user.id}
    emptyLabel="No matching people"
    render={(user) => (
      <>
        <Avatar subject={user} size={22} />
        <span className="suggest-menu__label">{user.displayName}</span>
      </>
    )}
  />
))
MentionList.displayName = 'MentionList'

export const MentionSuggestion = Extension.create({
  name: 'mentionSuggestion',

  addProseMirrorPlugins() {
    return [
      Suggestion<Directory>({
        editor: this.editor,
        // Every Suggestion plugin needs its own key: the default is a single
        // shared `suggestion$`, so a second one throws "Adding different
        // instances of a keyed plugin" and takes the whole editor down.
        pluginKey: new PluginKey('mentionSuggestion'),
        char: '@',
        // Mid-word "@" is an email address being typed, not a mention.
        allowedPrefixes: [' ', '\n'],
        items: async ({ query }) => matches(await loadDirectory(), query),
        command: ({ editor, range, props }) =>
          editor.commands.insertMention(range, { id: props.id, label: props.displayName }),
        render: renderSuggestion(MentionList),
      }),
    ]
  },
})
