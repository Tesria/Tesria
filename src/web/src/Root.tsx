import { Outlet } from 'react-router-dom'
import { ScrollToTop } from './components/ScrollToTop'

/**
 * The app root inside the router. ScrollToTop reads the location, so it has
 * to live under the router rather than beside it.
 */
export function Root() {
  return (
    <>
      <ScrollToTop />
      <Outlet />
    </>
  )
}
