import { createContext, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { apiRequest, onSessionChange, refreshSession, setSession, type Session, type SessionUser } from '@/lib/api'

type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

type AuthContextValue = {
  status: AuthStatus
  user: SessionUser | null
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<SessionUser | null>(null)

  useEffect(() => {
    const unsubscribe = onSessionChange((session) => {
      setUser(session?.user ?? null)
      setStatus(session ? 'authenticated' : 'anonymous')
    })
    // Ao abrir a plataforma, tenta retomar a sessão pelo cookie HttpOnly.
    void refreshSession()
    return unsubscribe
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const session = await apiRequest<Session>('/auth/login', {
      method: 'POST',
      body: { email, password },
      skipAuthRetry: true,
    })
    setSession(session)
  }, [])

  const logout = useCallback(async () => {
    try {
      await apiRequest('/auth/logout', { method: 'POST', skipAuthRetry: true })
    } finally {
      setSession(null)
    }
  }, [])

  const value = useMemo(() => ({ status, user, login, logout }), [status, user, login, logout])

  return <AuthContext value={value}>{children}</AuthContext>
}
