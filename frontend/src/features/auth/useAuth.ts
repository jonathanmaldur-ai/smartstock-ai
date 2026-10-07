import { use } from 'react'
import type { Role } from '@/lib/api'
import { AuthContext } from './AuthProvider'

export function useAuth() {
  const context = use(AuthContext)
  if (!context) throw new Error('useAuth precisa estar dentro de <AuthProvider>.')
  return context
}

export function hasRole(role: Role | undefined, allowed: readonly Role[]) {
  return role !== undefined && allowed.includes(role)
}
