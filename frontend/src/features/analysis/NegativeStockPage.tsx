import { Download, MinusCircle, TrendingDown, TrendingUp } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { apiDownload } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatDate, formatNumber } from '@/lib/format'
import { toQuery } from '@/lib/query'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { ActionCard } from './ActionCard'
import { AnalysisBanner } from './AnalysisBanner'
import { AnalysisFilters } from './AnalysisFilters'
import { priorityInfo, type AlertPriority } from './analysisApi'
import {
  causeInfo,
  negativeCauses,
  useNegativeRanking,
  useNegatives,
  useNegativeSummary,
  type NegativeCause,
  type NegativeFilters,
  type NegativeItem,
  type NegativeStoreRow,
} from './negativesApi'

const priorities: readonly AlertPriority[] = ['Critical', 'High', 'Medium', 'Low']

/** Decisão 26: onde estão os negativos, se estão diminuindo e por onde começar a correção no ERP. */
export function NegativeStockPage() {
  const toast = useToast()
  const [filters, setFilters] = useState<Omit<NegativeFilters, 'page' | 'search'>>({})
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [productId, setProductId] = useState<number | null>(null)
  const [exporting, setExporting] = useState(false)
  const debouncedSearch = useDebouncedValue(search)
  const summary = useNegativeSummary()

  const change = (next: Partial<typeof filters>) => {
    setFilters((current) => ({ ...current, ...next }))
    setPage(1)
  }

  const exportExcel = async () => {
    setExporting(true)
    try {
      await apiDownload(`/analysis/negatives/export?${toQuery({ ...filters, search: debouncedSearch })}`, 'estoque-negativo.xlsx')
    } catch {
      toast.show('Não foi possível gerar o Excel.', 'error')
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <PageHeader
        title="Estoque negativo"
        description="Onde estão os negativos, se estão diminuindo e as causas prováveis. A correção é feita no ERP; o Excel traz a lista para isso."
        actions={
          <Button variant="secondary" icon={<Download aria-hidden className="size-4" />} loading={exporting} onClick={() => void exportExcel()}>
            Excel para correção
          </Button>
        }
      />
      <AnalysisBanner />

      {summary.isPending ? (
        <Skeleton className="mb-6 h-32" />
      ) : summary.data?.analysisId ? (
        <>
          <div className="mb-6 grid gap-3 tablet:grid-cols-3">
            <ActionCard tone="text-error" value={summary.data.items} title="Itens negativos" description="Produto × loja com estoque abaixo de zero" />
            <ActionCard tone="text-error" value={summary.data.units} title="Unidades negativas" description="Soma de todos os negativos" />
            <ActionCard
              tone="text-warning"
              value={summary.data.criticalItems}
              title="Críticos"
              description="Negativos em produto que vende na loja: comece por eles"
              onClick={() => change({ priority: 'Critical' })}
            />
          </div>

          <StoresCard
            stores={summary.data.stores}
            previousDate={summary.data.previousStockDate}
            selected={filters.storeId}
            onSelect={(storeId) => change({ storeId: storeId === filters.storeId ? undefined : storeId })}
          />

          <div className="mb-6 grid gap-6 notebook:grid-cols-2">
            <CausesCard causes={summary.data.causes} selected={filters.cause} onSelect={(cause) => change({ cause: cause === filters.cause ? undefined : cause })} />
            <RankingCard storeId={filters.storeId} />
          </div>
        </>
      ) : null}

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
            label: 'Prioridade',
            value: filters.priority,
            options: priorities.map((p) => ({ value: p, label: priorityInfo[p].label })),
            onChange: (priority) => change({ priority: priority as AlertPriority | undefined }),
          },
          {
            kind: 'options',
            label: 'Causa provável',
            value: filters.cause,
            options: negativeCauses.map((c) => ({ value: c, label: causeInfo[c].label })),
            onChange: (cause) => change({ cause: cause as NegativeCause | undefined }),
          },
        ]}
      />

      <ItemsCard filters={{ ...filters, search: debouncedSearch, page }} onPage={setPage} onOpen={setProductId} />

      <Modal open={productId !== null} onClose={() => setProductId(null)} title="Ficha do produto" size="lg">
        {productId !== null && <ProductOverviewView productId={productId} />}
      </Modal>
    </>
  )
}

function StoresCard({
  stores,
  previousDate,
  selected,
  onSelect,
}: {
  stores: NegativeStoreRow[]
  previousDate: string | null
  selected?: number
  onSelect: (storeId: number) => void
}) {
  return (
    <Card className="mb-6">
      <CardHeader
        title="Por loja"
        description={
          previousDate
            ? `Variação comparada com o estoque de ${formatDate(previousDate)}. Clique numa loja para filtrar.`
            : 'A variação aparece a partir da próxima importação de estoque. Clique numa loja para filtrar.'
        }
      />
      <Table>
        <caption className="sr-only">Estoque negativo por loja</caption>
        <thead>
          <tr>
            <Th>Loja</Th>
            <Th className="text-right">Itens negativos</Th>
            <Th className="text-right">Unidades</Th>
            <Th className="text-right">% dos itens</Th>
            <Th className="text-right">Críticos</Th>
            <Th className="text-right">Variação</Th>
          </tr>
        </thead>
        <tbody>
          {stores.map((s) => (
            <tr key={s.storeId} className={cn(selected === s.storeId && 'bg-primary-soft')}>
              <Td className="whitespace-nowrap">
                <button type="button" className="text-left font-medium text-primary hover:underline" aria-pressed={selected === s.storeId} onClick={() => onSelect(s.storeId)}>
                  {s.code} {s.name}
                </button>
              </Td>
              <Td className="text-right tabular-nums">{s.items.toLocaleString('pt-BR')}</Td>
              <Td className="text-right tabular-nums text-error">{formatNumber(s.units, 0)}</Td>
              <Td className="text-right tabular-nums">{formatNumber(s.percentOfItems, 1)}%</Td>
              <Td className="text-right tabular-nums">{s.criticalItems.toLocaleString('pt-BR')}</Td>
              <Td className="text-right">
                <Trend current={s.items} previous={s.previousItems} />
              </Td>
            </tr>
          ))}
        </tbody>
      </Table>
    </Card>
  )
}

/** Menos negativos = melhorou (verde). */
function Trend({ current, previous }: { current: number; previous: number | null }) {
  if (previous === null) return <span className="text-text-subtle">—</span>
  const delta = current - previous
  if (delta === 0) return <span className="text-text-muted">igual</span>
  const better = delta < 0
  const Icon = better ? TrendingDown : TrendingUp
  return (
    <span className={cn('inline-flex items-center gap-1 tabular-nums', better ? 'text-success' : 'text-error')}>
      <Icon aria-hidden className="size-4" />
      {delta > 0 ? '+' : ''}
      {delta.toLocaleString('pt-BR')} itens
    </span>
  )
}

function CausesCard({
  causes,
  selected,
  onSelect,
}: {
  causes: { cause: NegativeCause; items: number }[]
  selected?: NegativeCause
  onSelect: (cause: NegativeCause) => void
}) {
  return (
    <Card>
      <CardHeader title="Causas prováveis" description="Hipóteses para investigar no ERP. Um item pode ter mais de uma, ou nenhuma identificada." />
      <ul className="flex flex-col divide-y divide-border">
        {causes.map((c) => (
          <li key={c.cause}>
            <button
              type="button"
              aria-pressed={selected === c.cause}
              onClick={() => onSelect(c.cause)}
              className={cn('flex w-full items-center justify-between gap-4 p-4 text-left hover:bg-surface-muted', selected === c.cause && 'bg-primary-soft')}
            >
              <span className="flex flex-col">
                <span className="text-body font-medium text-text">{causeInfo[c.cause].label}</span>
                <span className="text-caption text-text-muted">{causeInfo[c.cause].hint}</span>
              </span>
              <span className="font-display text-subtitle tabular-nums text-text">{c.items.toLocaleString('pt-BR')}</span>
            </button>
          </li>
        ))}
      </ul>
    </Card>
  )
}

function RankingCard({ storeId }: { storeId?: number }) {
  const [by, setBy] = useState<'Brand' | 'Category'>('Brand')
  const { data, isPending, isError } = useNegativeRanking(by, storeId)
  const worst = data?.[0]?.units ?? 0

  return (
    <Card>
      <CardHeader
        title={by === 'Brand' ? 'Marcas com mais negativo' : 'Categorias com mais negativo'}
        description={storeId ? 'Na loja selecionada.' : 'Na rede toda.'}
        actions={
          <div className="flex gap-1">
            <Button size="sm" variant={by === 'Brand' ? 'primary' : 'secondary'} onClick={() => setBy('Brand')}>
              Marca
            </Button>
            <Button size="sm" variant={by === 'Category' ? 'primary' : 'secondary'} onClick={() => setBy('Category')}>
              Categoria
            </Button>
          </div>
        }
      />
      {isPending ? (
        <TableLoading rows={5} />
      ) : isError ? (
        <p className="p-4 text-body text-error">Não foi possível carregar o ranking.</p>
      ) : (
        <ol className="flex flex-col gap-3 p-4">
          {data.slice(0, 10).map((g) => (
            <li key={g.id} className="flex flex-col gap-1">
              <span className="flex justify-between gap-2 text-body">
                <span className="truncate text-text">{g.name}</span>
                <span className="shrink-0 tabular-nums text-text-muted">
                  {formatNumber(g.units, 0)} un. · {g.items.toLocaleString('pt-BR')} itens
                </span>
              </span>
              <span className="h-2 w-full rounded-full bg-surface-muted" aria-hidden>
                <span className="block h-2 rounded-full bg-error" style={{ width: `${worst ? Math.max((g.units / worst) * 100, 2) : 0}%` }} />
              </span>
            </li>
          ))}
        </ol>
      )}
    </Card>
  )
}

function ItemsCard({ filters, onPage, onOpen }: { filters: NegativeFilters; onPage: (page: number) => void; onOpen: (productId: number) => void }) {
  const { data, isPending, isError, refetch } = useNegatives(filters)

  return (
    <Card>
      {isPending ? (
        <TableLoading />
      ) : isError ? (
        <TableError what="os negativos" onRetry={() => void refetch()} />
      ) : data.totalCount === 0 ? (
        <EmptyState icon={MinusCircle} title="Nenhum estoque negativo encontrado" description="Ajuste os filtros ou gere uma nova análise." />
      ) : (
        <>
          <Table>
            <caption className="sr-only">Itens com estoque negativo</caption>
            <thead>
              <tr>
                <Th>Prioridade</Th>
                <Th>Produto</Th>
                <Th>Loja</Th>
                <Th className="text-right">Estoque</Th>
                <Th className="text-right">Vendeu em 12 meses</Th>
                <Th>Causas prováveis</Th>
                <Th className="text-right">Ficha</Th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((item) => (
                <ItemRow key={`${item.productId}-${item.storeCode}`} item={item} onOpen={onOpen} />
              ))}
            </tbody>
          </Table>
          <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={onPage} />
        </>
      )}
    </Card>
  )
}

function ItemRow({ item, onOpen }: { item: NegativeItem; onOpen: (productId: number) => void }) {
  return (
    <tr>
      <Td>
        <Badge tone={priorityInfo[item.priority].tone}>{priorityInfo[item.priority].label}</Badge>
      </Td>
      <Td className="min-w-64">
        <span className="flex flex-col">
          <span>{item.productDescription}</span>
          <span className="text-caption text-text-subtle">
            <span className="font-mono">{item.productCode}</span>
            {item.productReference && ` · Ref. ${item.productReference}`} · {item.brandName}
          </span>
        </span>
      </Td>
      <Td className="whitespace-nowrap">
        {item.storeCode} {item.storeName}
      </Td>
      <Td className="text-right font-medium tabular-nums text-error">{formatNumber(item.quantity)}</Td>
      <Td className="text-right tabular-nums">{item.sold12Months ? formatNumber(item.sold12Months) : '—'}</Td>
      <Td className="min-w-56">
        <span className="flex flex-wrap gap-1">
          {item.causes.length === 0 ? (
            <span className="text-caption text-text-subtle">Nenhuma identificada</span>
          ) : (
            item.causes.map((c) => (
              <Badge key={c} tone="warning">
                {causeInfo[c].label}
              </Badge>
            ))
          )}
        </span>
        {item.pendingTransferUnits > 0 && (
          <span className="mt-1 block text-caption text-text-muted">{formatNumber(item.pendingTransferUnits)} un. enviadas sem entrada</span>
        )}
        {item.duplicateProductCode && (
          <span className="mt-1 block text-caption text-text-muted">
            Ver também o código <span className="font-mono">{item.duplicateProductCode}</span>
          </span>
        )}
      </Td>
      <Td className="text-right">
        <Button variant="ghost" size="sm" onClick={() => onOpen(item.productId)} aria-label={`Ver ficha de ${item.productDescription}`}>
          Ver ficha
        </Button>
      </Td>
    </tr>
  )
}
