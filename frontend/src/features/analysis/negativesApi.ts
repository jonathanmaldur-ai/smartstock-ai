import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { Paged } from '@/features/catalog/catalogApi'
import { apiRequest } from '@/lib/api'
import { toQuery } from '@/lib/query'
import type { AlertPriority } from './analysisApi'

export type NegativeCause = 'TransferNotReceived' | 'SaleWithoutEntry' | 'ShippedWithoutEntry' | 'FractionalUnit' | 'PossibleDuplicate'

/** Causas prováveis da decisão 38: hipóteses para investigar, não conclusões. */
export const causeInfo: Record<NegativeCause, { label: string; hint: string }> = {
  TransferNotReceived: { label: 'Transferência não recebida', hint: 'Foi enviada para a loja e a entrada não foi lançada' },
  SaleWithoutEntry: { label: 'Vende sem entrada', hint: 'A loja vende o produto e nunca recebeu por transferência: falta entrada de nota' },
  ShippedWithoutEntry: { label: 'Saiu sem entrada de nota', hint: 'Enviou por transferência sem ter entrada registrada (típico do Depósito)' },
  FractionalUnit: { label: 'Unidade fracionada', hint: 'Quantidade quebrada ou unidade em metro/quilo/litro' },
  PossibleDuplicate: { label: 'Possível duplicado', hint: 'Outro código da mesma marca e referência tem estoque positivo na loja' },
}

export const negativeCauses = Object.keys(causeInfo) as NegativeCause[]

export type NegativeStoreRow = {
  storeId: number
  code: string
  name: string
  items: number
  units: number
  criticalItems: number
  percentOfItems: number
  previousItems: number | null
  previousUnits: number | null
}

export type NegativeSummary = {
  analysisId: string | null
  stockDate: string | null
  previousStockDate: string | null
  items: number
  units: number
  criticalItems: number
  stores: NegativeStoreRow[]
  causes: { cause: NegativeCause; items: number }[]
}

export type NegativeGroup = { id: number; name: string; items: number; units: number }

export type NegativeItem = {
  productId: number
  productCode: string
  productDescription: string
  productReference: string | null
  brandName: string
  categoryName: string
  storeCode: string
  storeName: string
  quantity: number
  sold12Months: number
  priority: AlertPriority
  causes: NegativeCause[]
  pendingTransferUnits: number
  duplicateProductCode: string | null
}

export type NegativeFilters = {
  storeId?: number
  priority?: AlertPriority
  cause?: NegativeCause
  categoryId?: number
  search: string
  page: number
}

export const NEGATIVE_PAGE_SIZE = 25

export const useNegativeSummary = () =>
  useQuery({ queryKey: ['analysis', 'negatives', 'summary'], queryFn: () => apiRequest<NegativeSummary>('/analysis/negatives/summary') })

export const useNegativeRanking = (by: 'Brand' | 'Category', storeId?: number) =>
  useQuery({
    queryKey: ['analysis', 'negatives', 'ranking', by, storeId],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<NegativeGroup[]>(`/analysis/negatives/ranking?${toQuery({ by, storeId })}`),
  })

export const useNegatives = (filters: NegativeFilters) =>
  useQuery({
    queryKey: ['analysis', 'negatives', 'list', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<NegativeItem>>(`/analysis/negatives?${toQuery({ ...filters, pageSize: NEGATIVE_PAGE_SIZE })}`),
  })
