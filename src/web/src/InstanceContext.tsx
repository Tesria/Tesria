import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, DEFAULT_BRANDING, type InstanceInfo } from './api/client'

type InstanceState = { info: InstanceInfo | undefined; reload: () => Promise<void> }

/** Undefined info while the first fetch is in flight. */
const InstanceContext = createContext<InstanceState>({ info: undefined, reload: async () => {} })

/**
 * What the app knows about the instance before anyone signs in (dev-plan 5.5).
 *
 * Fetched once, beside the session check, because the very first screen
 * depends on it: whether an anonymous visitor sees the public shell or the
 * sign-in page, and whether the sign-in page offers to create an account.
 * It also carries the branding (dev-plan 13.1), and `reload` is how the
 * Branding tab makes a saved change show in the header straight away.
 */
export function InstanceProvider({ children }: { children: ReactNode }) {
  const [info, setInfo] = useState<InstanceInfo | undefined>(undefined)

  const reload = useCallback(async () => {
    try {
      setInfo(await api.instance())
    } catch {
      // Never strand the app on a failed fetch. The safe assumption is the
      // private one: no public reading, so a visitor is asked to sign in,
      // and the page looks like Tesria.
      setInfo((current) => current ?? {
        instanceName: 'Tesria',
        needsOwner: false,
        publicReading: false,
        allowPublicRegistration: true,
        branding: DEFAULT_BRANDING,
      })
    }
  }, [])

  useEffect(() => { void reload() }, [reload])

  const value = useMemo(() => ({ info, reload }), [info, reload])
  return <InstanceContext value={value}>{children}</InstanceContext>
}

/** The instance facts, or undefined while they are still being fetched. */
export function useInstance(): InstanceInfo | undefined {
  return use(InstanceContext).info
}

/** Fetches the instance facts again, after something that changes them has been saved. */
export function useReloadInstance(): () => Promise<void> {
  return use(InstanceContext).reload
}
