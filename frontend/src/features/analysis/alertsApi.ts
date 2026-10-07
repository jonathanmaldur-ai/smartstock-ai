import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { Paged } from '@/features/catalog/catalogApi'
import { apiRequest } from '@/lib/api'
import { toQuery } from '@/lib/query'
import type { AlertPriority } from './analysisApi'

export type AlertType = 'PredictedRupture' | 'SoldWithoutReplenishment' | 'SharpStockDrop' | 'StoreImbalance' | 'BrandConcentration'

/** Regras da decisão 47, em linguagem da loja. */
export const alertTypeHints: Record<AlertType, string> = {
  PredictedRupture: 'Vende 12+ por ano e o estoque acaba em menos de 7 dias',
  SoldWithoutReplenishment: 'Vende todo dia, o estoque caiu e nada foi recebido desde a análise anterior',
  SharpStockDrop: 'Estoque caiu mais da metade (10+ un.) sem ser por transferência enviada',
  StoreImbalance: 'Sobra (mais de 90 dias) numa loja e falta em outra que vende',
  BrandConcentration: 'Mais de 60% do estoque da marca numa loja que vende pouco dela',
}

export type AlertTypeCount = { type: AlertType; label: string; priority: AlertPriority; total: number; unseen: number }

export type AlertSummary = {
  analysisId: string | null
  stockDate: string | null
  previousStockDate: string | null
  types: AlertTypeCount[]
}

export type StockAlert = {
  id: number
  type: AlertType
  typeLabel: string
  priority: AlertPriority
  productId: number | null
  productCode: string | null
  productDescription: string | null
  storeCode: string | null
  storeName: string | null
  brandName: string | null
  message: string
  seenAt: string | null
  seenByEmail: string | null
}

export type AlertFilters = {
  type?: AlertType
  priority?: AlertPriority
  storeId?: number
  search: string
  includeSeen: boolean
  page: number
}

export const ALERT_PAGE_SIZE = 25

export const useAlertSummary = () =>
  useQuery({ queryKey: ['analysis', 'alerts', 'summary'], queryFn: () => apiRequest<AlertSummary>('/alerts/summary') })

export const useAlerts = (filters: AlertFilters) =>
  useQuery({
    queryKey: ['analysis', 'alerts', 'list', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<StockAlert>>(`/alerts?${toQuery({ ...filters, pageSize: ALERT_PAGE_SIZE })}`),
  })

export const useMarkAlertsSeen = () => {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (input: { ids: number[]; seen: boolean }) =>
      apiRequest<{ decided: number }>('/alerts/seen', { method: 'POST', body: input }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['analysis', 'alerts'] }),
  })
}
