import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'

export type AuditResult = 'Success' | 'Failure'

export type AuditLog = {
  id: number
  occurredAt: string
  userEmail: string | null
  action: string
  entityType: string | null
  entityId: string | null
  result: AuditResult
  details: string | null
  ipAddress: string | null
}

export type AuditFilters = {
  page: number
  action?: string
  userEmail?: string
  result?: AuditResult
  from?: string
  to?: string
}

type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

export const PAGE_SIZE = 25

/** Nomes em português dos códigos gravados pelo backend (AuditActions). */
export const actionLabels: Record<string, string> = {
  'auth.login.succeeded': 'Login realizado',
  'auth.login.failed': 'Falha de login',
  'auth.logout': 'Logout',
  'auth.password.reset_requested': 'Pedido de recuperação de senha',
  'auth.password.defined': 'Senha definida',
  'auth.password.define_failed': 'Falha ao definir senha',
  'user.created': 'Usuário criado',
  'user.updated': 'Usuário alterado',
  'user.deactivated': 'Usuário desativado',
  'user.activated': 'Usuário reativado',
  'user.invite_resent': 'Convite reenviado',
  'system.admin_seeded': 'Administrador inicial criado',
  'store.updated': 'Loja alterada',
  'category.exclusion_changed': 'Categoria incluída/excluída das análises',
  'category.merged': 'Categorias unificadas',
  'import.uploaded': 'Arquivo enviado para validação',
  'import.rejected': 'Arquivo recusado',
  'import.confirmed': 'Importação gravada',
  'import.discarded': 'Importação descartada',
}

export const actionLabel = (action: string) => actionLabels[action] ?? action

export function useAuditLogs(filters: AuditFilters) {
  return useQuery({
    queryKey: ['audit', filters],
    placeholderData: keepPreviousData,
    queryFn: () => {
      const params = new URLSearchParams({ page: String(filters.page), pageSize: String(PAGE_SIZE) })
      if (filters.action) params.set('action', filters.action)
      if (filters.userEmail) params.set('userEmail', filters.userEmail)
      if (filters.result) params.set('result', filters.result)
      // Datas do filtro são dias inteiros no horário local.
      if (filters.from) params.set('from', new Date(`${filters.from}T00:00:00`).toISOString())
      if (filters.to) params.set('to', new Date(`${filters.to}T23:59:59.999`).toISOString())
      return apiRequest<Paged<AuditLog>>(`/audit?${params}`)
    },
  })
}

export function useAuditActions() {
  return useQuery({ queryKey: ['audit', 'actions'], queryFn: () => apiRequest<string[]>('/audit/actions') })
}
