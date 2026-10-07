import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest } from '@/lib/api'
import type { Paged } from '@/features/catalog/catalogApi'

export type ImportType = 'Brands' | 'Products' | 'Stock' | 'Sales' | 'Transfers' | 'DailySales'
export type ImportStatus = 'Validated' | 'Confirmed' | 'Discarded' | 'Rejected'
export type IssueSeverity = 'Error' | 'Warning'

export type IssueGroup = { severity: IssueSeverity; code: string; message: string; count: number; sampleValues: string[] }

export type ImportReport = {
  id: string
  type: ImportType
  status: ImportStatus
  fileName: string
  uploadedAt: string
  uploadedByEmail: string
  totalRows: number
  validRows: number
  errorRows: number
  warningCount: number
  rejectionReason: string | null
  duplicateOfBatchId: string | null
  decidedAt: string | null
  decidedByEmail: string | null
  /** Data dos dados informada no envio (estoque e vendas). */
  referenceDate: string | null
  storeCode: string | null
  storeName: string | null
  periodStart: string | null
  periodEnd: string | null
  /** Registros de importações anteriores substituídos (transferências do mesmo período). */
  replacedRecords: number
  issueGroups: IssueGroup[]
}

type ImportTypeInfo = {
  value: ImportType
  label: string
  file: string
  /** O arquivo do ERP não traz data: o usuário informa (decisões 31 e 32). */
  needsDate?: boolean
  /** Vendas: um arquivo por loja, todos enviados de uma vez. */
  multiple?: boolean
}

export const importTypes: readonly ImportTypeInfo[] = [
  { value: 'Brands', label: 'Marcas', file: 'Marca.xlsx' },
  { value: 'Products', label: 'Produtos', file: 'Produtos.xlsx' },
  { value: 'Stock', label: 'Estoque', file: 'Estoque.xlsx', needsDate: true },
  { value: 'Sales', label: 'Vendas', file: 'um arquivo por loja', needsDate: true, multiple: true },
  { value: 'Transfers', label: 'Transferências', file: 'relatório de transferências' },
  { value: 'DailySales', label: 'Vendas por dia', file: 'relatório de vendas, um arquivo por loja', multiple: true },
]

export const importTypeInfo = (type: ImportType) => importTypes.find((t) => t.value === type)!

export const importTypeLabel = (type: ImportType) => importTypeInfo(type).label

export const statusLabels: Record<ImportStatus, string> = {
  Validated: 'Aguardando confirmação',
  Confirmed: 'Gravado',
  Discarded: 'Descartado',
  Rejected: 'Recusado',
}

export function useImportHistory(page: number) {
  return useQuery({
    queryKey: ['imports', page],
    placeholderData: keepPreviousData,
    queryFn: () => apiRequest<Paged<ImportReport>>(`/imports?page=${page}&pageSize=10`),
  })
}

/** Depois de gravar, os dados mudaram: atualiza tudo o que depende deles. */
function useImportMutation<TVariables>(mutationFn: (variables: TVariables) => Promise<ImportReport>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () =>
      Promise.all(
        [['imports'], ['brands'], ['products'], ['categories'], ['product-overview']].map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      ),
  })
}

export type UploadVariables = { type: ImportType; file: File; referenceDate?: string; storeCode?: string }

export const useUploadImport = () =>
  useImportMutation(({ type, file, referenceDate, storeCode }: UploadVariables) => {
    const form = new FormData()
    form.append('file', file)
    if (referenceDate) form.append('referenceDate', referenceDate)
    if (storeCode) form.append('storeCode', storeCode)
    return apiRequest<ImportReport>(`/imports/${type}`, { method: 'POST', body: form })
  })

export const useConfirmImport = () =>
  useImportMutation((id: string) => apiRequest<ImportReport>(`/imports/${id}/confirm`, { method: 'POST' }))

export const useDiscardImport = () =>
  useImportMutation((id: string) => apiRequest<ImportReport>(`/imports/${id}/discard`, { method: 'POST' }))
