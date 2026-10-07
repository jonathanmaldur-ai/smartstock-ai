import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'
import { toQuery } from '@/lib/query'

export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

export type StoreType = 'Store' | 'Warehouse' | 'Ecommerce'
export type Store = { id: number; code: string; name: string; city: string | null; type: StoreType; status: 'Active' | 'Closed' }
export type Brand = { id: number; code: string; name: string; isActive: boolean; productCount: number }
export type Category = { id: number; name: string; excludedFromAnalysis: boolean; productCount: number; aliases: string[] }
export type Product = {
  id: number
  code: string
  description: string
  unit: string | null
  reference: string | null
  isActive: boolean
  brandCode: string
  brandName: string
  categoryName: string
  categoryExcluded: boolean
  subcategoryName: string | null
  /** Fora de época: sai das sugestões (decisão 37). */
  isSeasonal: boolean
}

export const PAGE_SIZE = 25

export const storeTypeLabels: Record<StoreType, string> = { Store: 'Loja', Warehouse: 'Depósito', Ecommerce: 'E-commerce' }

export const useStores = () => useQuery({ queryKey: ['stores'], queryFn: () => apiRequest<Store[]>('/stores') })

export const useCategories = () => useQuery({ queryKey: ['categories'], queryFn: () => apiRequest<Category[]>('/categories') })

export function useBrands(search: string, page: number) {
  return useQuery({
    queryKey: ['brands', search, page],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<Brand>>(`/brands?${toQuery({ search, page, pageSize: PAGE_SIZE })}`),
  })
}

export type ProductFilters = { search: string; categoryId?: number; isActive?: boolean; page: number }

export function useProducts(filters: ProductFilters) {
  return useQuery({
    queryKey: ['products', filters],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<Product>>(`/products?${toQuery({ ...filters, pageSize: PAGE_SIZE })}`),
  })
}

export type StoreInventory = {
  storeCode: string
  storeName: string
  storeType: StoreType
  /** null: nenhum estoque importado ainda. */
  stock: number | null
  /** null: vendas da loja ainda não importadas. */
  sold12Months: number | null
  salesPeriodEnd: string | null
  dailyAverage: number | null
  /** null: a loja não vende o produto. */
  coverageDays: number | null
}

export type TransferMovement = {
  date: string
  isCancellation: boolean
  originCode: string
  destinationCode: string
  direction: 'In' | 'Out'
  quantity: number
  userName: string | null
}

export type ProductOverview = {
  product: Product
  stockDate: string | null
  stores: StoreInventory[]
  recentTransfers: TransferMovement[]
}

/** Ficha do produto (decisão 34): estoque, vendas, VMD e cobertura por loja e últimas transferências. */
export const useProductOverview = (productId: number | null) =>
  useQuery({
    queryKey: ['product-overview', productId],
    enabled: productId !== null,
    queryFn: () => apiRequest<ProductOverview>(`/products/${productId}/overview`),
  })

function useCatalogMutation<TVariables, TResult>(keys: string[][], mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => Promise.all(keys.map((queryKey) => queryClient.invalidateQueries({ queryKey }))),
  })
}

/** Marca o produto como sazonal / fora de época (só Administrador). */
export const useSetProductSeasonal = () =>
  useCatalogMutation([['products'], ['product-overview'], ['analysis']], ({ id, seasonal }: { id: number; seasonal: boolean }) =>
    apiRequest<Product>(`/products/${id}/seasonal`, { method: 'PUT', body: { seasonal } }),
  )

export const useUpdateStore = () =>
  useCatalogMutation([['stores']], ({ id, name, city }: { id: number; name: string; city: string | null }) =>
    apiRequest<Store>(`/stores/${id}`, { method: 'PUT', body: { name, city } }),
  )

export const useSetCategoryExcluded = () =>
  useCatalogMutation([['categories'], ['products']], ({ id, excluded }: { id: number; excluded: boolean }) =>
    apiRequest<Category>(`/categories/${id}/exclusion`, { method: 'PUT', body: { excluded } }),
  )

export const useMergeCategory = () =>
  useCatalogMutation([['categories'], ['products']], ({ sourceId, targetId }: { sourceId: number; targetId: number }) =>
    apiRequest<Category>(`/categories/${sourceId}/merge`, { method: 'POST', body: { targetId } }),
  )
