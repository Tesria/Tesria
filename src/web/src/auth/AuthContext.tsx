import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ApiError, type User } from '../api/client'

type AuthState = {
  /** undefined while the initial session check is in flight. */
  user: User | null | undefined
  login: (email: string, password: string) => Promise<void>
  register: (email: string, displayName: string, password: string) => Promise<void>
  logout: () => Promise<void>
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

  const register = useCallback(async (email: string, displayName: string, password: string) => {
    setUser(await api.auth.register(email, displayName, password))
  }, [])

  const logout = useCallback(async () => {
    await api.auth.logout()
    setUser(null)
  }, [])

  const value = useMemo<AuthState>(
    () => ({ user, login, register, logout }),
    [user, login, register, logout],
  )
  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = use(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
