import { ReactRenderer } from '@tiptap/react'
import type { SuggestionOptions, SuggestionProps } from '@tiptap/suggestion'
import type { ComponentType } from 'react'
import type { SuggestionListRef } from './SuggestionList'

/**
 * The `render` half of a `@tiptap/suggestion` plugin: mount a React component
 * at the cursor, keep it updated, hand it the keys, tear it down.
 *
 * Identical for every suggestion (slash, mention, emoji): positioning,
 * scroll/resize tracking and outside-click dismissal all come from
 * Suggestion's own managed `mount()` API, so there is nothing per-suggestion
 * here except which component to draw.
 */
export function renderSuggestion<T>(
  Component: ComponentType<SuggestionProps<T>>,
): SuggestionOptions<T>['render'] {
  return () => {
    let component: ReactRenderer<SuggestionListRef, SuggestionProps<T>>
    let unmount: (() => void) | undefined

    return {
      onStart: (props) => {
        component = new ReactRenderer(Component, { editor: props.editor, props })
        unmount = props.mount(component.element)
      },
      onUpdate: (props) => component.updateProps(props),
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
  }
}
