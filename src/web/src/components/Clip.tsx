import { useEffect, useRef, useState } from 'react'

/**
 * One of the onboarding clips (dev-plan 10.4), played as a loop.
 *
 * Three things decide what is shown, in this order:
 *
 * * **Reduced motion.** Somebody who has asked the system for less movement
 *   gets the poster, a still of the clip's final frame. This is not a
 *   preference about video players; a looping animation beside text is
 *   exactly what the setting is for.
 * * **The theme.** Every clip exists in light and dark. Which one to fetch is
 *   decided here rather than by two `<source>` elements, because the app's
 *   theme is its own setting and only sometimes follows the system.
 * * **Whether it plays at all.** If the video errors or never starts, the
 *   poster stays. Onboarding that shows a black rectangle is worse than
 *   onboarding that shows a picture.
 */
export function Clip({ name, className }: { name: string; className?: string }) {
  const [theme, setTheme] = useState(currentTheme)
  const [reduced, setReduced] = useState(prefersReducedMotion)
  const [failed, setFailed] = useState(false)
  const video = useRef<HTMLVideoElement>(null)

  useEffect(() => {
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)')
    const dark = window.matchMedia('(prefers-color-scheme: dark)')
    const sync = () => { setReduced(motion.matches); setTheme(currentTheme()) }
    motion.addEventListener('change', sync)
    dark.addEventListener('change', sync)
    // The app writes its choice to <html data-theme>, so watch that too.
    const observer = new MutationObserver(sync)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    return () => {
      motion.removeEventListener('change', sync)
      dark.removeEventListener('change', sync)
      observer.disconnect()
    }
  }, [])

  const poster = `/onboarding/${name}.${theme}.png`
  const clip = `/onboarding/${name}.${theme}.webm`

  // The autoplay attribute is not enough on its own: a browser may decline it
  // (phones are stricter) or simply not start, which leaves a frozen frame
  // where an illustration should be. Ask explicitly, and take a refusal as
  // "show the still" rather than leaving it stuck.
  useEffect(() => {
    if (reduced || failed) return
    const el = video.current
    if (!el) return
    let cancelled = false
    el.play().catch(() => { if (!cancelled) setFailed(true) })
    return () => { cancelled = true }
  }, [clip, reduced, failed])

  if (reduced || failed) {
    return <img className={className} src={poster} alt="" />
  }

  return (
    <video
      ref={video}
      className={className}
      src={clip}
      poster={poster}
      autoPlay
      loop
      muted
      playsInline
      // No controls: it is an illustration, not something to operate.
      onError={() => setFailed(true)}
      aria-hidden="true"
    />
  )
}

function currentTheme(): 'light' | 'dark' {
  const chosen = document.documentElement.getAttribute('data-theme')
  if (chosen === 'dark' || chosen === 'light') return chosen
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}
