import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { Tone } from '@/components/ui/Feedback'
import type { Paged } from '@/features/catalog/catalogApi'
import { apiRequest, type Role } from '@/lib/api'
import { toQuery } from '@/lib/query'

export type StockSituation =
  | 'Rupture'
  | 'CriticalCoverage'
  | 'BelowMinimum'
  | 'Normal'
  | 'Excess'
  | 'Stagnant'
  | 'NoMovement'
  | 'Warehouse'
export type AlertPriority = 'None' | 'Low' | 'Medium' | 'High' | 'Critical'
export type SuggestionStatus = 'Suggested' | 'Approved' | 'Rejected' | 'Completed' | 'Superseded'

/** Gerar a análise: Administrador, Gerente e Operador. Aprovar/rejeitar: Administrador e Gerente (PRD). */
export const analyzeRoles: readonly Role[] = ['Administrador', 'Gerente', 'Operador']
export const approveRoles: readonly Role[] = ['Administrador', 'Gerente']

export const situationInfo: Record<StockSituation, { label: string; tone: Tone; description: string }> = {
  Rupture: { label: 'Ruptura', tone: 'error', description: 'Vende e está sem estoque' },
  CriticalCoverage: { label: 'Cobertura crítica', tone: 'error', description: 'Estoque para menos de 7 dias' },
  BelowMinimum: { label: 'Abaixo do mínimo', tone: 'warning', description: 'Estoque para menos de 15 dias' },
  Normal: { label: 'Normal', tone: 'success', description: 'Entre o mínimo e o excesso' },
  Excess: { label: 'Excesso', tone: 'info', description: 'Estoque para mais de 120 dias' },
  Stagnant: { label: 'Parado', tone: 'neutral', description: 'Tem estoque e não vendeu em 12 meses' },
  NoMovement: { label: 'Sem movimento', tone: 'neutral', description: 'Sem venda e sem estoque positivo' },
  Warehouse: { label: 'Depósito', tone: 'neutral', description: 'Não vende, abastece a rede' },
}

export const priorityInfo: Record<AlertPriority, { label: string; tone: Tone }> = {
  Critical: { label: 'Crítica', tone: 'error' },
  High: { label: 'Alta', tone: 'warning' },
  Medium: { label: 'Média', tone: 'info' },
  Low: { label: 'Baixa', tone: 'neutral' },
  None: { label: '—', tone: 'neutral' },
}

export const statusInfo: Record<SuggestionStatus, { label: string; tone: Tone }> = {
  Suggested: { label: 'Pendente', tone: 'warning' },
  Approved: { label: 'Aprovada', tone: 'success' },
  Rejected: { label: 'Rejeitada', tone: 'neutral' },
  Completed: { label: 'Realizada', tone: 'success' },
  Superseded: { label: 'Substituída', tone: 'neutral' },
}

export type AnalysisSummary = {
  analysisId: string | null
  stockDate: string | null
  analysisDate: string | null
  createdAt: string | null
  createdByEmail: string | null
  isOutdated: boolean
  situations: { situation: StockSituation; count: number }[]
  /** Rupturas de produto que vende pelo menos a venda mínima (decisão 35). */
  relevantRuptures: number
  suggestions: { pending: number; pendingUnits: number; approved: number; approvedUnits: number; rejected: number; completed: number }
  purchaseCount: number
  purchaseUnits: number
}

export type StockParameters = {
  criticalCoverageDays: number
  minimumDays: number
  idealDays: number
  maximumDays: number
  excessDays: number
  minimumAnnualSales: number
  /** Resumo dos alertas por e-mail aos Administradores e Gerentes após cada análise (decisão 47). */
  sendAlertEmail: boolean
  updatedAt: string
  updatedByEmail: string | null
}

export type Position = {
  productId: number
  productCode: string
  productDescription: string
  brandName: string
  categoryName: string
  storeCode: string
  storeName: string
  stock: number
  projectedStock: number
  sold12Months: number
  dailyAverage: number
  coverageDays: number | null
  situation: StockSituation
  priority: AlertPriority
}

export type Suggestion = {
  id: number
  productId: number
  productCode: string
  productDescription: string
  productReference: string | null
  brandName: string
  categoryName: string
  originCode: string
  originName: string
  destinationCode: string
  destinationName: string
  quantity: number
  priority: AlertPriority
  status: SuggestionStatus
  reason: string
  originStock: number
  originDailyAverage: number
  originCoverageDays: number | null
  destinationStock: number
  destinationDailyAverage: number
  destinationCoverageDays: number | null
  destinationCoverageAfter: number | null
  destinationNegative: boolean
  analysisDate: string
  decidedAt: string | null
  decidedByEmail: string | null
  decisionNote: string | null
  /** Saída encontrada no arquivo de transferências (decisão 9). */
  completedOn: string | null
  completedQuantity: number | null
}

export type SuggestionRoute = {
  originStoreId: number
  originCode: string
  originName: string
  destinationStoreId: number
  destinationCode: string
  destinationName: string
  count: number
  units: number
  criticalCount: number
  negativeCount: number
  /** Os produtos mais urgentes da rota (prévia). */
  topProducts: { description: string; quantity: number }[]
}

export type Purchase = {
  productId: number
  productCode: string
  productDescription: string
  brandName: string
  categoryName: string
  storeCode: string
  storeName: string
  quantity: number
  stock: number
  dailyAverage: number
  coverageDays: number | null
}

export type PositionFilters = { situation?: StockSituation; storeId?: number; categoryId?: number; search: string; page: number }
export type SuggestionFilters = {
  status?: SuggestionStatus
  originStoreId?: number
  destinationStoreId?: number
  priority?: AlertPriority
  categoryId?: number
  search: string
  page: number
  /** Decisão 36: destino com estoque negativo fica oculto por padrão. */
  hideNegativeDestination?: boolean
}
export type PurchaseFilters = { storeId?: number; categoryId?: number; search: string; page: number }

export const PAGE_SIZE = 25

export const useAnalysisSummary = () =>
  useQuery({ queryKey: ['analysis', 'summary'], queryFn: () => apiRequest<AnalysisSummary>('/analysis') })

export const usePositions = (filters: PositionFilters) =>
  useQuery({
    queryKey: ['analysis', 'positions', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<Position>>(`/analysis/positions?${toQuery({ ...filters, pageSize: PAGE_SIZE })}`),
  })

export const useSuggestions = (filters: SuggestionFilters) =>
  useQuery({
    queryKey: ['analysis', 'suggestions', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<Suggestion>>(`/transfer-suggestions?${toQuery({ ...filters, pageSize: PAGE_SIZE })}`),
  })

/** Sugestões agrupadas por rota (decisão 36). Paginação não se aplica. */
export const useSuggestionRoutes = (filters: Omit<SuggestionFilters, 'page' | 'originStoreId' | 'destinationStoreId'>) =>
  useQuery({
    queryKey: ['analysis', 'routes', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<SuggestionRoute[]>(`/transfer-suggestions/routes?${toQuery(filters)}`),
  })

export const usePurchases = (filters: PurchaseFilters) =>
  useQuery({
    queryKey: ['analysis', 'purchases', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<Purchase>>(`/analysis/purchases?${toQuery({ ...filters, pageSize: PAGE_SIZE })}`),
  })

export const useParameters = () =>
  useQuery({ queryKey: ['analysis', 'parameters'], queryFn: () => apiRequest<StockParameters>('/analysis/parameters') })

/** Tudo da análise muda junto: resumo, listas e parâmetros. */
function useAnalysisMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => queryClient.invalidateQueries({ queryKey: ['analysis'] }) })
}

export const useRunAnalysis = () =>
  useAnalysisMutation(() => apiRequest<AnalysisSummary>('/analysis', { method: 'POST' }))

/** Por ids, ou a rota inteira (origem + destino) quando não há ids. */
export type DecisionInput = {
  ids?: number[]
  approve: boolean
  note?: string
  originStoreId?: number
  destinationStoreId?: number
  hideNegativeDestination?: boolean
}

export const useDecideSuggestions = () =>
  useAnalysisMutation((body: DecisionInput) =>
    apiRequest<{ decided: number }>('/transfer-suggestions/decision', { method: 'POST', body }),
  )

export type ParametersInput = Omit<StockParameters, 'updatedAt' | 'updatedByEmail'>

export const useUpdateParameters = () =>
  useAnalysisMutation((body: ParametersInput) => apiRequest<StockParameters>('/analysis/parameters', { method: 'PUT', body }))
