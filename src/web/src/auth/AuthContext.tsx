import { createContext, use, useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { api, ApiError, type TotpChallenge, type User } from '../api/client'
import { setPreferredCodeScheme } from '../editor/codeSchemes'
import { SESSION_CHECK_EVENT } from './sessionCheck'

type AuthState = {
  /** undefined while the initial session check is in flight. */
  user: User | null | undefined
  /** Resolves with a challenge when a one-time code is still needed. */
  login: (email: string, password: string) => Promise<TotpChallenge | null>
  completeTotp: (challenge: string, code: string) => Promise<void>
  /**
   * Resolves with the new account's recovery codes, shown once. `onCodes`
   * receives them *before* the session is set: setting the session first let
   * the tour gate and the register page's own guard redirect away before the
   * codes were ever drawn (found 2026-09-22, three accounts out of four).
   */
  register: (
    email: string,
    displayName: string,
    password: string,
    inviteToken?: string,
    onCodes?: (codes: string[]) => void,
  ) => Promise<string[]>
  logout: () => Promise<void>
  /** Re-reads the session, after editing your own profile, so the topbar and
   *  anything else reading `user` pick the change up without a reload. */
  refresh: () => Promise<void>
  /** Whether the signed-in account holds an instance right (dev-plan 11.1).
   *  False while the session check is in flight and for anonymous readers. */
  can: (permission: string) => boolean
  /** When this tab found its session ended elsewhere (t2-024), or null. Set
   *  as the tab signs itself out; cleared by the next sign-in. */
  sessionEndedAt: number | null
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null | undefined>(undefined)
  const [sessionEndedAt, setSessionEndedAt] = useState<number | null>(null)
  const userRef = useRef(user)
  useEffect(() => { userRef.current = user }, [user])

  useEffect(() => {
    let canceled = false
    api.auth
      .me()
      .then((u) => !canceled && setUser(u))
      .catch((err: unknown) => {
        // 401 simply means "not signed in"; anything else we also treat as
        // logged-out for the purposes of routing.
        if (!canceled) setUser(null)
        if (!(err instanceof ApiError && err.status === 401)) console.error(err)
      })
    return () => {
      canceled = true
    }
  }, [])

  // A session ended in another tab or on another device (t2-024): the API
  // client asks for a check after a 401 or 404, and a signed-in tab whose
  // session is gone signs itself out instead of showing "Not found."
  useEffect(() => {
    let checking = false
    const check = () => {
      if (checking || !userRef.current) return
      checking = true
      api.auth
        .me()
        .then((u) => {
          if (u.id !== userRef.current?.id) setUser(u)
        })
        .catch((err: unknown) => {
          if (err instanceof ApiError && err.status === 401 && userRef.current) {
            setUser(null)
            setSessionEndedAt(Date.now())
          }
        })
        .finally(() => { checking = false })
    }
    window.addEventListener(SESSION_CHECK_EVENT, check)
    return () => window.removeEventListener(SESSION_CHECK_EVENT, check)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const result = await api.auth.login(email, password)
    if ('requiresTotp' in result) return result
    setUser(result)
    setSessionEndedAt(null)
    return null
  }, [])

  const completeTotp = useCallback(async (challenge: string, code: string) => {
    setUser(await api.auth.loginTotp(challenge, code))
    setSessionEndedAt(null)
  }, [])

  const register = useCallback(async (
    email: string, displayName: string, password: string, inviteToken?: string,
    onCodes?: (codes: string[]) => void,
  ) => {
    const registered = await api.auth.register(email, displayName, password, inviteToken)
    onCodes?.(registered.recoveryCodes)
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

  // The colors a new code block starts with, where the editor's insert
  // commands can read them (codeSchemes.ts, 2026-09-29).
  useEffect(() => { setPreferredCodeScheme(user?.codeBlockScheme) }, [user])

  const value = useMemo<AuthState>(
    () => ({ user, login, completeTotp, register, logout, refresh, can, sessionEndedAt }),
    [user, login, completeTotp, register, logout, refresh, can, sessionEndedAt],
  )
  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthState {
  const ctx = use(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
