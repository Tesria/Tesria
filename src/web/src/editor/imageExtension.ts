import TiptapImage from '@tiptap/extension-image'

/** Adds Confluence-style display options (border, drop shadow) to the stock Image node. */
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
    }
  },
})
