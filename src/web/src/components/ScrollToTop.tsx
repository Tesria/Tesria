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
  const { pathname } = useLocation()
  useEffect(() => {
    window.scrollTo(0, 0)
  }, [pathname])
  return null
}
