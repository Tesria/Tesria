import { useEffect, useMemo, useRef, useState } from 'react'
import {
  api,
  ApiError,
  type AccentCheck,
  type AccentPolicy,
  type BrandDisplay,
  type BrandingSettings,
  type BrandLogo,
  type SignInArrangement,
  type ThemePolicy,
} from '../../api/client'
import { BrandMark } from '../../components/BrandMark'
import { useConfirm } from '../../components/ConfirmDialog'
import { useReloadInstance } from '../../InstanceContext'
import { ACCENTS } from '../../theme'

/**
 * Administration → Branding (dev-plan 13.1).
 *
 * Nothing here changes what anyone sees until it is saved, and every default
 * is Tesria's. Files are saved the moment they are chosen, because a file
 * picked and then forgotten about is a worse surprise than one that applied;
 * everything else waits for Save, because a color half-typed should not be
 * on everyone's screen.
 */

type Form = {
  brandName: string
  display: BrandDisplay
  signInArrangement: SignInArrangement
  themePolicy: ThemePolicy
  accentPolicy: AccentPolicy
  /** One of the six, or "brand" for the custom colors. */
  accent: string
  accentLight: string
  accentDark: string
}

const NEUTRAL = { light: '#172b4d', dark: '#b6c2cf', bgLight: '#ffffff', bgDark: '#161a1d' }

/**
 * Each built-in accent's --primary in each theme, as index.css defines them.
 * Not ACCENT_HEX from theme.ts: that is the favicon's palette, tuned for a
 * tab strip, and it is close to these but not the same.
 */
const PRIMARY: Record<string, { light: string; dark: string }> = {
  blue: { light: '#0c66e4', dark: '#579dff' },
  teal: { light: '#0b6b82', dark: '#6cc3e0' },
  green: { light: '#1a6c45', dark: '#4bce97' },
  purple: { light: '#5b47ba', dark: '#b8acf6' },
  orange: { light: '#9a4d00', dark: '#fea362' },
  magenta: { light: '#a53a7f', dark: '#f797d2' },
}

function formOf(s: BrandingSettings): Form {
  return {
    brandName: s.brandName ?? '',
    display: s.display,
    signInArrangement: s.signInArrangement,
    themePolicy: s.themePolicy,
    accentPolicy: s.accentPolicy,
    accent: s.accentName ?? 'blue',
    accentLight: s.accentLight ?? '',
    accentDark: s.accentDark ?? '',
  }
}

/** A text field's color as #rrggbb, or null while it is not one yet. */
function hex(value: string): string | null {
  let v = value.trim()
  if (!v) return null
  if (!v.startsWith('#')) v = `#${v}`
  if (/^#[0-9a-f]{3}$/i.test(v)) v = `#${v[1]}${v[1]}${v[2]}${v[2]}${v[3]}${v[3]}`
  return /^#[0-9a-f]{6}$/i.test(v) ? v.toLowerCase() : null
}

function errorOf(err: unknown, fallback: string): string {
  if (err instanceof ApiError) {
    const first = Object.values(err.fieldErrors ?? {})[0]?.[0]
    return first ?? err.message ?? fallback
  }
  return fallback
}

export function AdminBrandingPage() {
  const [settings, setSettings] = useState<BrandingSettings | null>(null)
  const [form, setForm] = useState<Form | null>(null)
  const [checks, setChecks] = useState<AccentCheck[]>([])
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const reloadInstance = useReloadInstance()
  const { ask, dialog } = useConfirm()

  useEffect(() => {
    api.admin.branding.get()
      .then((s) => { setSettings(s); setForm(formOf(s)) })
      .catch(() => setError('Could not load the branding.'))
  }, [])

  const custom = form?.accent === 'brand'
  const needsLight = form?.themePolicy !== 'dark'
  const needsDark = form?.themePolicy !== 'light'
  const light = form ? hex(form.accentLight) : null
  const dark = form ? hex(form.accentDark) : null

  // The readability check follows the colors as they are typed, a moment
  // after the typing stops (decision 5).
  useEffect(() => {
    if (!custom) { setChecks([]); return }
    const l = needsLight ? light : null
    const d = needsDark ? dark : null
    if (!l && !d) { setChecks([]); return }
    const timer = window.setTimeout(() => {
      api.admin.branding.preview(l, d).then(setChecks).catch(() => setChecks([]))
    }, 250)
    return () => window.clearTimeout(timer)
  }, [custom, light, dark, needsLight, needsDark])

  if (!settings || !form) return <p className="muted">{error ?? 'Loading…'}</p>

  const set = <K extends keyof Form>(key: K, value: Form[K]) => setForm((f) => (f ? { ...f, [key]: value } : f))

  /** After a file changes: the header picks it up now; the tab icon on the next page load. */
  async function afterFile(next: BrandingSettings, message: string) {
    setSettings(next)
    setStatus(message)
    setError(null)
    await reloadInstance()
  }

  async function run(action: () => Promise<BrandingSettings>, message: string, failure: string) {
    setBusy(true)
    setStatus(null)
    try {
      await afterFile(await action(), message)
    } catch (err) {
      setError(errorOf(err, failure))
    } finally {
      setBusy(false)
    }
  }

  async function save() {
    if (!form || !settings) return
    setBusy(true)
    setStatus(null)
    setError(null)
    try {
      // Blue with people free to choose is Tesria's default, not a choice
      // worth recording: stored as nothing, so the instance stays unbranded.
      const accentName = form.accent === 'blue' && form.accentPolicy === 'any' ? null : form.accent
      const next = await api.admin.branding.save({
        brandName: form.brandName.trim() || null,
        display: form.display,
        signInArrangement: form.signInArrangement,
        themePolicy: form.themePolicy,
        accentPolicy: form.accentPolicy,
        accentName,
        accentLight: custom && needsLight ? light : null,
        accentDark: custom && needsDark ? dark : null,
      })
      // Colors and locks live in the page the server sends (so that nobody
      // sees a flash of the wrong theme), which means a reload to show them.
      const lookChanged = next.themePolicy !== settings.themePolicy || next.accentPolicy !== settings.accentPolicy
        || next.accentName !== settings.accentName || next.accentLight !== settings.accentLight
        || next.accentDark !== settings.accentDark
      if (lookChanged) {
        window.location.reload()
        return
      }
      await afterFile(next, 'Branding saved.')
      setForm(formOf(next))
    } catch (err) {
      setError(errorOf(err, 'Could not save the branding.'))
    } finally {
      setBusy(false)
    }
  }

  async function reset() {
    const ok = await ask({
      title: 'Reset to Tesria?',
      body: (
        <>
          <p>This removes the brand name, the logos and the favicon, and puts back Tesria&rsquo;s colors with nobody held to a theme or accent.</p>
          <p>The uploaded files are deleted and cannot be brought back. The instance name is not changed.</p>
        </>
      ),
      confirmLabel: 'Reset to Tesria',
      danger: true,
    })
    if (!ok) return
    setBusy(true)
    try {
      await api.admin.branding.reset()
      window.location.reload()
    } catch (err) {
      setError(errorOf(err, 'Could not reset the branding.'))
      setBusy(false)
    }
  }

  const bothShown = form.display === 'logo-and-name'
  const dirty = JSON.stringify(form) !== JSON.stringify(formOf(settings))

  return (
    <div className="branding">
      {dialog}
      {error && <p className="alert alert--error">{error}</p>}
      {status && <p className="profile__ok">{status}</p>}

      <section className="profile__section profile__section--wide">
        <h2>Preview</h2>
        <p className="muted small">How the header and the sign-in page will look, before anything is saved.</p>
        <div className="branding-preview">
          {(['light', 'dark'] as const)
            .filter((mode) => (mode === 'light' ? needsLight : needsDark))
            .map((mode) => (
              <BrandPreview key={mode} mode={mode} form={form} settings={settings} light={light} dark={dark} />
            ))}
        </div>
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Name and logo</h2>
        <label className="branding__field">
          <span>Brand name</span>
          <input
            value={form.brandName}
            placeholder="Tesria"
            maxLength={60}
            onChange={(e) => set('brandName', e.target.value)}
          />
          <span className="muted small">
            Shown beside the logo in the header and on the sign-in page. Leave it empty for Tesria. The
            instance name in Settings is separate and does not change the header.
          </span>
        </label>

        <fieldset className="branding__choice">
          <legend>What to show</legend>
          {([
            ['logo-and-name', 'Logo and name'],
            ['logo', 'Logo only, for a logo that already contains the name'],
            ['name', 'Name only'],
          ] as const).map(([value, label]) => (
            <label key={value} className="admin__toggle admin__toggle--inline">
              <input type="radio" name="display" checked={form.display === value} onChange={() => set('display', value)} />
              <span>{label}</span>
            </label>
          ))}
          {form.display === 'logo' && !settings.logo && (
            <p className="muted small">Until a logo is uploaded, the name is shown instead.</p>
          )}
        </fieldset>

        <fieldset className="branding__choice" disabled={!bothShown}>
          <legend>On the sign-in page</legend>
          {([
            ['side-by-side', 'Logo and name side by side'],
            ['stacked', 'Logo above the name'],
          ] as const).map(([value, label]) => (
            <label key={value} className="admin__toggle admin__toggle--inline">
              <input type="radio" name="arrangement" checked={form.signInArrangement === value}
                onChange={() => set('signInArrangement', value)} />
              <span>{label}</span>
            </label>
          ))}
          {!bothShown && <p className="muted small">Only matters when both the logo and the name are shown.</p>}
        </fieldset>

        <div className="branding__files">
          <FileSlot
            title="Logo"
            hint="SVG, PNG, JPEG or WebP. SVG stays sharp at every size."
            logo={settings.logo}
            accept=".svg,image/svg+xml,image/png,image/jpeg,image/webp"
            busy={busy}
            onPick={(file) => run(() => api.admin.branding.uploadLogo(file), 'Logo uploaded.', 'Could not upload the logo.')}
            onRemove={() => run(() => api.admin.branding.removeLogo(), 'Logo removed.', 'Could not remove the logo.')}
          />
          <FileSlot
            title="Logo for dark mode"
            hint="Optional. For a logo that disappears on a dark background; without one, the logo above is used in both."
            logo={settings.logoDark}
            dark
            accept=".svg,image/svg+xml,image/png,image/jpeg,image/webp"
            busy={busy}
            onPick={(file) => run(() => api.admin.branding.uploadLogo(file, true), 'Dark-mode logo uploaded.', 'Could not upload the logo.')}
            onRemove={() => run(() => api.admin.branding.removeLogo(true), 'Dark-mode logo removed.', 'Could not remove the logo.')}
          />
          <FileSlot
            title="Favicon"
            hint="The tab icon. Square works best: SVG, PNG or ICO. Separate from the logo, because a wide logo makes a poor 16-pixel icon."
            favicon={settings.faviconHash}
            accept=".svg,.ico,image/svg+xml,image/png,image/x-icon,image/vnd.microsoft.icon,image/jpeg,image/webp"
            busy={busy}
            onPick={(file) => run(() => api.admin.branding.uploadFavicon(file), 'Favicon uploaded. Tabs show it as each page loads.', 'Could not upload the favicon.')}
            onRemove={() => run(() => api.admin.branding.removeFavicon(), 'Favicon removed.', 'Could not remove the favicon.')}
          />
        </div>
      </section>

      <section className="profile__section profile__section--wide">
        <h2>Theme and color</h2>
        <fieldset className="branding__choice">
          <legend>Theme</legend>
          {([
            ['any', 'Let people choose light, dark or their system setting'],
            ['light', 'Light only'],
            ['dark', 'Dark only'],
          ] as const).map(([value, label]) => (
            <label key={value} className="admin__toggle admin__toggle--inline">
              <input type="radio" name="theme" checked={form.themePolicy === value} onChange={() => set('themePolicy', value)} />
              <span>{label}</span>
            </label>
          ))}
        </fieldset>

        <fieldset className="branding__choice">
          <legend>Accent color</legend>
          <div className="branding__swatches">
            {ACCENTS.map((a) => (
              <button
                key={a.name}
                type="button"
                className={form.accent === a.name ? 'theme-menu__accent is-active' : 'theme-menu__accent'}
                style={{ background: `var(--accent-dot-${a.name})` }}
                title={a.label}
                aria-label={a.label}
                aria-pressed={form.accent === a.name}
                onClick={() => set('accent', a.name)}
              />
            ))}
            <button
              type="button"
              className={custom ? 'btn btn--primary btn--sm' : 'btn btn--ghost btn--sm'}
              aria-pressed={custom}
              onClick={() => set('accent', 'brand')}
            >
              Custom…
            </button>
          </div>

          {custom && (
            <div className="branding__colors">
              {needsLight && (
                <ColorField label="Light mode" value={form.accentLight} onChange={(v) => set('accentLight', v)}
                  check={checks.find((c) => c.mode === 'light')} />
              )}
              {needsDark && (
                <ColorField label="Dark mode" value={form.accentDark} onChange={(v) => set('accentDark', v)}
                  check={checks.find((c) => c.mode === 'dark')} />
              )}
            </div>
          )}

          <label className="admin__toggle admin__toggle--inline branding__lock">
            <input type="checkbox" checked={form.accentPolicy === 'locked'}
              onChange={(e) => set('accentPolicy', e.target.checked ? 'locked' : 'any')} />
            <span>Use this accent for everyone. People can no longer pick their own.</span>
          </label>
          {form.accentPolicy === 'any' && form.accent !== 'blue' && (
            <p className="muted small">People who have not picked an accent get this one, and can still change it.</p>
          )}
        </fieldset>
      </section>

      <div className="branding__actions">
        <button type="button" className="btn btn--primary" disabled={busy || !dirty} onClick={save}>Save</button>
        <button type="button" className="btn btn--ghost" disabled={busy || !dirty} onClick={() => setForm(formOf(settings))}>
          Discard changes
        </button>
        <span className="branding__spacer" />
        <button type="button" className="btn btn--danger" disabled={busy || !settings.isCustomized} onClick={reset}>
          Reset to Tesria
        </button>
      </div>
      {settings.changedAt && (
        <p className="muted small">
          Last changed {new Date(settings.changedAt).toLocaleString()}
          {settings.changedByName && <> by {settings.changedByName}</>}.
        </p>
      )}
    </div>
  )
}

/** A color, its readability, and the better shade when it needs one (decision 5). */
function ColorField({ label, value, onChange, check }: {
  label: string
  value: string
  onChange: (value: string) => void
  check: AccentCheck | undefined
}) {
  const color = hex(value)
  return (
    <div className="branding__color">
      <label className="branding__field">
        <span>{label}</span>
        <span className="branding__color-inputs">
          <input type="color" value={color ?? '#000000'} onChange={(e) => onChange(e.target.value)} aria-label={`${label} color picker`} />
          <input value={value} placeholder="#0c66e4" onChange={(e) => onChange(e.target.value)} spellCheck={false} />
        </span>
      </label>
      {value && !color && <p className="alert alert--error small">Enter a color as #rrggbb.</p>}
      {check && (
        check.passes ? (
          <p className="muted small">
            Readable: {check.primaryVsBackground}:1 as link text, {check.onPrimaryVsPrimary}:1 on buttons.
          </p>
        ) : (
          <div className="branding__warning">
            <p className="small">
              Hard to read: {check.primaryVsBackground}:1 as link text and {check.onPrimaryVsPrimary}:1 on buttons,
              where 4.5:1 is the usual minimum. Some people will struggle with links and buttons in this color.
            </p>
            {check.suggested && (
              <p className="small">
                <span className="branding__chip" style={{ background: check.suggested }} aria-hidden="true" />{' '}
                {check.suggested} is the nearest shade that reads well.{' '}
                <button type="button" className="link-btn" onClick={() => onChange(check.suggested!)}>Use it</button>
                {' '}or keep yours: it is your call.
              </p>
            )}
          </div>
        )
      )}
    </div>
  )
}

/** One upload slot: what is there now, and replacing or removing it. Applies immediately. */
function FileSlot({ title, hint, logo, dark, favicon, accept, busy, onPick, onRemove }: {
  title: string
  hint: string
  logo?: BrandLogo | null
  dark?: boolean
  favicon?: string | null
  accept: string
  busy: boolean
  onPick: (file: File) => void
  onRemove: () => void
}) {
  const input = useRef<HTMLInputElement>(null)
  const has = logo !== undefined ? !!logo : !!favicon
  const soft = logo && logo.format !== 'svg' && logo.height !== null && logo.height < 150
  return (
    <div className={dark ? 'branding__file branding__file--dark' : 'branding__file'}>
      <p className="branding__file-title">{title}</p>
      <div className="branding__file-preview">
        {logo && <img src={logo.url} alt="" className="branding__file-logo" />}
        {favicon && <img src={`/api/branding/favicon-180.png?v=${favicon}`} alt="" className="branding__file-favicon" />}
        {!has && <span className="muted small">None</span>}
      </div>
      <p className="muted small">{hint}</p>
      {soft && (
        <p className="small branding__warning">
          This image is {logo!.height}px tall, so it will look soft on the sign-in page. An SVG, or a PNG at
          least 150px tall, stays sharp.
        </p>
      )}
      <input
        ref={input}
        type="file"
        accept={accept}
        hidden
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = ''
          if (file) onPick(file)
        }}
      />
      <div className="row-gap">
        <button type="button" className="btn btn--sm" disabled={busy} onClick={() => input.current?.click()}>
          {has ? 'Replace' : 'Upload'}
        </button>
        {has && <button type="button" className="btn btn--ghost btn--sm" disabled={busy} onClick={onRemove}>Remove</button>}
      </div>
    </div>
  )
}

/**
 * The header bar and sign-in block as they will look in one theme, drawn
 * from the unsaved form. The page's own theme cannot show both at once, so
 * this sets its colors directly rather than through the theme tokens.
 */
function BrandPreview({ mode, form, settings, light, dark }: {
  mode: 'light' | 'dark'
  form: Form
  settings: BrandingSettings
  light: string | null
  dark: string | null
}) {
  const accent = useMemo(() => {
    if (form.accent === 'brand') return (mode === 'light' ? light ?? dark : dark ?? light) ?? PRIMARY.blue[mode]
    return (PRIMARY[form.accent] ?? PRIMARY.blue)[mode]
  }, [form.accent, mode, light, dark])

  const logo = mode === 'dark' ? settings.logoDark ?? settings.logo : settings.logo
  const name = form.brandName.trim() || 'Tesria'
  const showMark = form.display !== 'name'
  const showName = form.display !== 'logo' || !logo
  const stacked = showMark && showName && form.signInArrangement === 'stacked'
  const fg = mode === 'light' ? NEUTRAL.light : NEUTRAL.dark
  const bg = mode === 'light' ? NEUTRAL.bgLight : NEUTRAL.bgDark

  const mark = (size: number, height: number) =>
    logo
      ? <img src={logo.url} alt="" style={{ height, width: 'auto', maxWidth: '100%' }} />
      : <span style={{ color: accent, display: 'inline-flex' }}><BrandMark size={size} /></span>

  return (
    <div className="branding-preview__panel" style={{ background: bg, color: fg }}>
      <p className="branding-preview__label">{mode === 'light' ? 'Light' : 'Dark'}</p>
      <div className="branding-preview__bar" style={{ borderColor: mode === 'light' ? '#dcdfe4' : '#2c333a' }}>
        {showMark && mark(20, 24)}
        {showName && <strong>{name}</strong>}
        <span className="branding-preview__link" style={{ color: accent }}>Spaces</span>
      </div>
      <div className={stacked ? 'branding-preview__signin branding-preview__signin--stacked' : 'branding-preview__signin'}>
        {showMark && mark(36, stacked || !showName ? 72 : 48)}
        {showName && <span className="auth-brand__name">{name}</span>}
      </div>
      <span className="branding-preview__button"
        style={{ background: accent, color: mode === 'light' ? '#ffffff' : '#1d2125' }}>Sign in</span>
    </div>
  )
}
