import { ShoppingCart } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/Feedback'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { formatNumber } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { AnalysisBanner } from './AnalysisBanner'
import { AnalysisFilters } from './AnalysisFilters'
import { usePurchases, type PurchaseFilters } from './analysisApi'

/** Faltas que nenhuma loja da rede consegue cobrir (decisão 10): recomendação, nunca pedido de compra. */
export function PurchasesPage() {
  const [filters, setFilters] = useState<Omit<PurchaseFilters, 'page' | 'search'>>({})
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [productId, setProductId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search)
  const { data, isPending, isError, refetch } = usePurchases({ ...filters, search: debouncedSearch, page })

  const change = (next: Partial<typeof filters>) => {
    setFilters((current) => ({ ...current, ...next }))
    setPage(1)
  }

  return (
    <>
      <PageHeader
        title="Sugestões de compra"
        description="O que falta para levar as lojas ao estoque ideal e nenhuma unidade da rede (nem o Depósito) consegue ceder. É uma recomendação, não um pedido."
      />
      <AnalysisBanner />

      <AnalysisFilters
        search={search}
        onSearch={(value) => {
          setSearch(value)
          setPage(1)
        }}
        fields={[
          { kind: 'store', label: 'Loja', value: filters.storeId, onChange: (storeId) => change({ storeId }) },
          { kind: 'category', label: 'Categoria', value: filters.categoryId, onChange: (categoryId) => change({ categoryId }) },
        ]}
      />

      <Card>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="as sugestões de compra" onRetry={() => void refetch()} />
        ) : data.totalCount === 0 ? (
          <EmptyState icon={ShoppingCart} title="Nenhuma sugestão de compra" description="Ajuste os filtros ou gere uma nova análise." />
        ) : (
          <>
            <Table>
              <caption className="sr-only">Sugestões de compra por produto e loja</caption>
              <thead>
                <tr>
                  <Th>Produto</Th>
                  <Th>Loja</Th>
                  <Th className="text-right">Falta</Th>
                  <Th className="text-right">Estoque</Th>
                  <Th className="text-right">VMD</Th>
                  <Th className="text-right">Cobertura</Th>
                  <Th className="text-right">Ficha</Th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((p) => (
                  <tr key={`${p.productId}-${p.storeCode}`}>
                    <Td className="min-w-64">
                      <span className="flex flex-col">
                        <span>{p.productDescription}</span>
                        <span className="text-caption text-text-subtle">
                          <span className="font-mono">{p.productCode}</span> · {p.brandName} · {p.categoryName}
                        </span>
                      </span>
                    </Td>
                    <Td className="whitespace-nowrap">
                      {p.storeCode} {p.storeName}
                    </Td>
                    <Td className="text-right font-medium tabular-nums">{formatNumber(p.quantity)}</Td>
                    <Td className="text-right tabular-nums">{formatNumber(p.stock, 1)}</Td>
                    <Td className="text-right tabular-nums">{formatNumber(p.dailyAverage)}</Td>
                    <Td className="whitespace-nowrap text-right tabular-nums">
                      {p.coverageDays === null ? '—' : `${formatNumber(p.coverageDays, 0)} dias`}
                    </Td>
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => setProductId(p.productId)} aria-label={`Ver ficha de ${p.productDescription}`}>
                        Ver ficha
                      </Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
          </>
        )}
      </Card>

      <Modal open={productId !== null} onClose={() => setProductId(null)} title="Ficha do produto" size="lg">
        {productId !== null && <ProductOverviewView productId={productId} />}
      </Modal>
    </>
  )
}
