import { ScrollToTop } from './components/ScrollToTop'
import { SetupGate } from './components/SetupGate'
import { TipHost } from './components/TipHost'

/**
 * The app root inside the router. ScrollToTop reads the location, so it has
 * to live under the router rather than beside it, and SetupGate has to sit
 * above every route including /login and /register (dev-plan 10.2), which
 * are outside Layout.
 */
export function Root() {
  return (
    <>
      <ScrollToTop />
      {/* Wraps the outlet: as a sibling its redirect raced SessionGate's. */}
      <SetupGate />
      <TipHost />
    </>
  )
}
