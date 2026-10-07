import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'
import { toQuery } from '@/lib/query'
import type { Paged } from '@/features/catalog/catalogApi'

export type ReportInfo = { key: string; title: string; description: string; group: string }
export type ReportCatalog = { reports: ReportInfo[]; brands: { id: number; name: string }[] }

export type ReportColumnKind = 'Text' | 'Integer' | 'Decimal' | 'Date'
export type ReportCell = string | number | null

export type ReportData = {
  key: string
  title: string
  description: string
  filterSummary: string
  analysisDate: string | null
  stockDate: string | null
  columns: { header: string; kind: ReportColumnKind }[]
  rows: ReportCell[][]
  totalRows: number
}

export type ReportFilters = { storeId?: number; brandId?: number; categoryId?: number }

export const useReportCatalog = () =>
  useQuery({ queryKey: ['reports', 'catalog'], queryFn: () => apiRequest<ReportCatalog>('/reports') })

export const useReport = (key: string | null, filters: ReportFilters) =>
  useQuery({
    queryKey: ['reports', key, filters],
    enabled: key !== null,
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<ReportData>(`/reports/${key}?${toQuery(filters)}`),
  })

export const reportQuery = (filters: ReportFilters) => toQuery(filters)

export type AnalysisRef = { id: string; analysisDate: string; stockDate: string; createdAt: string }
export type StoreMetrics = {
  relevantRuptures: number
  negativeItems: number
  excess: number
  stagnant: number
  stockUnits: number
  coverageDays: number | null
}
export type MonthlyReport = {
  current: AnalysisRef
  previous: AnalysisRef | null
  options: AnalysisRef[]
  indicators: { name: string; current: number; previous: number | null; lowerIsBetter: boolean | null; unit: string }[]
  stores: { code: string; name: string; current: StoreMetrics; previous: StoreMetrics | null }[]
  transfers: {
    since: string | null
    approved: number
    approvedUnits: number
    completed: number
    completedUnits: number
    rejected: number
    pendingApproval: number
    precisionPercent: number | null
    averageDaysToComplete: number | null
  }
}

export type DailySalesTotals = {
  net: number
  gross: number
  sales: number
  pieces: number
  days: number
  averageTicket: number | null
  piecesPerSale: number | null
  netPerDay: number | null
}

export type DailySalesReport = {
  from: string | null
  to: string | null
  previousFrom: string | null
  previousTo: string | null
  firstAvailable: string | null
  lastAvailable: string | null
  scope: string
  current: DailySalesTotals
  previous: DailySalesTotals | null
  days: { date: string; net: number; sales: number; pieces: number }[]
  stores: {
    storeId: number
    code: string
    name: string
    net: number
    sales: number
    pieces: number
    days: number
    averageTicket: number | null
    previousNet: number | null
    bestDay: string | null
    bestDayNet: number | null
  }[]
  weekdays: { dayOfWeek: number; label: string; averageNet: number; averageSales: number; days: number }[]
  /** Produtos vendidos no período (relatório detalhado por itens): os 100 que mais venderam em valor. */
  products: {
    available: boolean
    total: number
    items: { productId: number; code: string; description: string; brand: string; quantity: number; amount: number; days: number; stores: number }[]
  }
}

/** Vendas por dia (decisão 51). Sem período: os últimos 30 dias com venda importada. */
export const DAILY_KEY = 'vendas-diarias'

export type DailySalesFilters = { de?: string; ate?: string; storeId?: number }

export const useDailySales = (filters: DailySalesFilters) =>
  useQuery({
    queryKey: ['reports', DAILY_KEY, filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<DailySalesReport>(`/reports/${DAILY_KEY}?${toQuery(filters)}`),
  })

/** Relatório mensal (Fase 4.2): não tem filtros; escolhe-se só a análise anterior para comparar. */
export const MONTHLY_KEY = 'mensal'

export const monthlyQuery = (compareWith: string | null) => (compareWith ? `comparar=${encodeURIComponent(compareWith)}` : '')

export const useMonthlyReport = (enabled: boolean, compareWith: string | null) =>
  useQuery({
    queryKey: ['reports', MONTHLY_KEY, compareWith],
    enabled,
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<MonthlyReport>(`/reports/${MONTHLY_KEY}?${monthlyQuery(compareWith)}`),
  })

export type ProductDay = {
  date: string
  storeId: number
  storeCode: string
  storeName: string
  productId: number
  code: string
  description: string
  brand: string
  quantity: number
  amount: number
  /** Em quantas vendas (cupons) o produto saiu. */
  sales: number
}

/** Produtos vendidos em cada dia e loja (decisão 52), do dia mais recente para o mais antigo. */
export const useProductDays = (filters: DailySalesFilters & { search?: string; page: number }) =>
  useQuery({
    queryKey: ['reports', DAILY_KEY, 'produtos', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<ProductDay>>(`/reports/${DAILY_KEY}/produtos?${toQuery({ ...filters, pageSize: 50 })}`),
  })

export type SaleLine = {
  date: string
  storeCode: string
  storeName: string
  saleNumber: string
  quantity: number
  unitPrice: number
  amount: number
  saleTotal: number
  saleItems: number
  registeredCustomer: boolean
  payment: string | null
  fiscalDocument: string | null
}

/** As vendas em que um produto saiu numa loja e num dia (decisão 53). */
export const useSaleLines = (key: { productId: number; de: string; ate: string; storeId?: number } | null) =>
  useQuery({
    queryKey: ['reports', DAILY_KEY, 'vendas', key],
    enabled: key !== null,
    queryFn: () => apiRequest<SaleLine[]>(`/reports/${DAILY_KEY}/produtos/vendas?${toQuery(key!)}`),
  })
