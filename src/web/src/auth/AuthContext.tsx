import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ApiError, type User } from '../api/client'

type AuthState = {
  /** undefined while the initial session check is in flight. */
  user: User | null | undefined
  login: (email: string, password: string) => Promise<void>
  /** Resolves with the new account's recovery codes, shown once. */
  register: (
    email: string,
    displayName: string,
    password: string,
    inviteToken?: string,
  ) => Promise<string[]>
  logout: () => Promise<void>
  /** Re-reads the session — after editing your own profile, so the topbar and
   *  anything else reading `user` pick the change up without a reload. */
  refresh: () => Promise<void>
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
    setUser(await api.auth.login(email, password))
  }, [])

  const register = useCallback(async (
    email: string, displayName: string, password: string, inviteToken?: string,
  ) => {
    const registered = await api.auth.register(email, displayName, password, inviteToken)
    setUser(registered)
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

  const value = useMemo<AuthState>(
    () => ({ user, login, register, logout, refresh }),
    [user, login, register, logout, refresh],
  )
  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = use(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
