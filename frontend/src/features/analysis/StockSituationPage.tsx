import { Activity } from 'lucide-react'
import { useState } from 'react'
import { ActionCard } from './ActionCard'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Badge, EmptyState } from '@/components/ui/Feedback'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { cn } from '@/lib/cn'
import { formatNumber } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { AnalysisBanner } from './AnalysisBanner'
import { AnalysisFilters } from './AnalysisFilters'
import {
  priorityInfo,
  situationInfo,
  useAnalysisSummary,
  usePositions,
  type Position,
  type PositionFilters,
  type StockSituation,
} from './analysisApi'

/** Ordem dos cartões: do mais urgente ao menos urgente. */
const situationOrder: readonly StockSituation[] = ['Rupture', 'CriticalCoverage', 'BelowMinimum', 'Normal', 'Excess', 'Stagnant']

export function StockSituationPage() {
  const [filters, setFilters] = useState<Omit<PositionFilters, 'page' | 'search'>>({})
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [productId, setProductId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search)
  const summary = useAnalysisSummary()
  const { data, isPending, isError, refetch } = usePositions({ ...filters, search: debouncedSearch, page })

  const change = (next: Partial<typeof filters>) => {
    setFilters((current) => ({ ...current, ...next }))
    setPage(1)
  }

  return (
    <>
      <PageHeader
        title="Situação do estoque"
        description="Cada produto em cada loja, classificado pela cobertura (estoque ÷ venda média diária). Os mais urgentes aparecem primeiro."
      />
      <AnalysisBanner />

      {summary.data?.analysisId && (
        <div className="mb-6 grid gap-3 tablet:grid-cols-3">
          <ActionCard
            tone="text-error"
            value={summary.data.relevantRuptures}
            title="Rupturas que importam"
            description="Produtos que vendem pelo menos 12 por ano e estão sem estoque na loja"
            onClick={() => change({ situation: 'Rupture' })}
          />
          <ActionCard
            tone="text-primary"
            value={summary.data.suggestions.pendingUnits}
            title="Unidades para transferir"
            description={`${summary.data.suggestions.pending.toLocaleString('pt-BR')} sugestões pendentes de aprovação`}
            to="/sugestoes"
          />
          <ActionCard
            tone="text-warning"
            value={summary.data.purchaseUnits}
            title="Unidades para comprar"
            description={`${summary.data.purchaseCount.toLocaleString('pt-BR')} faltas que a rede não consegue cobrir`}
            to="/compras"
          />
        </div>
      )}

      {summary.data?.analysisId && <h2 className="mb-3 text-subtitle text-text">Detalhe por situação</h2>}
      {summary.data?.analysisId && (
        <div className="mb-6 grid grid-cols-2 gap-3 tablet:grid-cols-3 notebook:grid-cols-6">
          {situationOrder.map((situation) => {
            const count = summary.data.situations.find((s) => s.situation === situation)?.count ?? 0
            const selected = filters.situation === situation
            return (
              <button
                key={situation}
                type="button"
                aria-pressed={selected}
                onClick={() => change({ situation: selected ? undefined : situation })}
                className={cn(
                  'flex flex-col gap-1 rounded-md border bg-surface p-4 text-left transition-colors hover:bg-surface-muted',
                  selected ? 'border-primary ring-2 ring-primary' : 'border-border',
                )}
              >
                <Badge tone={situationInfo[situation].tone}>{situationInfo[situation].label}</Badge>
                <span className="font-display text-heading tabular-nums text-text">{count.toLocaleString('pt-BR')}</span>
                <span className="text-caption text-text-muted">{situationInfo[situation].description}</span>
              </button>
            )
          })}
        </div>
      )}

      <AnalysisFilters
        search={search}
        onSearch={(value) => {
          setSearch(value)
          setPage(1)
        }}
        fields={[
          { kind: 'store', label: 'Loja', value: filters.storeId, onChange: (storeId) => change({ storeId }) },
          {
            kind: 'options',
            label: 'Situação',
            value: filters.situation,
            options: situationOrder.map((s) => ({ value: s, label: situationInfo[s].label })),
            onChange: (situation) => change({ situation: situation as StockSituation | undefined }),
          },
          { kind: 'category', label: 'Categoria', value: filters.categoryId, onChange: (categoryId) => change({ categoryId }) },
        ]}
      />

      <Card>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="a situação do estoque" onRetry={() => void refetch()} />
        ) : data.totalCount === 0 ? (
          <EmptyState
            icon={Activity}
            title={summary.data?.analysisId ? 'Nenhum item encontrado' : 'Nenhuma análise gerada'}
            description={summary.data?.analysisId ? 'Ajuste a busca ou os filtros.' : 'Gere a análise para ver a situação do estoque.'}
          />
        ) : (
          <>
            <PositionsTable items={data.items} onOpen={setProductId} />
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

function PositionsTable({ items, onOpen }: { items: Position[]; onOpen: (productId: number) => void }) {
  return (
    <Table>
      <caption className="sr-only">Situação de cada produto por loja</caption>
      <thead>
        <tr>
          <Th>Produto</Th>
          <Th>Loja</Th>
          <Th className="text-right">Estoque</Th>
          <Th className="text-right">Venda 12 meses</Th>
          <Th className="text-right">VMD</Th>
          <Th className="text-right">Cobertura</Th>
          <Th>Situação</Th>
          <Th>Prioridade</Th>
          <Th className="text-right">Ficha</Th>
        </tr>
      </thead>
      <tbody>
        {items.map((p) => (
          <tr key={`${p.productId}-${p.storeCode}`}>
            <Td className="min-w-64">
              <span className="flex flex-col">
                <span>{p.productDescription}</span>
                <span className="text-caption text-text-subtle">
                  <span className="font-mono">{p.productCode}</span> · {p.brandName}
                </span>
              </span>
            </Td>
            <Td className="whitespace-nowrap">
              {p.storeCode} {p.storeName}
            </Td>
            <Td className="text-right tabular-nums">
              <span className="flex flex-col items-end">
                <span className={cn(p.stock < 0 && 'font-medium text-error')}>{formatNumber(p.stock)}</span>
                {p.projectedStock !== p.stock && (
                  <span className="text-caption text-text-subtle">projetado {formatNumber(p.projectedStock, 1)}</span>
                )}
              </span>
            </Td>
            <Td className="text-right tabular-nums">{formatNumber(p.sold12Months)}</Td>
            <Td className="text-right tabular-nums">{p.dailyAverage ? formatNumber(p.dailyAverage) : '—'}</Td>
            <Td className="whitespace-nowrap text-right tabular-nums">
              {p.coverageDays === null ? '—' : `${formatNumber(p.coverageDays, 0)} dias`}
            </Td>
            <Td>
              <Badge tone={situationInfo[p.situation].tone}>{situationInfo[p.situation].label}</Badge>
            </Td>
            <Td>
              {p.priority === 'None' ? '—' : <Badge tone={priorityInfo[p.priority].tone}>{priorityInfo[p.priority].label}</Badge>}
            </Td>
            <Td className="text-right">
              <Button variant="ghost" size="sm" onClick={() => onOpen(p.productId)} aria-label={`Ver ficha de ${p.productDescription}`}>
                Ver ficha
              </Button>
            </Td>
          </tr>
        ))}
      </tbody>
    </Table>
  )
}
