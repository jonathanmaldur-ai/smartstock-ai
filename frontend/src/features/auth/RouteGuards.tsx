import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { FullPageSpinner } from '@/components/ui/Spinner'
import type { Role } from '@/lib/api'
import { hasRole, useAuth } from './useAuth'

/** Só deixa passar usuários logados; os demais vão para o login e voltam depois. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <FullPageSpinner label="Carregando sua sessão" />
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return children
}

/** Restringe a página a alguns perfis. A API também valida: isto só evita mostrar telas inúteis. */
export function RequireRole({ roles, children }: { roles: readonly Role[]; children: ReactNode }) {
  const { user } = useAuth()
  if (!hasRole(user?.role, roles)) return <Navigate to="/" replace />
  return children
}

/** Páginas públicas (login etc.): quem já está logado vai direto para o Dashboard. */
export function RedirectIfAuthenticated({ children }: { children: ReactNode }) {
  const { status } = useAuth()
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  if (status === 'loading') return <FullPageSpinner label="Carregando" />
  if (status === 'authenticated') return <Navigate to={from} replace />
  return children
}
