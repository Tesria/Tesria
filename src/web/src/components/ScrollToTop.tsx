import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'

/** React Router (outside the data-router APIs, which is what <BrowserRouter>
 *  is) never resets scroll position on navigation: the browser just keeps
 *  whatever scrollY the previous page had. Landing on a shorter page already
 *  scrolled past its own content hides everything, sticky topbar included,
 *  until the user manually scrolls back up. Most browsers don't clamp this
 *  visibly on pushState the way they do on a real page load, which is why
 *  this didn't show up until testing on a real phone. */
export function ScrollToTop() {
  const { pathname, hash } = useLocation()
  useEffect(() => {
    // A link to a section (/profile#two-factor) scrolls to it instead. The
    // section may only render once its data arrives, so it is looked for
    // for a couple of seconds before giving up (2026-09-23).
    window.scrollTo(0, 0)
    if (!hash) return
    const id = decodeURIComponent(hash.slice(1))
    let tries = 0
    const timer = window.setInterval(() => {
      const target = document.getElementById(id)
      if (target) target.scrollIntoView()
      if (target || ++tries > 20) window.clearInterval(timer)
    }, 100)
    return () => window.clearInterval(timer)
  }, [pathname, hash])
  return null
}
