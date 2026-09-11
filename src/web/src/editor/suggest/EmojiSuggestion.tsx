import { Extension } from '@tiptap/core'
import Suggestion from '@tiptap/suggestion'
import { PluginKey } from '@tiptap/pm/state'
import type { SuggestionProps } from '@tiptap/suggestion'
import { forwardRef } from 'react'
import { filterEmoji, type Emoji } from '../emoji'
import { SuggestionList, type SuggestionListRef } from './SuggestionList'
import { renderSuggestion } from './renderSuggestion'

const EmojiList = forwardRef<SuggestionListRef, SuggestionProps<Emoji>>((props, ref) => (
  <SuggestionList
    ref={ref}
    items={props.items}
    onSelect={(emoji) => props.command(emoji)}
    keyOf={(emoji) => emoji.char}
    emptyLabel="No matching emoji"
    render={(emoji) => (
      <>
        <span className="suggest-menu__emoji">{emoji.char}</span>
        <span className="suggest-menu__label">:{emoji.name}:</span>
      </>
    )}
  />
))
EmojiList.displayName = 'EmojiList'

/**
 * `:name` inserts the literal character. Nothing new reaches the schema, the
 * export renderer or the search index — the document just contains text that
 * happened to be typed with a picker.
 */
export const EmojiSuggestion = Extension.create({
  name: 'emojiSuggestion',

  addProseMirrorPlugins() {
    return [
      Suggestion<Emoji>({
        editor: this.editor,
        // Every Suggestion plugin needs its own key: the default is a single
        // shared `suggestion$`, so a second one throws "Adding different
        // instances of a keyed plugin" and takes the whole editor down.
        pluginKey: new PluginKey('emojiSuggestion'),
        char: ':',
        // Two characters before the list appears: a bare ":" is punctuation,
        // and popping a menu open every time someone types one would be
        // unusable. Also skips ":" mid-word, which is a URL or a time.
        allowedPrefixes: [' ', '\n'],
        startOfLine: false,
        items: ({ query }) => (query.length < 2 ? [] : filterEmoji(query)),
        command: ({ editor, range, props }) =>
          editor.chain().focus().deleteRange(range).insertContent(props.char).run(),
        render: renderSuggestion(EmojiList),
      }),
    ]
  },
})
