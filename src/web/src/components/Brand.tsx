import type { ReactNode } from 'react'
import { DEFAULT_BRANDING, type BrandLogo, type Branding } from '../api/client'
import { useInstance } from '../InstanceContext'
import { BrandMark } from './BrandMark'

/**
 * The instance's brand (dev-plan 13.1): its logo and name, or Tesria's mark
 * and "Tesria" until someone sets them on purpose.
 *
 * The markup and classes are the ones `SiteChrome.Topbar` emits for exports,
 * so the stylesheet an export ships sizes the logo the same way. A logo is
 * only ever an <img>, never inlined, which is what makes an uploaded SVG
 * unable to run anything whatever it contains (decision 6).
 */

export function useBranding(): Branding {
  return useInstance()?.branding ?? DEFAULT_BRANDING
}

function LogoImg({ logo, dark, hasDark, alt }: { logo: BrandLogo; dark: boolean; hasDark: boolean; alt: string }) {
  const cls = !hasDark ? 'brand__logo' : dark ? 'brand__logo brand__logo--dark' : 'brand__logo brand__logo--light'
  return (
    <img
      className={cls}
      src={logo.url}
      alt={alt}
      width={logo.width ?? undefined}
      height={logo.height ?? undefined}
      // Decoded off the main thread; it is small, but it is on every page.
      decoding="async"
    />
  )
}

/** The logo, with its dark-mode twin when there is one; or Tesria's mark. */
function Mark({ branding, markSize }: { branding: Branding; markSize: number }) {
  if (!branding.logo) return <span className="brand__mark" aria-hidden="true"><BrandMark size={markSize} /></span>
  // Alone, the logo stands for the name, so it carries it; beside the name
  // it would be read twice.
  const alt = branding.display === 'logo' ? branding.name : ''
  return (
    <span className="brand__logo-wrap">
      <LogoImg logo={branding.logo} dark={false} hasDark={!!branding.logoDark} alt={alt} />
      {branding.logoDark && <LogoImg logo={branding.logoDark} dark hasDark alt={alt} />}
    </span>
  )
}

/** What the header shows in its top-left corner. */
export function BrandLockup() {
  const branding = useBranding()
  const showMark = branding.display !== 'name'
  // "Logo only" with no logo would leave the corner empty; the server already
  // falls back, and this agrees with it.
  const showName = branding.display !== 'logo' || !branding.logo
  return (
    <>
      {showMark && <Mark branding={branding} markSize={20} />}
      {showName && <span className="brand__word">{branding.name}</span>}
    </>
  )
}

/**
 * The brand above the sign-in card (decision 16), side by side by default,
 * `[Tesria mark] Tesria` until branded, or stacked, logo only or name only
 * as configured.
 */
export function AuthBrand() {
  const branding = useBranding()
  const showMark = branding.display !== 'name'
  const showName = branding.display !== 'logo' || !branding.logo
  const stacked = showMark && showName && branding.signInArrangement === 'stacked'
  const cls = ['auth-brand', stacked ? 'auth-brand--stacked' : '', showMark && !showName ? 'auth-brand--logo' : '']
    .filter(Boolean).join(' ')
  return (
    <div className={cls}>
      {showMark && <Mark branding={branding} markSize={36} />}
      {showName && <span className="auth-brand__name">{branding.name}</span>}
    </div>
  )
}

/**
 * One muted line under a branded sign-in card (decision F). Not shown on an
 * unbranded instance, where Tesria's mark and name are already above the
 * card and saying it twice would be the opposite of subtle.
 */
export function PoweredBy() {
  const branding = useBranding()
  if (!branding.hasIdentity) return null
  return (
    <p className="powered-by">
      Powered by{' '}
      <a href="https://brianintheloop.com/tesria" target="_blank" rel="noopener noreferrer">Tesria</a>
    </p>
  )
}

/** The frame every sign-in card sits in: the brand above it, the attribution below. */
export function AuthPage({ children }: { children: ReactNode }) {
  return (
    <div className="center">
      <div className="auth-page">
        <AuthBrand />
        {children}
        <PoweredBy />
      </div>
    </div>
  )
}
