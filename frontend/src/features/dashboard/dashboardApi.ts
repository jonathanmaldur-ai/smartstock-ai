import { useQuery } from '@tanstack/react-query'
import type { StoreType } from '@/features/catalog/catalogApi'
import { apiRequest } from '@/lib/api'

export type DataFreshness = {
  stockDate: string | null
  stockAgeDays: number | null
  stockOutdated: boolean
  salesDate: string | null
  salesAgeDays: number | null
  salesOutdated: boolean
  transfersDate: string | null
  transfersAgeDays: number | null
  transfersOutdated: boolean
}

export type DashboardKpis = {
  analysisDate: string
  stockDate: string
  stockUnits: number
  sold12Months: number
  annualTurnover: number | null
  networkCoverageDays: number | null
  relevantRuptures: number
  pendingSuggestions: number
  pendingUnits: number
  negativeItems: number
}

export type StoreDashboardRow = {
  storeId: number
  code: string
  name: string
  type: StoreType
  stockUnits: number
  sold12Months: number
  coverageDays: number | null
  rupture: number
  belowMinimum: number
  normal: number
  excess: number
  stagnant: number
}

export type TopProduct = {
  productId: number
  code: string
  description: string
  brandName: string
  sold12Months: number
  stockUnits: number
  ruptureStores: number
}

export type TrendPoint = { analysisDate: string; stockDate: string; relevantRuptures: number; negativeItems: number; suggestions: number }

export type Dashboard = {
  freshness: DataFreshness
  kpis: DashboardKpis | null
  stores: StoreDashboardRow[]
  topProducts: TopProduct[]
  trend: TrendPoint[]
}

export const useDashboard = () =>
  useQuery({ queryKey: ['analysis', 'dashboard'], queryFn: () => apiRequest<Dashboard>('/dashboard') })

/** Dos itens que vendem na loja, quantos % estão em ruptura ou abaixo do mínimo. */
export function shortagePercent(store: StoreDashboardRow) {
  const selling = store.rupture + store.belowMinimum + store.normal + store.excess
  return selling === 0 ? 0 : (100 * (store.rupture + store.belowMinimum)) / selling
}
