import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ApiError, type TotpChallenge, type User } from '../api/client'

type AuthState = {
  /** undefined while the initial session check is in flight. */
  user: User | null | undefined
  /** Resolves with a challenge when a one-time code is still needed. */
  login: (email: string, password: string) => Promise<TotpChallenge | null>
  completeTotp: (challenge: string, code: string) => Promise<void>
  /** Resolves with the new account's recovery codes, shown once. */
  register: (
    email: string,
    displayName: string,
    password: string,
    inviteToken?: string,
  ) => Promise<string[]>
  logout: () => Promise<void>
  /** Re-reads the session, after editing your own profile, so the topbar and
   *  anything else reading `user` pick the change up without a reload. */
  refresh: () => Promise<void>
  /** Whether the signed-in account holds an instance right (dev-plan 11.1).
   *  False while the session check is in flight and for anonymous readers. */
  can: (permission: string) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null | undefined>(undefined)

  useEffect(() => {
    let cancelled = false
    api.auth
      .me()
      .then((u) => !cancelled && setUser(u))
      .catch((err: unknown) => {
        // 401 simply means "not signed in"; anything else we also treat as
        // logged-out for the purposes of routing.
        if (!cancelled) setUser(null)
        if (!(err instanceof ApiError && err.status === 401)) console.error(err)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const result = await api.auth.login(email, password)
    if ('requiresTotp' in result) return result
    setUser(result)
    return null
  }, [])

  const completeTotp = useCallback(async (challenge: string, code: string) => {
    setUser(await api.auth.loginTotp(challenge, code))
  }, [])

  const register = useCallback(async (
    email: string, displayName: string, password: string, inviteToken?: string,
  ) => {
    const registered = await api.auth.register(email, displayName, password, inviteToken)
    // The register response is a *partial* user: it carries the recovery codes
    // but no permissions, role name or setupRequired. Setting it as the
    // session would leave the app thinking this account may do nothing, which
    // matters most for the first-run owner (dev-plan 10.2). Read the session
    // back instead, and fall back to the partial one if that fails.
    try {
      setUser(await api.auth.me())
    } catch {
      setUser(registered)
    }
    return registered.recoveryCodes
  }, [])

  const refresh = useCallback(async () => {
    try {
      setUser(await api.auth.me())
    } catch {
      // A failed refresh should not blank an otherwise working session; the
      // next real 401 will route to /login on its own.
    }
  }, [])

  const logout = useCallback(async () => {
    await api.auth.logout()
    setUser(null)
  }, [])

  // Hiding a control the server would refuse spares people a page of 403s;
  // it is never the enforcement itself (dev-plan 11.1).
  const can = useCallback(
    (permission: string) => user?.permissions?.includes(permission) ?? false,
    [user],
  )

  const value = useMemo<AuthState>(
    () => ({ user, login, completeTotp, register, logout, refresh, can }),
    [user, login, completeTotp, register, logout, refresh, can],
  )
  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = use(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
