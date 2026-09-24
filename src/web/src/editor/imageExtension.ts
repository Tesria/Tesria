import TiptapImage from '@tiptap/extension-image'
import { ReactNodeViewRenderer } from '@tiptap/react'
import { ImageView } from './ImageView'

/**
 * Adds Confluence-style display options (border, drop shadow) to the stock
 * Image node, and a size, an alignment and a caption (dev-plan 15.2), drawn
 * by ImageView.
 */
export const Image = TiptapImage.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      border: {
        default: false,
        parseHTML: (element: HTMLElement) => element.classList.contains('img-border'),
        renderHTML: (attributes: { border?: boolean }) =>
          attributes.border ? { class: 'img-border' } : {},
      },
      shadow: {
        default: false,
        parseHTML: (element: HTMLElement) => element.classList.contains('img-shadow'),
        renderHTML: (attributes: { shadow?: boolean }) =>
          attributes.shadow ? { class: 'img-shadow' } : {},
      },
      /** Percent of the column, 10 to 100; null is the picture's own size. */
      width: {
        default: null,
        parseHTML: (element: HTMLElement) => {
          const v = Number(element.getAttribute('data-width'))
          return Number.isFinite(v) && v >= 10 && v <= 100 ? v : null
        },
        renderHTML: (attributes: { width?: number | null }) =>
          attributes.width ? { 'data-width': String(attributes.width) } : {},
      },
      /** left, center (the default), right or full. */
      align: {
        default: 'center',
        parseHTML: (element: HTMLElement) => {
          const v = element.getAttribute('data-align')
          return v === 'left' || v === 'right' || v === 'full' ? v : 'center'
        },
        renderHTML: (attributes: { align?: string }) =>
          attributes.align && attributes.align !== 'center' ? { 'data-align': attributes.align } : {},
      },
      caption: {
        default: null,
        parseHTML: (element: HTMLElement) => element.getAttribute('data-caption'),
        renderHTML: (attributes: { caption?: string | null }) =>
          attributes.caption ? { 'data-caption': attributes.caption } : {},
      },
    }
  },

  addNodeView() {
    return ReactNodeViewRenderer(ImageView)
  },
})
