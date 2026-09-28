# Tesria style guide: Minimal and Glass

Tesria draws its interface in two styles. **Minimal** is Tesria's classic
flat look and the default. **Glass** is tesria.com's frosted look, brought
into the app in 0.8.1. This guide is for anyone who adds a screen or a
component and has to make it look right in both, and for the website team,
since tesria.com and the app's Glass share one look.

Every value here comes from the code as of 0.8.1-dev (2026-09-28). When the
code and this guide disagree, the code is what ships; fix whichever is wrong,
and keep the two in step. The sources:

| File | What it holds |
|---|---|
| `src/web/src/index.css` | Minimal, and everything both styles share: sizes, colors per theme and accent, radii, shadows |
| `src/web/src/glass.css` | Every Glass rule and token, loaded after `index.css` (`main.tsx`) |
| `src/web/src/theme.ts`, `src/web/index.html` | How the theme, style, accent and Reduce Motion are stored and applied |
| `src/web/src/components/ThemeToggle.tsx` | The appearance menu |
| `src/web/src/editor/appearance.ts` | An element's own Style |
| `src/web/src/components/Layout.tsx` | The top bar and its collapse levels |
| `src/web/src/components/OverflowTabs.tsx` | Tab bars that never scroll |
| `src/web/src/components/popoverMotion.ts`, `src/web/src/routes/SpacePage.tsx` | Glass motion for menus and the sidebar |
| `src/web/src/scrollbars.ts` | Glass scrollbars |
| `src/web/src/components/PieChart.tsx`, `src/web/src/editor/ChartView.tsx`, `src/web/src/editor/MermaidView.tsx` | Charts and diagrams |

Related: the brand kit and the mark's colors are in this folder's
[README.md](README.md) and [colors.css](colors.css); how Tesria talks about
itself is in [messaging.md](messaging.md); the theming mechanism is in
[architecture.md, Theming](../architecture.md#theming-themets-componentsthemetoggletsx).

---

## 1. Principles

1. **Minimal is the default; Glass is a choice.** Minimal is Tesria as it
   has always looked: solid surfaces, thin borders, a flat accent. Glass is
   tesria.com's frosted look. Each person picks one per browser. The name
   Flat is retired; it survives only as the stored value `flat`.
2. **One layout, two finishes.** Both styles share the same layout, the same
   control heights and the same collapse rules. Glass adds only the glass:
   every Glass rule is keyed to `:root[data-style="glass"]` (or to an
   element's `data-appearance="glass"`), and Minimal is `index.css` with
   nothing taken away.
3. **Glass is for chrome and containers, not for reading.** The top bar,
   buttons, menus, the sidebar and cards are frosted. The page itself, the
   editor, tables, form fields and long text stay on solid ground, so they
   stay easy to read.
4. **No glass on glass.** When a glass control sits on a frosted surface,
   the control drops its own glass. The top bar's Spaces and Admin pill and
   the avatar pill lose theirs once the bar docks into its frosted strip.
5. **The accent means "this one".** The current page, the selected choice
   and the primary action carry the accent. Everything else is neutral.
6. **One light.** In dark, light falls from the top left: fills are lighter
   there and darker at the bottom right, and strokes are bright at the top
   left. In light the whole thing is mirrored: darker at the top left,
   lighter at the bottom right.
7. **Whole pixels.** Heights are whole pixels: 48px in the top bar, 38px
   below it.
8. **Title Case** for every name in the interface.
9. **It degrades.** Without `backdrop-filter`, or when the device asks for
   less transparency or less motion, Glass gets more solid and stops moving.
10. **Every color is a token.** Nothing is hard-coded in a component except
    where an export needs a literal (see [Elements](#7-elements-and-exports)).

---

## 2. Choosing a style

### The four settings

The appearance menu (`ThemeToggle.tsx`, the sun, moon or monitor button in
the top bar) holds four settings. Each is stored per browser in
`localStorage` and expressed to CSS as an attribute on `<html>`:

| Setting | Menu heading | Values | Storage key | Attribute on `<html>` | Default |
|---|---|---|---|---|---|
| Theme | THEME | System, Light, Dark | `tesria-theme` | `data-theme="light"` or `"dark"`; absent for System | System |
| Style | STYLE | Minimal, Glass | `tesria-style` (`glass` only; Minimal removes the key) | `data-style="glass"`; absent for Minimal | Minimal |
| Accent | ACCENT COLOR | Blue, Green, Purple, Orange, Magenta, and the instance's brand color when it has one | `tesria-accent` | `data-accent="green"` and so on; absent for blue | Blue, or the instance's default |
| Reduce Motion | a switch under STYLE, shown only with Glass | on or off | `tesria-reduce-motion` (`1`) | `data-motion="reduce"` | Off |

The style's code name is `flat` for Minimal (`StylePreference` in
`theme.ts`), and it stays that way: browsers have saved it, and elements
store it. Only the name people see changed.

An instance can hold everyone to a theme (`data-theme-lock`) or an accent
(`data-accent-lock`), written onto `<html>` by the server. A locked setting
is not offered. The style is always a personal choice, so the menu stays
even when the theme and the accent are both locked.

### Before first paint

`index.html` carries a small synchronous script that reads the stored
values and sets the attributes before anything is drawn, so the page never
flashes the wrong theme or style. It also sets `data-os="windows"` on
Windows (for the 1px button nudge, see [Typography](#4-typography-and-text-color)).
The storage keys and attribute logic are duplicated between that script and
`theme.ts`: **change them together.** The script is allowed by the content
security policy by its hash, so its text must be identical on every
instance.

### How to write a rule for each style

- **Minimal:** a plain rule in `index.css`, e.g. `.btn { ... }`.
- **Glass:** a rule in `glass.css` keyed to the style:
  `:root[data-style="glass"] .btn { ... }`.
- **Minimal only:** `:root:not([data-style="glass"]) .x { ... }` in
  `index.css`. Use it rarely; today it places the top bar's theme and bell
  icons and the notification count, where Minimal draws no circle.
- **An element with its own Style** (status, chart, code block, diagram)
  takes both forms, so it follows the reader unless it has chosen:
  `:root[data-style="glass"] .status:not([data-appearance="flat"]), .status[data-appearance="glass"] { ... }`.
- **Dark theme values** are declared three times, as `index.css` does for
  every color: on `:root` (light), inside
  `@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { ... } }`,
  and under `:root[data-theme="dark"]`. That is what lets an explicit choice
  win in both directions.
- **In script**, test the style with
  `document.documentElement.getAttribute('data-style') === 'glass'`, and
  motion with `motionReduced()` from `theme.ts` (the switch or the system
  setting).

### Three CSS pitfalls

From `docs/architecture.md` and the comments in `glass.css`:

1. **A custom property is worked out where it is declared.** A token built
   on `:root` from another token takes that token's `:root` value; setting
   the inner token on an element later does not rebuild it. That is how the
   sidebar once lost its accent tint. Build a variant as its own token
   (`--sidebar-bg` beside `--panel-bg`), or set the whole value on the
   element.
2. **A background color may only be the last layer.** `background: var(--x), var(--y)`
   where `--x` ends in a plain color is invalid, and the browser drops the
   whole declaration silently. Wrap a color meant as a layer in
   `linear-gradient(c, c)`, as `--panel-bg` does.
3. **An element with `backdrop-filter` is a backdrop root.** A frosted menu
   inside it blurs only that element, not the page behind. The docked top
   bar therefore draws its frost on a `::before` layer behind its contents,
   not on the bar itself.

Two more that cost time in 0.8.1: a gradient on a button with a transparent
1px border repeats into the border unless `background-origin: border-box`
is set (Glass sets it with `!important` on every button, `glass.css`); and a
frosted card is a layer of its own, so a menu hanging out of a bar above it
needs a `z-index` (`.tabs.tabs--fit` has `z-index: 6`).

---

## 3. Tokens

The core palette is the same in both styles: Glass does not redefine
`--bg`, `--surface`, `--text` or the accent. It adds its own tokens in
`glass.css`, and one page background, `--ground`.

### 3.1 Sizes

| Token | Value | Purpose |
|---|---|---|
| `--topbar-h` | `64px` | Top bar height, both styles. The sidebar, the page bar and the phone menu pin themselves below it |
| `--ctl` | `48px` | Every control in the top bar: the Spaces and Admin pill, search, theme, bell, avatar pill, Sign In. In Glass, also tab bars |
| `--ctl-page` | `38px` | Every button below the top bar in Glass (about 80% of `--ctl`) |
| `--page-pad` | `1.5rem`; `0.75rem` at 640px and below | Page padding. Also read by the full-width table and layout breakout math: change it only here |
| `--sidebar-width` | `260px` by default, dragged by the reader | Space sidebar column |
| `--bp-mobile` | `640px` | Phone breakpoint. CSS cannot read a token in `@media`, so each query repeats `640px` with a "keep in sync" comment |
| `--bp-tablet` | `1024px` | Tablet breakpoint (documentation only; the setup wizard uses 860px) |
| `--safe-left`, `--safe-right` | `env(safe-area-inset-*)` | Keeps controls clear of a phone's notch |
| `--radius` | `6px` | Minimal's standard corner |

### 3.2 Core colors

Light is `:root`; dark applies under the dark media query or
`data-theme="dark"`. Both styles use these.

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `--bg` | `#ffffff` | `#161a1d` | Page background |
| `--bg-alt` | `#f4f5f7` | `#1d2125` | Minimal's quiet fill: secondary buttons, the sidebar, `.card`, hovers |
| `--surface` | `#ffffff` | `#22272b` | Raised surfaces: cards, popovers, the top bar, inputs. Same as `--bg` in light, lighter in dark |
| `--surface-sunken` | `#f4f5f7` | `#1d2125` | Logs, embed frames |
| `--border` | `#e4e6eb` | `#38414a` | Minimal's hairlines |
| `--hover` | `#e9ebef` | `#2c333a` | Hover wash on rows and icon buttons |
| `--text` | `#172b4d` | `#c7d1db` | Body text, and the bar's links, tabs, username, PAGES |
| `--muted` | `#6b778c` | `#9fadbc` | Secondary text: hints, captions, table headers |
| `--danger` | `#c9372c` | `#f87168` | Destructive actions, errors |
| `--danger-soft` | `#ffeceb` | `#42221f` | Error backgrounds |
| `--success` | `#22a06b` | `#4bce97` | Success dots and badges |
| `--warning` | `#d9a000` | `#e2b203` | Warning dots and stripes |
| `--swatch-border` | `rgba(9, 30, 66, 0.2)` | `rgba(255, 255, 255, 0.24)` | Ring around color swatches |

The same blocks define `--panel-*` (the editor's info, note, success,
warning and error callouts), `--status-*-bg` and `--status-*-ink`,
`--text-color-*`, `--chart-*` (the disk chart) and the tracked-change
colors. The mark's own four colors are `--tesria-write`, `--tesria-keep`,
`--tesria-share` and `--tesria-automate` (see [README.md](README.md)); they
never follow the accent.

### 3.3 Accent

The accent drives every `--primary*` token. Each accent is defined twice,
never derived, because contrast pulls light and dark in opposite
directions; both sets were checked against WCAG AA (light on `#ffffff`,
dark on `#161a1d`).

| Accent | `--primary` light | `--primary-dark` light | `--primary` dark | `--primary-dark` dark |
|---|---|---|---|---|
| Blue (default, no attribute) | `#0c66e4` | `#0055cc` | `#579dff` | `#85b8ff` |
| Green | `#1a6c45` | `#155939` | `#4bce97` | `#7ddcb4` |
| Purple | `#5b47ba` | `#4b3a99` | `#b8acf6` | `#ccc3f9` |
| Orange | `#9a4d00` | `#7e3f00` | `#fea362` | `#febd8e` |
| Magenta | `#a53a7f` | `#873068` | `#f797d2` | `#f9b4df` |

Each accent also sets `--primary-soft` (a selected row's fill),
`--primary-softer` (an unread notification), `--primary-soft-border`, and
`--on-primary`: **white in light, `#1d2125` (dark ink) in dark**, because
the dark theme's accents are bright. Text on anything filled with the
accent uses `--on-primary`, never `#fff`. The instance's own brand accent
(`data-accent="brand"`) is generated by the server
(`src/Api/Infrastructure/Branding/AccentColors.cs`). The picker's dots read
`--accent-dot-*`, themed so each dot shows the color it will produce.

### 3.4 Radii

| Thing | Minimal | Glass |
|---|---|---|
| Buttons | `6px` (`--radius`) | `999px` (pill) |
| Round icon buttons | `50%` in the top bar; `6px` elsewhere (`.overflow-menu__trigger`, `.tree-filter__children`) | `50%` (circle) |
| Plain icon buttons (edit tree, hide sidebar) | `6px` | `10px` hover wash |
| Search field | `999px` | `999px` |
| Tab bar | none (underline tabs) | `999px` pill, tabs `999px` |
| Menus and popovers | `6px` | `14px` |
| Menu rows | `4px` | `3px` (`8px` in the frosted drop-down) |
| Cards and sections | `6px` | `18px`; backup cards and alerts `14px` |
| Space sidebar | square | `18px` |
| Code block | `6px` | `10px` |
| Diagram panel | none | `12px` inside the console |
| Status | `3px` | `3px` |
| Badges | `4px`; notification count `999px` | notification count pill |

### 3.5 Shadows

Minimal (`index.css`):

| Token | Light | Dark | Used for |
|---|---|---|---|
| `--shadow-card` | `0 1px 2px rgb(0 0 0 / 0.06), 0 4px 12px rgb(0 0 0 / 0.08)` | `0 1px 2px rgb(0 0 0 / 0.3), 0 6px 16px rgb(0 0 0 / 0.35)` | Every raised card: `.backup-card`, `.profile__section`, `.stat`, `.dash__panel`, `.card`, `.alerts__item`, `.space-card a` (one shared rule, so a new card cannot stay flat by accident) |
| `--shadow-sm` | `0 1px 3px rgba(23, 43, 77, 0.08)` | `0 1px 3px rgba(0, 0, 0, 0.4)` | Small lifts |
| `--shadow-md` | `0 4px 14px rgba(23, 43, 77, 0.15)` | `0 4px 14px rgba(0, 0, 0, 0.5)` | Editor popovers: link menu, bubble toolbar, chip menu |
| `--shadow-lg` | `0 8px 24px rgba(23, 43, 77, 0.18)` | `0 8px 24px rgba(0, 0, 0, 0.6)` | Menus, dialogs, the phone menu |
| `--img-shadow` | `0 4px 14px rgba(23, 43, 77, 0.25)` | `0 4px 14px rgba(0, 0, 0, 0.6)` | Pictures with a shadow, animations |

Glass shadows are listed with the other Glass tokens below. Shadows are
always plain: no colored glows, on buttons or on badges.

### 3.6 Glass tokens

All in `glass.css`. The dark column applies under the dark media query or
`data-theme="dark"`.

**Blur.** Controls: `blur(14px) saturate(170%)`. Panels (sidebar, menus,
cards): `blur(20px) saturate(180%)`. The docked top bar: `blur(8px)`.

**The ground.** `--ground` (Glass only): two soft radial washes of the
accent over `--bg`, `radial-gradient(1100px 520px at 12% -8%, primary 9%)`
and `radial-gradient(900px 480px at 100% 0%, primary 6%)`, fixed to the
window. It gives the glass something to be glass over.

**Surfaces and controls**

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `--glass` | `rgba(255, 255, 255, 0.5)` | `color-mix(in srgb, var(--surface) 60%, transparent)` | Fill of a glass control |
| `--glass-hi` | `rgba(255, 255, 255, 0.95)` | `rgba(255, 255, 255, 0.22)` | Top inner edge of a bar or field |
| `--glass-lo` | `rgba(10, 20, 34, 0.08)` | `rgba(0, 0, 0, 0.25)` | Bottom inner edge |
| `--glass-hover` | `rgba(255, 255, 255, 0.55)` | `rgba(255, 255, 255, 0.07)` | Hover wash |
| `--glass-border` | `rgba(10, 20, 34, 0.12)` | `rgba(255, 255, 255, 0.14)` | Edge of a glass bar or field (white glass on a white page needs a dark edge) |
| `--glass-sheen` | `linear-gradient(160deg, rgba(10, 20, 34, 0.24), rgba(10, 20, 34, 0.04) 45%, #fff)` | `linear-gradient(160deg, rgba(255, 255, 255, 0.22), transparent 70%)` | The diagonal light on a button: shaded top left in light, lit top left in dark |
| `--frame-sheen` | `linear-gradient(160deg, rgba(255, 255, 255, 0.95), rgba(255, 255, 255, 0.15) 70%)` | `linear-gradient(160deg, rgba(255, 255, 255, 0.08), transparent 60%)` | Gentler sheen for large frames; dark panels are built on it |

**Panels**

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `--panel-bg` | a white 160deg sheen (`0.85`, `0.35` at 50%, `0.55`) over `rgba(250, 251, 253, 0.72)` | `var(--frame-sheen)` over `var(--glass)` | Neutral frost: cards, menus, Administration sections, chart frames. Bright white, never gray |
| `--sidebar-bg` | a cooler sheen (`0.72`, `0.06` at 50%, `rgba(10, 20, 34, 0.07)`) over `color-mix(primary 8%, rgba(212, 222, 236, 0.5))` | `var(--frame-sheen)` over `color-mix(primary 10%, var(--glass))` | The one accent-tinted frost: the space sidebar |
| `--panel-border` | `rgba(10, 20, 34, 0.1)` | `var(--glass-border)` | Hairline edge of a panel |
| `--panel-rim` | `inset 0 1px 0 rgba(255, 255, 255, 0.95), inset 0 0 0 1px rgba(255, 255, 255, 0.6)` | `inset 0 1px 0 var(--glass-hi)` | Bright inner rim; with the hairline, it is what reads as glass on white |
| `--diagram-bg` | a white 160deg sheen over `rgba(226, 233, 243, 0.86)` | `var(--panel-bg)` over `color-mix(surface 80%, transparent)` | The frosted panel a diagram sits on |

Menus add their own base under `--panel-bg`:
`color-mix(in srgb, var(--surface) 88%, transparent)`, denser than a card,
so text stays readable over a code block or a colored callout.

**Buttons**

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `--btn-rim` | `linear-gradient(160deg, rgba(10, 20, 34, 0.34), rgba(10, 20, 34, 0.14) 45%, #fff)` | `linear-gradient(160deg, rgba(255, 255, 255, 0.5), rgba(255, 255, 255, 0.06) 55%, rgba(255, 255, 255, 0.02))` | 1px gradient stroke of a neutral button |
| `--btn-in-top` / `--btn-in-bot` | `rgba(10, 20, 34, 0.08)` / `rgba(255, 255, 255, 0.95)` | `rgba(255, 255, 255, 0.22)` / `rgba(0, 0, 0, 0.25)` | Inner edge lines, top and bottom |
| `--btn-shadow` | `0 3px 6px -1px rgba(10, 20, 34, 0.32), 0 10px 22px -8px rgba(10, 20, 34, 0.42)` | `0 2px 6px -1px rgba(0, 0, 0, 0.55), 0 8px 24px -8px rgba(0, 0, 0, 0.6)` | Under a button or a bar pill |
| `--accent-shadow` | `0 2px 4px rgba(10, 20, 34, 0.22), 0 8px 18px -6px rgba(10, 20, 34, 0.3)` | `0 2px 4px rgba(0, 0, 0, 0.5), 0 8px 18px -6px rgba(0, 0, 0, 0.6)` | Under a colored button or a lens |
| `--inset-btn-shadow` | `0 4px 10px -1px rgba(10, 20, 34, 0.75), 0 2px 3px rgba(10, 20, 34, 0.4)` | `0 4px 10px -1px #000, 0 2px 3px rgba(0, 0, 0, 0.7)` | The small toggle inside the tree filter |
| `--tint-base` | `rgba(255, 255, 255, 0.8)` | `rgba(255, 255, 255, 0.03)` | What the accent is mixed into |
| `--lens-tint` | `82%` | `80%` | Accent strength of a selected item in a bar |
| `--cta-tint` | `88%` | `86%` | Accent strength of a primary button (plus 8% on hover) |
| `--fill-top`, `--fill-top-base` | `82%`, `#000` | `48%`, `#fff` | Colored button, top-left stop: darker in light, lighter in dark |
| `--fill-end`, `--fill-end-base` | `76%`, `#fff` | `62%`, `#000` | Colored button, bottom-right stop |
| `--fill-mid-at` | `50%` | `38%` | Where the pure color sits |
| `--fill-top-sm`, `--fill-end-sm` | `66%`, `52%` | `48%`, `62%` | Stronger stops for a 30px button |
| `--rim-top`, `--rim-top-base` | `65%`, `#000` | `0%`, `rgba(255, 255, 255, 0.7)` | Colored button stroke, top left: a deep shade in light, white in dark |
| `--rim-mid`, `--rim-mid-base` | `85%`, `#fff` | `0%`, `rgba(255, 255, 255, 0.12)` | Stroke, middle |
| `--rim-end`, `--rim-end-base` | `45%`, `#fff` | `0%`, `rgba(255, 255, 255, 0.03)` | Stroke, bottom right: a pale tint in light |
| `--accent-rim` | `rgba(255, 255, 255, 0.7)` | `rgba(255, 255, 255, 0.5)` | Stroke around the avatar |
| `--on-danger` | `#ffffff` | `var(--bg)` | Text on a danger-filled button |

**Elevation, status and console**

| Token | Light | Dark | Purpose |
|---|---|---|---|
| `--box-shadow` | `0 2px 6px rgba(10, 20, 34, 0.08), 0 24px 48px -16px rgba(10, 20, 34, 0.28)` | `0 2px 6px rgba(0, 0, 0, 0.35), 0 24px 48px -16px rgba(0, 0, 0, 0.7)` | Under every frosted panel |
| `--hdr-shadow` | `0 1px 2px rgba(10, 20, 34, 0.08), 0 8px 24px rgba(10, 20, 34, 0.16)` | `0 1px 2px rgba(0, 0, 0, 0.4), 0 8px 24px rgba(0, 0, 0, 0.55)` | Under the docked top bar and the open phone menu |
| `--code-shadow` | `0 4px 10px -2px rgba(10, 20, 34, 0.3), 0 16px 36px -10px rgba(10, 20, 34, 0.45)` | `0 4px 10px -2px rgba(0, 0, 0, 0.6), 0 16px 36px -10px rgba(0, 0, 0, 0.75)` | Under a code block |
| `--sg-red`, `--sg-yellow`, `--sg-green`, `--sg-blue`, `--sg-purple` | `#e0321c`, `#b85c00`, `#07804a`, `#1766f2`, `#6f3ae0` | `#ff5a47`, `#ffc21a`, `#2fe0a0`, `#4a9bff`, `#a57dff` | Glass status colors, more saturated than Minimal's inks |
| `--status-glass-ink` | `#ffffff` | `var(--bg)` | Text on a Glass status |
| `--badge-rim` | `rgba(255, 255, 255, 0.7)` | `rgba(255, 255, 255, 0.22)` | Edge of the notification count |
| `--term` | `#060d17` | same | Console background (dark in both themes) |
| `--term-ink`, `--term-dim`, `--term-rule` | `#dbe6f4`, `#7f93ae`, `#1c2d45` | same | Code text; language label; dividers |
| `--term-sheen` | `linear-gradient(160deg, rgba(255, 255, 255, 0.12), transparent 50%)` | same | Light across the console |
| `--term-head`, `--term-head-shadow` | `linear-gradient(135deg, #1b2840, #04080e 70%)`, `rgba(0, 0, 0, 0.7)` | same | The console's title bar and its shadow onto the code |
| `--term-glass`, `--term-glass-rim`, `--term-glass-hi` | `rgba(255, 255, 255, 0.07)`, `0.14`, `0.22` | same | Copy and line-number pills; the console's edge |
| `--sb-thumb` | `rgba(10, 20, 34, 0.38)` | `rgba(255, 255, 255, 0.4)` | Scrollbar thumb |

Defined but not read by any rule today: `--glass-rim`, `--glass-lens`,
`--glass-shadow`, `--glass-edge`, `--glass-contact`. Use the tokens above
instead, or remove these when next touching the file.

### 3.7 The colored-button recipe

A Glass button filled with a color takes its diagonal from that color, not
from a white sheen (which turned the color milky). Primary, danger and the
tree filter's on state all use it; a new colored button should too:

```css
:root[data-style="glass"] .my-button {
  --rim-c: var(--primary);            /* the color, for the stroke */
  color: var(--on-primary);
  background: linear-gradient(160deg,
    color-mix(in srgb, color-mix(in srgb, var(--primary) var(--cta-tint), var(--tint-base)) var(--fill-top), var(--fill-top-base)),
    color-mix(in srgb, var(--primary) var(--cta-tint), var(--tint-base)) var(--fill-mid-at),
    color-mix(in srgb, color-mix(in srgb, var(--primary) var(--cta-tint), var(--tint-base)) var(--fill-end), var(--fill-end-base)));
  box-shadow: inset 0 1px 0 var(--btn-in-top), inset 0 -1px 0 var(--btn-in-bot), var(--accent-shadow);
}
```

The stroke is drawn by the shared `::before` ring (section 6.1), which mixes
its three stops from `--rim-c`. Because the fill and stroke tokens flip
between themes, the same rule is lit from the top left in dark and mirrored
in light.

---

## 4. Typography and text color

**Typefaces.** The interface uses the system stack:
`-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif`.
Code, status badges in Glass and the console use
`ui-monospace, SFMono-Regular, Menlo, Consolas, monospace`. The TESRIA
wordmark is Archivo at weight 800 and 112% width, cut to its six letters
and embedded in `index.css` as `Tesria Wordmark`; an instance's own name
keeps the system font.

**Sizes in use**

| Text | Minimal | Glass |
|---|---|---|
| Page title (`h1`) | `1.7rem` | same |
| Top bar links (Spaces, Admin) | `15px`, weight 500 (the current one too) | same |
| Username beside the avatar | weight 500 | `15px`; `17px` when the bar is docked |
| Wordmark in the top bar | `1.05rem` | `22px`, mark `28px`, `10px` apart |
| Buttons | `0.9rem`, weight 500; small `0.8rem`; Sign In in the bar weight 600 | same |
| Tabs | `0.9rem`, weight 500; current 600 | `15px` |
| Small bold capitals: THEME, STYLE, ACCENT COLOR, NOTIFICATIONS | `0.72rem`, weight 700, uppercase, `0.03em` tracking, `--text` | same |
| PAGES in the sidebar | `15px`, weight 700, uppercase, `--text`, sized to the button on its row | same |
| Space Settings in the sidebar | `17px` (lowercase letters, so a little larger to match PAGES) | same |
| Table headers, KPI labels | `0.78rem` / `0.75rem`, uppercase, `--muted` | same |
| KPI value | `1.6rem`, weight 700 | same |
| Code | `0.85rem`, line height 1.5 | line height 1.75; header `12.5px` weight 500 |
| Status | `0.72em`, weight 700, uppercase | monospace, weight 600 |

**Text color rules**

- The bar's links, the username, tab labels, PAGES and Space Settings use
  `--text`. Never `--muted` gray for something people navigate by.
- The current item keeps the accent: `--primary-dark` on `--primary-soft`
  in Minimal, `--on-primary` on the accent lens in Glass. In the phone menu
  the current link is bold in `--primary` with no tint.
- `--muted` is for secondary text: hints, captions, dates, table headers.
- Text on an accent fill uses `--on-primary`; on a danger fill in Glass,
  `--on-danger`.
- Text never gets a shadow, in either style.

**Title Case.** Every name in the interface is in Title Case: buttons,
tabs, headings, menu items, labels. Small words follow AP style and stay
lowercase unless first or last: *a, an, the, and, but, or, for, nor*, and
prepositions of three letters or fewer (*at, by, in, of, on, to, up*).
Examples in the app: **+ New Page**, **Space Settings**, **Mark All Read**,
**Reduce Motion**, **Show Resolved**, **Sign In**. Headings shown in small
capitals (THEME, PAGES) are still written in Title Case in the source.

**Phones.** Every form control is at least `16px` on a phone or any touch
screen (`@media (max-width: 640px), (pointer: coarse)`), or iOS Safari zooms
the page on focus.

**Windows.** Segoe UI sits a label's capitals about 1px below center, so on
Windows (`data-os="windows"`) button text moves up 1px: Minimal takes 1px
from the top padding and adds it at the bottom; Glass, where the height is
fixed, sets `padding-top: 0; padding-bottom: 2px`. Menu rows are exempt.

---

## 5. Layout

### 5.1 The top bar

`header.topbar` in `Layout.tsx`: 64px tall (`--topbar-h`), sticky, with
`1rem` side padding plus the safe-area inset. Left to right: the hamburger
(when collapsed), the brand, the page bar (Spaces, Admin), search, then the
right-hand cluster: theme, bell, and the avatar pill (or Sign In). Every
control is 48px (`--ctl`) in both styles. Sign Out is not in the bar; it is
on the profile page, opposite its heading.

| | Minimal | Glass |
|---|---|---|
| Bar | `--surface` with a `--border` bottom edge | Transparent, floating over `--ground` |
| After scrolling 4px (`.is-docked`) | No change | Docks into a frosted strip: a `::before` layer of `--bg` at 60% with `blur(8px)`, a bottom edge of `--text` at 10%, and `--hdr-shadow`. The Spaces and Admin pill and the avatar pill drop their glass; the theme and bell buttons keep theirs |
| Spaces and Admin | Plain links, `4px` corners; current one `--primary-soft` | One glass pill, 48px, `4px` padding; links fill it; current one a tinted lens |
| Search | 48px pill on `--bg-alt` | 48px glass pill |
| Theme and bell | 20px icons in a 48px round box, no circle drawn; hover fills the circle with `--hover` | Glass circles with sheen and gradient stroke |
| Avatar | 38px avatar and your name | The same in a glass pill; docked, the avatar grows to 48px and the name to 17px |
| Menu open (`.is-menu-open`) | Solid `--surface` menu under the bar | Bar and menu share the page's `--ground`, no frost, no seam; the menu has `10px` bottom corners and `--hdr-shadow` below only |

In Minimal, with no circles drawn, the theme and bell icons are spaced by
their ink: the bell steps 16px toward the theme button, and keeps 11px more
on its right while it shows a count (`:root:not([data-style="glass"])` rules
at the end of `index.css`).

**Collapse levels.** The bar never shrinks or cuts anything short to make
room; it moves things into the hamburger menu instead. `useTopbarFit`
measures an invisible copy of the bar (`.topbar__ruler`) and picks the
fullest level that fits with 12px to spare, allowing search at least
200px. It measures again when the window resizes, when the style or theme
changes, and when fonts load. The level is on the bar as `data-fit`.

| Level | In the bar | In the menu |
|---|---|---|
| 0 | Everything, with your name beside the avatar | Nothing (no hamburger) |
| 1 | Hamburger, brand, theme, bell, avatar (no name) | Spaces, Admin, search |
| 2 | Hamburger, brand, bell, avatar | Theme button too |
| 3 | Hamburger, brand, avatar | Bell too |

This depends on the measured width, not the screen size: a long instance
name or a larger style collapses the bar sooner. When the bar is full again
the menu closes, but only after it has stayed full for 400ms (a phone's
keyboard closing briefly measures as "fits").

### 5.2 The space sidebar

`aside.sidebar` in `SpacePage.tsx`: three bands, a head (space icon, key,
name, hide button, **+ New Page**), the page tree (PAGES heading, filter,
rows, the only part that scrolls) and a foot (**Space Settings**). The
column is `--sidebar-width` wide and can be dragged by a 10px handle on its
right edge, which shows a 2px accent line on hover, focus or drag.

| | Minimal | Glass |
|---|---|---|
| Panel | Full height under the bar, `--bg-alt`, `--border` right edge | Floats inside its column: `0.75rem` from the bar and the window bottom, left edge in line with the logo (`1rem` plus the safe area), `1rem` padding, `18px` corners, `--sidebar-bg` with `blur(20px) saturate(180%)`, `--panel-border`, `--panel-rim` and `--box-shadow` |
| Current page, current Space Settings | `--primary-soft` fill, `--primary-dark` text, weight 600 | The same plus a 1px inset accent stroke |
| Hidden | A 40px rail with a 30px square toggle | Column shrinks to `38px + 1rem + 0.5rem` plus the safe area; one round 38px glass button, its icon in the accent, in line with the logo |
| Hiding and showing | Instant | Animated (section 8) |

### 5.3 Page bars

- **Viewing a page** (`.page-actionbar`): Minimal is a sticky bar under the
  top bar on `--surface` with a bottom border, its last button lined up with
  the avatar. Glass (641px and wider) takes no height: Edit, Full Width and
  the ⋮ menu float at the top right, still sticky, `0.75rem` down, and the
  breadcrumb moves up to line up with the top of the floating sidebar,
  leaving `24rem` on its right for the buttons. On a phone the Glass bar
  keeps its height but has no fill or bottom edge.
- **Editing** (`.page-actionbar--editor`) keeps its solid bar in both
  styles: it holds the toolbar, which reflows by measuring its own width
  (container queries), not the viewport's.
- **The space bar on a phone** (`.space-actionbar`, **+ New** and the ⋮
  menu): Minimal is a sticky solid bar. Glass gives it no height; the
  buttons float at the top right in line with the avatar, and the space
  home's title moves up to share their 38px row.

### 5.4 The page

The reading column is `900px` wide (`.page-wrap`), or full width when the
page asks. Administration uses the full width. `--page-pad` is the gutter.
Pages, the editor and dialogs stay solid in both styles.

### 5.5 Phones (640px and below)

- The sidebar is hidden. The space home shows the whole page tree inline,
  and the hamburger menu carries the open space's name, **+ New Page**, its
  tree (which scrolls on its own, and the menu reaches the bottom of the
  screen) and **Space Settings**.
- The appearance and notification panels pin to the screen, `1rem` from
  each side and `60px` from the top, instead of hanging off their buttons.
  They show a close button.
- Full Width disappears (the column already fills the screen).
- Wide tables scroll sideways inside their own box, never the page. Tab
  bars never scroll (section 6.3).
- In Glass the docked bar gets a stronger shadow to separate it from the
  page scrolling under it.

---

## 6. Components

Each component below lists Minimal and Glass side by side. Glass
selectors in `glass.css` are the Minimal class with
`:root[data-style="glass"]` in front.

### 6.1 Buttons

All buttons are `.btn` plus a modifier. Below the top bar, Glass buttons are
38px tall (`min-height: var(--ctl-page)`), small ones included. Top bar
buttons are 48px in both styles.

| Kind | Class | Minimal | Glass |
|---|---|---|---|
| Secondary | `.btn` | `--bg-alt` fill, `6px` corners, `0.45rem 0.85rem` padding; hover `brightness(0.97)` | Clear glass pill: `var(--glass-sheen), var(--glass)`, gradient stroke, inner edges, `--btn-shadow`; `1.15rem` side padding; hover mixes in `--glass-hover` |
| Primary | `.btn--primary` | Flat `--primary`, `--on-primary` text; hover `--primary-dark` | The colored-button recipe (3.7) in the accent: its own diagonal, its own stroke, `--accent-shadow`; hover 8% stronger |
| Danger | `.btn--danger` | Transparent with a `--border` edge and `--danger` text; hover `--danger-soft` | The colored-button recipe in `--danger`, `--on-danger` text |
| Ghost | `.btn--ghost` | Transparent with a `--border` edge | Same as secondary |
| Outline | `.btn--outline` | Transparent, 2px `--primary` stroke, `--primary` text; hover 10% accent wash | The same, flat: no fill, no sheen, no gradient stroke, no shadow |
| Small | `.btn--sm` | `0.3rem 0.6rem` padding, `0.8rem` text | Still 38px tall; `0.95rem` side padding |
| Full width | `.btn--block` | Fills its row | Same |
| Round icon | `.theme-toggle`, `.notif__bell`, `.overflow-menu__trigger`, `.tree-filter__children`, the hidden sidebar's button | Top bar: 48px round, no fill; below: 34px (⋮) or 2rem (tree toggle) squares with a `--border` edge | Circles: 48px in the bar, 38px below it; sheen, stroke and shadow like a secondary button. The tree toggle sits inside the filter at 30px; on, it takes the colored recipe |
| Plain icon | `.tree-section__reorder`, `.sidebar__toggle` | Edit tree: a small ghost button (`1.75rem` minimum); hide sidebar: 30px, transparent, a `--border` edge and `--surface` fill on hover | 38px square, no glass, `--muted` icon; hover `--glass-hover` with `10px` corners |
| Text only | `.link-btn` | `--primary` text, `0.85rem`, underline on hover; `.link-btn--danger` in `--danger` | Same |
| Menu row | `.btn` inside `.overflow-menu__dropdown` | A plain row, `4px` corners | A plain row, `3px` corners, no glass; hover `--glass-hover` |
| Alert action | `.btn` inside `.alerts__actions` | A normal button | Plain accent text, weight 600, no frame; underline on hover |

**The stroke.** Every Glass button (and the avatar pill) gets its 1px
stroke from a `::before` ring, masked so the fill stays clear:

```css
:root[data-style="glass"] .btn::before {
  content: ""; position: absolute; inset: -1px; border-radius: inherit; padding: 1px;
  background: var(--btn-rim); pointer-events: none;
  -webkit-mask: linear-gradient(#000 0 0) content-box, linear-gradient(#000 0 0);
  -webkit-mask-composite: xor;
  mask: linear-gradient(#000 0 0) content-box exclude, linear-gradient(#000 0 0);
}
```

A colored button replaces `--btn-rim` with three stops mixed from `--rim-c`.
A button that must stay flat (outline, menu rows, alert actions) sets
`content: none` on its `::before`.

**The light, once more.** In dark: fill lighter at the top left and darker
at the bottom right, stroke bright at the top left, inner top edge light.
In light: all mirrored. Selected items in a bar (a lens) have no sheen and
no stroke.

**Disabled:** `opacity: 0.6`, default cursor, in both styles.

### 6.2 Top bar controls

Covered in 5.1. In short, for Glass: one height (48px) for the page bar,
search, theme, bell, avatar pill and Sign In; the page bar is one glass
pill with `4px` padding; its current link is a lens,
`color-mix(in srgb, var(--primary) var(--lens-tint), var(--tint-base))`
with `inset 0 1px 0 var(--glass-hi), var(--accent-shadow)` and
`--on-primary` text; hovered links get `--glass-hover`. The avatar has a
1px `--accent-rim` outline (inset, so it keeps its size) and
`--accent-shadow`.

### 6.3 Tabs

Administration, Space Settings and a page's Comments, Attachments, History
and Restrictions are tab bars built with `OverflowTabs`. **A tab bar never
scrolls sideways**, in either style: the tabs that fit are shown, the rest
go into a ••• button at the end (horizontal dots, not the ⋮ used for a
thing's actions), and that button shows as current when the current tab is
inside it. Widths are measured in an invisible copy (`.tabs__ruler`), as the
top bar does.

| | Minimal | Glass |
|---|---|---|
| Bar | A row with a `--border` bottom line | One glass pill, 48px (`--ctl`), `4px` padding, `2px` gaps, the same edge and shadow as the top bar's Spaces and Admin |
| Tab | `0.5rem 0.9rem`, `--text`, weight 500 | Pill, `0 14px`, `15px`; hover `--glass-hover` |
| Current | `--primary` text and a 2px `--primary` underline, weight 600 | The lens: accent fill, `--on-primary` text, no sheen |
| ••• menu | The standard menu; current item in `--primary`, weight 600 | Frosted menu |

Use `OverflowTabs` for any new tab bar. The bare `.tabs` class still has
`overflow-x: auto` for a single static tab.

### 6.4 Menus and popovers

| | Minimal | Glass |
|---|---|---|
| Appearance menu, notifications, ⋮ menus, the tab ••• menu | `--surface`, `--border`, `6px` corners, `--shadow-lg` | Neutral frost: `var(--panel-bg), color-mix(in srgb, var(--surface) 88%, transparent)`, `blur(20px) saturate(180%)`, `--panel-border`, `14px` corners, `--panel-rim` and `--box-shadow` |
| Selected item (Theme, Style choices) | `--primary-soft` fill, `--primary-dark` text | `color-mix(in srgb, var(--primary) 16%, var(--surface))` plus a 1px inset accent stroke |
| Section rules (between Theme, Style, Accent Color) | `--border` | `--panel-border` |
| Notifications header | Sticky, `--surface` | Sticky, `--surface` at 90% with `blur(14px)` |
| Unread notification | `--primary-softer`; hover `--primary-soft` | Accent at 12%; hover 20% |
| Headings | Small bold capitals in `--text`: THEME, STYLE, ACCENT COLOR, NOTIFICATIONS | Same |
| Opening and closing | Instant | The appearance and notification panels grow out of their button and shrink back into it (section 8) |

Editor popovers (the slash menu, toolbar drop-downs, the link menu, the
status and date menu, the emoji picker, the inline comment thread) and
dialogs stay solid in both styles: they sit over the text being edited.

The accent dots in the appearance menu are 26px circles with a
`--swatch-border` ring; the chosen one has a halo,
`0 0 0 2px var(--surface), 0 0 0 4px var(--primary)`.

### 6.5 Cards and panels

| | Minimal | Glass |
|---|---|---|
| Space cards (`.space-card a`) | `--surface`, `--border`, `6px`, `--shadow-card`; hover accent border | Neutral frost, `18px`; hover: accent border plus 1px inset accent stroke, and a 2px lift |
| KPI cards and dashboard panels (`.stat`, `.dash__panel`) | `--surface`, `--border`, `6px`, `--shadow-card` | Neutral frost, `18px` |
| Administration and Space Settings sections (`.page-wrap--admin` and `.page-wrap--space-settings` `.profile__section` and `.card`, `.about-card`) | `--surface` (or `--bg-alt` for `.card`), `--border`, `6px` | Neutral frost, `18px`; Support Tesria keeps its accent edge; the Danger Zone keeps a red edge (`--danger` at 45%) |
| Security sections, backup sections (`.security__section`, `.backups__section`) | As above | Neutral frost, `18px`; the unsaved backup key keeps its 4px red left stripe |
| Backup cards (`.backup-card`) | As above | Neutral frost, `14px` |
| Branding cards (`.branding__file`) | `--surface`, `--border` | Neutral frost, `18px`; the preview inside keeps its own light or dark ground, `12px` |
| Security alerts (`.alerts__item`) | `--border`, 4px severity stripe on the left, `6px` | Neutral frost, `14px`, the stripe kept |
| Profile page cards | `--surface` | Unchanged (solid) |
| Dialogs, the setup wizard, the tour | `--surface`, `--shadow-lg` | Unchanged (solid) |

**Only the space sidebar carries an accent tint.** Every other frosted
surface uses the neutral `--panel-bg`, which in light is bright white, never
gray. A new card gets:

```css
:root[data-style="glass"] .my-card {
  border: 1px solid var(--panel-border); border-radius: 18px;
  background: var(--panel-bg);
  -webkit-backdrop-filter: blur(20px) saturate(180%);
  backdrop-filter: blur(20px) saturate(180%);
  box-shadow: var(--panel-rim), var(--box-shadow);
}
```

A selected item on a frosted surface gets a 1px accent stroke,
`box-shadow: inset 0 0 0 1px var(--primary)`, inset so it adds nothing to
its size.

### 6.6 Forms

| | Minimal | Glass |
|---|---|---|
| Text inputs, textareas | `--surface`, `--border`, `6px`, `0.5rem 0.6rem`; focus: 2px `--primary` outline at `-1px` | Unchanged (solid): people type and read here |
| Top bar search | 48px pill on `--bg-alt` | 48px glass pill, `blur(14px) saturate(170%)`; focus: 2px `--primary` at `1px` |
| Page tree filter | `0.85rem`, `--surface` | 38px glass pill; in the sidebar it spans the panel with the "with children" toggle inside its right end (4px in, 30px) |
| Select under a label (`label > select`) | Block, `--surface`, `--border`, `6px` | Unchanged |
| Frosted drop-down (`select.glass-select` in a `span.glass-select-wrap`) | An ordinary select (the wrapper is `display: contents`) | 38px glass pill with an accent chevron drawn on the wrapper; no focus ring or border change on click. Where the browser supports `appearance: base-select` (Chrome and Edge 135 and later), the open list is a frosted menu with `8px` rows, hover `--glass-hover` and the chosen option at 14% accent, weight 600, with an accent check; elsewhere the list is the system's |
| Switch (`.switch`, `.switch-row`) | 36 by 20px, knob 16px; off `--text` at 25%, on `--primary`; slides 16px | Same |
| Checkbox, radio | The browser's | Same |

The frosted drop-down is used for the About page's package filter and the
permissions picker. Opt a select in by adding the class and the wrapper.

### 6.7 Badges and the notification count

| | Minimal | Glass |
|---|---|---|
| `.badge` and its variants (`--danger`, `--warning`, `--ok`, `--public`, `--owner`, `--resolved`) | `0.7rem`, weight 600, uppercase, `4px`, tinted fill | Unchanged |
| Notification count (`.notif__badge`) | `--danger` pill, `--on-primary` text, `0.65rem`, weight 700; in the bar 17px high, pinned to the bell icon's corner | 20px pill on the circle's corner (`-3px`), a fixed red gradient from `#c9372c` with white text in both themes, a `--badge-rim` edge and a plain shadow. No glow |

### 6.8 Alerts

| | Minimal | Glass |
|---|---|---|
| Inline messages (`.alert--error`, `--warning`, `--success`) | Tinted fill; warning and success with a 3px left stripe | Unchanged |
| Security alert cards (`.alerts__item`) | Bordered, 4px severity stripe | Frosted card, stripe kept; Acknowledge and Resolve are plain accent text with no frame, `1.25rem` apart |
| Editor callouts (`.panel--info` and the rest) | Tinted fill, colored border, masked icon | Unchanged |

### 6.9 Tables

Tables stay solid and plain in both styles: `.admin-table` has `--border`
row lines and small uppercase `--muted` headers; editor tables have
`--border` cells and a `--bg-alt` header row. In Glass a table sits on the
frost of the card around it (the dashboard's top-ten lists are inside
frosted `.dash__panel` cards) and adds no glass of its own. On a phone a
wide table scrolls sideways inside its own box.

### 6.10 Scrollbars

| | Minimal | Glass |
|---|---|---|
| Look | The browser's own | iOS style: no track, no arrows, a flat, fully rounded `--sb-thumb` gray thumb 6px wide in a 10px bar |
| When shown | Always, as the browser decides | A scroller's thumb shows only while the mouse is in it or it is scrolling, hiding 900ms after the last scroll. The page's own scrollbar always shows |
| Touch screens | The system's | The system's own overlay bars; `scrollbars.ts` does nothing without a fine pointer |
| Firefox | The browser's | `scrollbar-width: thin` and `scrollbar-color` only |

`scrollbars.ts` marks the scrollers under the mouse with `data-sb` and flips
their overflow for one synchronous layout (`.sb-refresh`) so Chrome
repaints the bar. Two cautions: in Chrome, setting `scrollbar-color` or
`scrollbar-width` switches the `::-webkit-scrollbar` parts off, so Glass
sets neither where the parts apply; and a rounded panel that scrolls itself
gives its track `margin-block: 12px` so the thumb stays clear of the
corners (the three frosted menus do).

---

## 7. Elements and exports

Four editor elements have a **Style** of their own: status, chart, code
block and diagram (a Mermaid code block). The picker (`AppearancePicker.tsx`)
offers **Theme default** (as the label reads today), **Minimal** and
**Glass**; it sits in the status menu, the chart's controls and the code
block's header.

- Stored as the node attribute `appearance`: `theme` (the default), `flat`
  or `glass` (`editor/appearance.ts`).
- Rendered as `data-appearance` only when it is not `theme`, so existing
  documents are unchanged.
- **Theme Default follows the reader's style.** Minimal or Glass is kept
  whoever reads the page.
- **Exports keep what the element asked for.** HTML, PDF and site exports
  are captures of the page with no reader's style, so a Theme Default
  element comes out Minimal and an explicit choice comes out as chosen.
- Element glass uses **literal shadow values**, not theme tokens, where an
  export needs them, and **no `backdrop-filter`** on the frame (it sits on
  the solid page, where a blur shows nothing, and a PDF renders plain fills
  more faithfully). The diagram panel is the exception: it blurs the
  console behind it, and an export that cannot blur shows the same fill
  unblurred.

| Element | Minimal | Glass |
|---|---|---|
| **Status** (`.status--red` and so on) | A lozenge: `--status-*-bg` fill, `--status-*-ink` text, weight 700, uppercase, `3px` | A small badge: a saturated diagonal gradient of `--sg-*` (`160deg`, color mixed 72% with white, the color at 45%, 72% with black), `--status-glass-ink` text, monospace weight 600, an inner top highlight and a plain drop shadow (`0 1px 2px rgba(0,0,0,.28), 0 2px 5px -1px rgba(0,0,0,.3)`). Gray (Not Started) keeps the theme's `--status-grey-ink` |
| **Chart** (`.chart`) | No frame when reading; bars flat with `3px` top corners; lines `2px`; flat slices; square swatches | A frosted frame (`--panel-bg`, `18px`, `0.9rem 1rem` padding, `--panel-rim`, `--box-shadow`). Each bar has its own drop shadow and a white gloss fading down; lines have a two-step drop shadow; a pie or donut has a drop shadow, each slice is a gradient of its own color lit from the top left, and light falls on the rims (a soft band and a thin bright line; on a donut both edges). Round legend swatches |
| **Code block** (`.code-block`) | Dark in both themes: `#0b1324` code, `#17203a` header, `6px` | A dark console in both themes: `--term` at 94% with `--term-sheen`, `10px`, `--term-glass-rim` edge, `--code-shadow`. The header (`--term-head`) casts a shadow onto the code and leads with the language, then Copy and line numbers as small pills. **No window dots.** Code at `16px 18px`, line height 1.75. Syntax colors are the same in both |
| **Diagram** (`.code-block--diagram .mermaid`) | The drawing on the page, centered | The console with the drawing on a frosted panel inside it: `--diagram-bg`, `blur(20px) saturate(180%)`, `12px` margin and corners, `--panel-border`, `--panel-rim` |

**Chart colors.** Minimal series colors (`ChartView.tsx`):
`#0c66e4, #00875a, #a54800, #5e4db2, #ae4787, #206a83, #946f00, #bf2600`.
Glass pie and donut slices use a brighter set:
`#1f7bff, #00b86b, #ff7a1a, #8b5cf6, #ec4899, #06b6d4, #f5b800, #ef4444`.
Bars and lines keep the Minimal colors in both styles. The slice gradient
and the rim light are drawn always (`PieChart.tsx`) and switched on by
`glass.css` through `--glass-fill` and `.chart-sheen`, which is what lets an
export keep them. The backups page's disk chart uses the same pie and
donut.

**Diagrams redraw for the theme.** Mermaid cannot read CSS variables, so
`MermaidView.tsx` initializes it with `dark` or `default` before every
drawing and redraws when `data-theme` or the system theme changes.

---

### Exported sites

An exported site (`SiteChrome.cs`) ships the app's compiled stylesheet, so
every rule here applies to it. Its own script applies the Style before first
paint: the reader's choice (`tesria-style`, `flat` stored too, since Minimal
can be a choice against a Glass default), else the look it was exported in
(`data-style-default="glass"`, set when the exporter was in Glass, and by the
Docs export), else Minimal. It docks the bar, marks hovered scrollers
(`data-sb`) and offers Reduce Motion, as the app does. In Glass the page
wrapper (`.export--site`) is transparent so the ground shows, the sidebar
stops above the footer bar, and on a phone the unrolled page list is in the
page's flow. The Pages button (`.site-menu`) always shows at 640px and below,
since an export never measures its bar.

### The editor

In Glass, on a computer, the editing space (`.space-content` holding
`.page-actionbar--editor`) is a pane inset like the sidebar: sticky at the
sidebar's top, the same `0.75rem` margin above and below (so both edges line
up with the sidebar's and never move), `1rem` from the window's right edge,
18px corners, `--surface` fill and `--box-shadow`. Only the page under the
toolbar scrolls (the pane's `.page-column`), so its scrollbar starts below
the toolbar and its track stops 14px short of the rounded bottom corner; the
pane itself does not clip, so toolbar menus can hang past its edge.

## 8. Motion

Motion belongs to Glass. Minimal switches instantly. All of it stops when
the reader turns on **Reduce Motion** (`data-motion="reduce"`) or the
system asks for reduced motion; script-driven motion checks both through
`motionReduced()`, and CSS transitions are cut by
`@media (prefers-reduced-motion: reduce)` rules and by a
`:root[data-motion="reduce"] *` rule that sets `transition: none` and
`animation: none` everywhere (so it also stills the few small transitions
Minimal has, such as the switch knob, if the attribute stays set).

| What | How | Timing | Where |
|---|---|---|---|
| Top bar docks | Frost, edge and shadow fade in once the page scrolls 4px | `0.2s` | `glass.css`, `Layout.tsx` |
| Avatar and name grow when docked | Pill padding goes, avatar to 48px, name to 17px | `0.2s ease` | `glass.css` |
| Appearance and notification panels open | Start as a circle the button's size under the button, slide down onto the panel's place, open out to the full panel with a 1% overshoot | `380ms`, `cubic-bezier(0.2, 0.8, 0.2, 1)` | `popoverMotion.ts` |
| ...and close | Far sides draw in to the circle, which slides up onto the button and fades | `300ms`, `cubic-bezier(0.55, 0, 0.6, 1)`; removed by the clock after 450ms if the browser pauses it | `popoverMotion.ts` |
| Sidebar hides | The right and bottom edges draw in to a 38px circle at the top-left corner, which keeps its shape (a clip, never a scale); the contents fade over the last stretch | `260ms`, `cubic-bezier(0.55, 0, 0.8, 0.2)` | `SpacePage.tsx` |
| ...then | The page's column slides over while the round button bounces in (scale 1, 1.14, 0.95, 1) | column `0.32s cubic-bezier(0.2, 0.8, 0.2, 1)`; bounce `440ms ease-out` | `glass.css` (`.space-layout.is-moving`), `SpacePage.tsx` |
| Sidebar shows | Opens back out of the circle, settling with a 1.2% overshoot | `420ms`, `cubic-bezier(0.2, 0.8, 0.2, 1)` | `SpacePage.tsx` |
| Space card hover | Lifts 2px, accent stroke appears | `0.18s ease` | `glass.css` |

Rules for new motion: use it only in Glass; start from and return to the
control that caused it; use a clip rather than a scale for a panel with
rounded corners; check `motionReduced()` in script and add a
`prefers-reduced-motion` rule in CSS; keep it under half a second.

---

## 9. Accessibility

**Focus.** Keep a visible focus ring on every control.

- Glass buttons, round buttons, the bar's links and tabs:
  `outline: 2px solid var(--primary); outline-offset: 2px` on
  `:focus-visible`.
- Inputs and textareas: `2px solid var(--primary)` at `-1px` (Minimal);
  Glass search and filter at `1px`.
- A switch row rings the switch; a Page Tree style option rings on
  `:focus-within`.
- Other controls (Minimal buttons, the Glass ⋮ trigger) use the browser's
  own ring. Never remove it without a replacement. The frosted drop-down's missing ring (the owner's
  call, because Chrome treats a click on a select as keyboard focus) is
  the one exception today.

**Contrast.** Text must pass WCAG AA (4.5:1, or 3:1 for large text) against
the worst background likely to show through. The accent sets were
measured. Where content scrolls under glass, raise the fill toward solid
(menus use 88% surface, the notifications header 90%). Measured for the
Glass status badge at the gradient's middle stop:

| Status | Light (white ink) | Dark (`--bg` ink) |
|---|---|---|
| Red | 4.52 | 5.67 |
| Yellow | 4.60 | 10.82 |
| Green | 5.01 | 10.25 |
| Blue | 4.98 | 6.19 |
| Purple | 6.25 | 5.82 |

Every light value passes 4.5:1 at the middle stop (yellow and green were
deepened on 2026-09-28 to get there). The lighter top-left of the gradient
lowers it a little; keep any new status color at 4.5:1 or better at its
middle stop, and never rely on color alone for meaning (the label says it).

**Reduced motion and transparency.**

- Reduce Motion (the switch) and the system setting both stop every Glass
  animation (section 8).
- Without `backdrop-filter`: `--glass` becomes
  `color-mix(in srgb, var(--surface) 88%, transparent)`.
- With `prefers-reduced-transparency: reduce`: `--glass` becomes
  `--surface`, the docked bar and the open menu become `--surface`, and
  every `backdrop-filter` in Glass is removed. The panel tokens
  (`--panel-bg`, `--sidebar-bg`, `--diagram-bg`) become solid `--surface`
  too. The override is written `:root:root:root` so it outranks the dark
  theme's own tokens.

**Touch targets.** Top bar controls are 48px; Glass buttons below the bar
38px; the editor toolbar's buttons 36px in both styles (the trade-off
Google Docs makes, so twenty buttons fit in a row). New controls should be
at least 38px in Glass and never smaller than the toolbar's 36px where
people tap. Rows in the phone menu's tree get extra padding for the same
reason.

**Semantics.** Icon-only buttons have an `aria-label` ("Toggle navigation",
"Notifications", "More tabs"); pressed choices use `aria-pressed`; Reduce
Motion is `role="switch"` with `aria-checked`; charts have an `aria-label`
carrying their numbers; text shown only as a picture has a screen-reader
copy (`.visually-hidden`, `.sr-only`).

---

## 10. Adding a new component: a checklist

1. **Minimal first.** Style it in `index.css` with tokens only: `--surface`
   or `--bg-alt`, `--border`, `--radius`, `--shadow-card` for a card,
   `--shadow-lg` for a floating menu. No hex values in components.
2. **Heights.** In the top bar, `var(--ctl)`; below it, buttons at
   `var(--ctl-page)` in Glass. Whole pixels only.
3. **Glass second.** Add `:root[data-style="glass"]` rules in `glass.css`,
   in the section for its kind. Reuse a recipe: a clear glass pill
   (secondary button), the colored-button recipe (3.7), the lens (current
   item in a bar), neutral frost (card or menu, 6.5), or the console.
4. **Choose its surface.** Chrome or a container? Frost it. Content people
   read or type in? Keep it solid. Sitting on frost already? No glass of
   its own.
5. **Accent only for "this one".** Current, selected or primary. A
   selected item on a frosted surface gets the 1px inset accent stroke.
6. **Check the light.** Dark: lit top left. Light: mirrored. Use the
   tokens, which flip for you; do not hard-code a direction.
7. **Both themes, both styles.** Look at it in Minimal light, Minimal dark,
   Glass light and Glass dark, with at least two accents (blue and a dark
   one such as orange).
8. **Text.** `--text` for anything navigable, `--muted` only for secondary
   text, `--on-primary` on the accent. Title Case for its name and labels.
9. **Tokens that depend on other tokens** are built as their own token, and
   a color used as a background layer is wrapped in `linear-gradient(c, c)`
   (section 2, pitfalls).
10. **Menus and bars.** A tab bar uses `OverflowTabs`. A menu that floats
    over the page uses the frosted menu recipe in Glass and a `z-index`
    above any frosted card near it.
11. **Motion**, if any: Glass only, from and back to its button, stopped by
    `motionReduced()` and by `prefers-reduced-motion`.
12. **Degrade.** Check it with `backdrop-filter` off and with reduced
    transparency: text must stay readable.
13. **Focus and size.** A visible focus ring; a touch target of at least
    38px in Glass.
14. **Phones.** Check it at 375px and on a desktop. A popover that is wider
    than a phone pins to the screen's side margins. Nothing may widen the
    page.
15. **Windows.** If it is a button, it inherits the 1px label nudge; a
    control that centers text another way needs its own.
16. **An editor element?** Give it `appearance` (`appearanceAttribute`),
    render `data-appearance` with `appearanceData()`, write its Glass
    selectors in the paired form (section 2), use literal shadows, and
    export a page to check it (both a Theme Default and an explicit choice).
17. **Record it.** Update this guide, `docs/architecture.md` if the
    mechanism changed, and the CHANGELOG.

---

## 11. Where the website's glass guide differs

The app's Glass was built from tesria.com's own glass guide (its values
live in the site's `src/styles/global.css`). These are the places the app
deliberately goes another way, mostly decided with the owner on 2026-09-27
and 28. The website team may want to bring some of them back to the site.

| Topic | tesria.com guide | Tesria app | Why |
|---|---|---|---|
| Where glass goes | Chrome only; menus, cards and tables solid | Chrome, the sidebar, menus and cards are frosted; content, forms, tables, dialogs and editor popovers stay solid | The owner wanted Administration and the spaces list to read as one frosted set |
| Menus and popovers | Solid `--surface`, `4px` corners, `6px` padding | Frosted: `--panel-bg` over 88% surface, `14px` corners, `blur(20px)` | Denser than a card so text stays readable over busy content |
| Selected menu item | `color-mix(brand 16%, surface)` | The same, plus a 1px inset accent stroke | Selected items on any frosted surface get the stroke |
| Dark glass fill | Navy, `rgba(22, 38, 61, .42)` | `color-mix(in srgb, var(--surface) 60%, transparent)` | The app's dark ground is charcoal (`#161a1d`), not navy |
| Glass edge | `--glass-rim`, white | `--glass-border`, dark in light (`rgba(10, 20, 34, .12)`), and a gradient `--btn-rim` stroke on buttons | White glass on a white page needs a dark edge |
| Sheen in light | White at the top left, fading | Dark at the top left, white at the bottom right | Light is mirrored in light (the owner, 2026-09-28) |
| Primary button | A flat tint (`--cta-tint`) with a whitish `--accent-rim` border | A diagonal gradient from its own color, a stroke mixed from that color, mirrored between themes | A white sheen over the color turned it milky |
| Button size | Padding `11px 22px`, small `6px 12px`; header 48px, 44px below 1180px | Fixed heights: 48px in the bar at every width, 38px below it (small too) | One height per zone; the bar collapses rather than shrinking |
| Button shadow | `--glass-shadow` | `--btn-shadow`, a close shadow plus a soft one, heavier in light | Buttons read as floating faintly otherwise |
| Docked header | Pads from 22px to 12px; single buttons keep their glass | Fixed 64px bar; the avatar pill drops its glass and the avatar and name grow to 48px; frost on a `::before` layer | No glass on glass; a frosted bar would stop its menus blurring the page |
| `--ground` | The page color | The page color plus two soft radial accent washes | Glass needs something to be glass over |
| Code blocks | Light and dark consoles; window dots; `--glass` Copy pill; `--box-shadow`; `backdrop-filter` | Dark console in both themes; no window dots; Copy and line numbers as `--term-glass` pills; `--code-shadow`; no blur on the frame | Code blocks were already dark in both themes; the owner removed the dots |
| Status badges | Top-to-bottom gradient, a glow in its own color | Diagonal gradient, plain drop shadow, no glow | A glow read as an alarm |
| Notification count | Not covered | A fixed red, top-to-bottom gradient, plain shadow | Same reason |
| Icon tiles | Glass tiles for feature icons | Not used | Not needed in the app yet |
| `scroll-padding-top` | Docked height plus 12px | Not set | |
| Not in the site's guide | | The frosted sidebar and its hide animation, menu motion, Reduce Motion, iOS scrollbars, tab bars that never scroll, the frosted drop-down, outline buttons, frosted charts and diagrams, per-element Style | Added in the app for 0.8.1 |

The shared parts are unchanged: the pill page bar with a tinted lens for
the current page (`--lens-tint` 82% light, 80% dark), `--tint-base`,
`--cta-tint`, `--accent-shadow`, `--hdr-shadow`, the `blur(14px)
saturate(170%)` control blur, the 4px dock threshold and `blur(8px)` strip,
circles for icon-only buttons, the rule that text on an accent uses the
accent's own ink, and the degrade to `color-mix(surface 88%)` without
`backdrop-filter`.

---

## Known gaps

Where the code does not yet follow a rule above (2026-09-28):

- **Minimal buttons are not 38px.** The 38px page-button height is Glass's
  (`glass.css`, `.btn`, `min-height: var(--ctl-page)`). Minimal's `.btn` is
  sized by its padding (about 34px), its ⋮ trigger is 34px, the tree toggle
  `2rem` and the sidebar toggle 30px (`index.css`). Whether Minimal should
  take the same heights is an open decision.
- **The ⋮ menus and the tab ••• menu open instantly.** Only the appearance
  and notification panels grow from their buttons (`usePopoverMotion`).
- **Alert actions** are plain text in Glass only; in Minimal they are
  ordinary buttons.

Exceptions by design, not gaps:

- **Glass scrollbars:** the page's own scrollbar always shows (Chrome will
  not repaint the window's scrollbar on demand, and the pointer is always
  in the page while it is in use); inner scrollers hide theirs.
- **The frosted drop-down** shows no focus ring or border change when
  clicked (the owner's decision); its shading still marks it.
