import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { Paged } from '@/features/catalog/catalogApi'
import { apiRequest } from '@/lib/api'
import { toQuery } from '@/lib/query'

/** Épocas de venda (decisão 65). */
export type Season = {
  id: number
  name: string
  startMonth: number
  endMonth: number
  worldCupOnly: boolean
  keywords: string[]
  /** Procura as palavras-chave também na descrição (desligado em Volta às aulas). */
  matchDescription: boolean
  isActive: boolean
  /** A época vale hoje. */
  inSeasonNow: boolean
  productCount: number
  pendingSuggestions: number
}

export type SeasonInput = Pick<Season, 'name' | 'startMonth' | 'endMonth' | 'worldCupOnly' | 'keywords' | 'matchDescription' | 'isActive'>

export type SuggestionKind = 'Keyword' | 'SalesStopped'

export type SeasonalSuggestion = {
  id: number
  productId: number
  code: string
  description: string
  brand: string
  category: string
  seasonId: number | null
  seasonName: string | null
  kind: SuggestionKind
  evidence: string
  /** Estoque positivo na rede, na última análise. */
  stock: number
  sold12Months: number
  updatedAt: string
}

export type SeasonalProduct = {
  productId: number
  code: string
  description: string
  brand: string
  category: string
  seasonId: number | null
  seasonName: string | null
  markedOutOfSeason: boolean
  state: 'InSeason' | 'OutOfSeason' | 'None'
  stock: number
  sold12Months: number
}

/** Filtro de época: uma época, "sem época" (a venda parou, sem palavra-chave) ou todas. */
export type SeasonFilter = { seasonId?: number; withoutSeason?: boolean }

export const kindLabels: Record<SuggestionKind, string> = { Keyword: 'Palavra-chave', SalesStopped: 'Venda parou' }

export const monthNames = ['janeiro', 'fevereiro', 'março', 'abril', 'maio', 'junho', 'julho', 'agosto', 'setembro', 'outubro', 'novembro', 'dezembro']

export const periodLabel = (season: Pick<Season, 'startMonth' | 'endMonth' | 'worldCupOnly'>) =>
  `${season.startMonth === season.endMonth ? monthNames[season.startMonth - 1] : `${monthNames[season.startMonth - 1]} a ${monthNames[season.endMonth - 1]}`}${
    season.worldCupOnly ? ' (ano de Copa)' : ''
  }`

export const useSeasons = () => useQuery({ queryKey: ['seasonality', 'seasons'], queryFn: () => apiRequest<Season[]>('/seasonality/seasons') })

export const useSeasonalSuggestions = (filters: SeasonFilter & { search?: string; page: number }) =>
  useQuery({
    queryKey: ['seasonality', 'suggestions', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<SeasonalSuggestion>>(`/seasonality/suggestions?${toQuery({ ...filters, pageSize: 50 })}`),
  })

export const useSeasonalProducts = (filters: { seasonId?: number; outOfSeasonOnly?: boolean; search?: string; page: number }) =>
  useQuery({
    queryKey: ['seasonality', 'products', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<SeasonalProduct>>(`/seasonality/products?${toQuery({ ...filters, pageSize: 50 })}`),
  })

function useSeasonalityMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ['seasonality'] }),
        queryClient.invalidateQueries({ queryKey: ['products'] }),
        queryClient.invalidateQueries({ queryKey: ['product-overview'] }),
      ]),
  })
}

/** Sem ids: decide todas as pendentes do filtro de época. */
export type SeasonalDecision = SeasonFilter & { ids: number[]; confirm: boolean }

export const useDecideSeasonal = () =>
  useSeasonalityMutation((decision: SeasonalDecision) =>
    apiRequest<{ decided: number }>('/seasonality/suggestions/decision', { method: 'POST', body: decision }),
  )

export const useSetProductSeason = () =>
  useSeasonalityMutation(({ productId, seasonId, outOfSeason }: { productId: number; seasonId: number | null; outOfSeason: boolean }) =>
    apiRequest<SeasonalProduct>(`/seasonality/products/${productId}`, { method: 'PUT', body: { seasonId, outOfSeason } }),
  )

export const useSaveSeason = () =>
  useSeasonalityMutation(({ id, ...input }: SeasonInput & { id?: number }) =>
    apiRequest<Season>(id ? `/seasonality/seasons/${id}` : '/seasonality/seasons', { method: id ? 'PUT' : 'POST', body: input }),
  )
