/**
 * The editor's colour swatches, shared by the text-highlight dropdown and the
 * table cell-background menu.
 *
 * These are Atlassian's own palette values, in the same light / medium / bold
 * tiers Confluence's cell "Background colour" picker offers — Confluence
 * deliberately ships a fixed palette rather than a hex input, and this app's
 * theme is already built on the same colour family (see index.css's `--text:
 * #172b4d` / `--primary: #0c66e4`), so the swatches sit naturally against it.
 *
 * Keeping them as data rather than CSS classes matters: the chosen value is
 * written into the stored document (a `backgroundColor` cell attribute or a
 * `highlight` mark's `color`), so it has to survive export and read-only
 * rendering with no stylesheet involved.
 */

export type Swatch = { name: string; value: string }

/**
 * Table cell backgrounds — three tiers of seven hues plus white, matching the
 * grid Confluence shows under Cell options → Background colour.
 */
export const CELL_BACKGROUND_TIERS: Swatch[][] = [
  [
    { name: 'White', value: '#ffffff' },
    { name: 'Light grey', value: '#f4f5f7' },
    { name: 'Light blue', value: '#deebff' },
    { name: 'Light teal', value: '#e6fcff' },
    { name: 'Light green', value: '#e3fcef' },
    { name: 'Light yellow', value: '#fffae6' },
    { name: 'Light red', value: '#ffebe6' },
    { name: 'Light purple', value: '#eae6ff' },
  ],
  [
    { name: 'Grey', value: '#dfe1e6' },
    { name: 'Blue', value: '#b3d4ff' },
    { name: 'Teal', value: '#b3f5ff' },
    { name: 'Green', value: '#abf5d1' },
    { name: 'Yellow', value: '#fff0b3' },
    { name: 'Red', value: '#ffbdad' },
    { name: 'Purple', value: '#c0b6f2' },
  ],
  [
    { name: 'Bold grey', value: '#b3bac5' },
    { name: 'Bold blue', value: '#4c9aff' },
    { name: 'Bold teal', value: '#79e2f2' },
    { name: 'Bold green', value: '#57d9a3' },
    { name: 'Bold yellow', value: '#ffc400' },
    { name: 'Bold red', value: '#ff8f73' },
    { name: 'Bold purple', value: '#998dd9' },
  ],
]

/**
 * Text highlight colours. Only the two lighter tiers: a highlight sits behind
 * body text, and the bold tier doesn't hold the app's `--text` (#172b4d)
 * legibly enough to offer as a text background.
 */
export const HIGHLIGHT_TIERS: Swatch[][] = [
  CELL_BACKGROUND_TIERS[0].slice(2), // drop white/light-grey — invisible as a highlight
  CELL_BACKGROUND_TIERS[1].slice(1),
]
