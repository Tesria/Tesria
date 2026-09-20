import { createContext, use, useEffect, useState, type ReactNode } from 'react'
import { api, type InstanceInfo } from './api/client'

/** Undefined while the first fetch is in flight. */
const InstanceContext = createContext<InstanceInfo | undefined>(undefined)

/**
 * What the app knows about the instance before anyone signs in (dev-plan 5.5).
 *
 * Fetched once, beside the session check, because the very first screen
 * depends on it: whether an anonymous visitor sees the public shell or the
 * sign-in page, and whether the sign-in page offers to create an account.
 */
export function InstanceProvider({ children }: { children: ReactNode }) {
  const [instance, setInstance] = useState<InstanceInfo | undefined>(undefined)

  useEffect(() => {
    let cancelled = false
    api.instance()
      .then((i) => !cancelled && setInstance(i))
      .catch(() => {
        // Never strand the app on a failed fetch. The safe assumption is the
        // private one: no public reading, so a visitor is asked to sign in.
        if (!cancelled) setInstance({
          instanceName: 'Tesria',
          needsOwner: false,
          publicReading: false,
          allowPublicRegistration: true,
        })
      })
    return () => { cancelled = true }
  }, [])

  return <InstanceContext value={instance}>{children}</InstanceContext>
}

/** The instance facts, or undefined while they are still being fetched. */
export function useInstance(): InstanceInfo | undefined {
  return use(InstanceContext)
}
