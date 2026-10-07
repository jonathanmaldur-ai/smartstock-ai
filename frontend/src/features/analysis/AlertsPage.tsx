import { BellOff, Eye, EyeOff } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { Modal } from '@/components/ui/Modal'
import { Switch } from '@/components/ui/Switch'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatDate, formatDateTime } from '@/lib/format'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { AnalysisBanner } from './AnalysisBanner'
import { AnalysisFilters } from './AnalysisFilters'
import { analyzeRoles, priorityInfo, type AlertPriority } from './analysisApi'
import {
  alertTypeHints,
  useAlerts,
  useAlertSummary,
  useMarkAlertsSeen,
  type AlertFilters,
  type AlertSummary,
  type AlertType,
  type StockAlert,
} from './alertsApi'

const priorities: readonly AlertPriority[] = ['High', 'Medium', 'Low']

/** Central de alertas (decisão 47): o que mudou desde a análise anterior e onde agir primeiro. Só avisa, nunca executa. */
export function AlertsPage() {
  const { user } = useAuth()
  const canMark = hasRole(user?.role, analyzeRoles)
  const [filters, setFilters] = useState<Omit<AlertFilters, 'page' | 'search'>>({ includeSeen: false })
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [productId, setProductId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search)
  const summary = useAlertSummary()

  const change = (next: Partial<typeof filters>) => {
    setFilters((current) => ({ ...current, ...next }))
    setPage(1)
  }

  return (
    <>
      <PageHeader
        title="Alertas"
        description="O que mudou desde a análise anterior e o que precisa de atenção. Os alertas só avisam: a decisão é sempre sua."
      />
      <AnalysisBanner />

      {summary.isPending ? (
        <Skeleton className="mb-6 h-32" />
      ) : summary.data?.analysisId ? (
        <TypeCards summary={summary.data} selected={filters.type} onSelect={(type) => change({ type: type === filters.type ? undefined : type })} />
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
            label: 'Tipo',
            value: filters.type,
            options: (summary.data?.types ?? []).map((t) => ({ value: t.type, label: t.label })),
            onChange: (type) => change({ type: type as AlertType | undefined }),
          },
          {
            kind: 'options',
            label: 'Prioridade',
            value: filters.priority,
            options: priorities.map((p) => ({ value: p, label: priorityInfo[p].label })),
            onChange: (priority) => change({ priority: priority as AlertPriority | undefined }),
          },
        ]}
      />

      <AlertsCard
        filters={{ ...filters, search: debouncedSearch, page }}
        canMark={canMark}
        onIncludeSeen={(includeSeen) => change({ includeSeen })}
        onPage={setPage}
        onOpen={setProductId}
      />

      <Modal open={productId !== null} onClose={() => setProductId(null)} title="Ficha do produto" size="lg">
        {productId !== null && <ProductOverviewView productId={productId} />}
      </Modal>
    </>
  )
}

function TypeCards({ summary, selected, onSelect }: { summary: AlertSummary; selected?: AlertType; onSelect: (type: AlertType) => void }) {
  return (
    <section className="mb-6 flex flex-col gap-3" aria-label="Alertas por tipo">
      <p className="text-caption text-text-muted">
        {summary.previousStockDate
          ? `Comparado com o estoque de ${formatDate(summary.previousStockDate)} (atual: ${formatDate(summary.stockDate!)}). Clique num tipo para filtrar.`
          : 'Ainda não há análise anterior: os alertas que comparam estoques aparecem a partir da próxima importação.'}
      </p>
      <div className="grid gap-3 tablet:grid-cols-2 notebook:grid-cols-5">
        {summary.types.map((t) => (
          <button
            key={t.type}
            type="button"
            aria-pressed={selected === t.type}
            onClick={() => onSelect(t.type)}
            className={cn(
              'flex flex-col gap-2 rounded-lg border border-border bg-surface p-4 text-left shadow-card transition-colors hover:bg-surface-muted',
              selected === t.type && 'border-primary bg-primary-soft',
            )}
          >
            <span className="flex items-start justify-between gap-2">
              <span className="text-label text-text">{t.label}</span>
              <Badge tone={priorityInfo[t.priority].tone}>{priorityInfo[t.priority].label}</Badge>
            </span>
            <span className="font-display text-display tabular-nums text-text">{t.unseen.toLocaleString('pt-BR')}</span>
            <span className="text-caption text-text-muted">
              {t.total > t.unseen ? `não vistos (de ${t.total.toLocaleString('pt-BR')}) · ` : ''}
              {alertTypeHints[t.type]}
            </span>
          </button>
        ))}
      </div>
    </section>
  )
}

function AlertsCard({
  filters,
  canMark,
  onIncludeSeen,
  onPage,
  onOpen,
}: {
  filters: AlertFilters
  canMark: boolean
  onIncludeSeen: (value: boolean) => void
  onPage: (page: number) => void
  onOpen: (productId: number) => void
}) {
  const toast = useToast()
  const { data, isPending, isError, refetch } = useAlerts(filters)
  const mark = useMarkAlertsSeen()
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const unseenOnPage = (data?.items ?? []).filter((a) => !a.seenAt)
  const allSelected = unseenOnPage.length > 0 && unseenOnPage.every((a) => selected.has(a.id))

  const toggle = (id: number) =>
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const markSeen = (ids: number[], seen: boolean) =>
    mark.mutate(
      { ids, seen },
      {
        onSuccess: (result) => {
          setSelected(new Set())
          toast.show(seen ? `${result.decided.toLocaleString('pt-BR')} alerta(s) marcado(s) como visto(s).` : 'Alerta voltou para a lista.', 'success')
        },
        onError: (e) => toast.show(e instanceof ApiError ? e.message : 'Não foi possível marcar os alertas.', 'error'),
      },
    )

  return (
    <Card>
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border p-4 tablet:px-6">
        <label className="flex items-center gap-3 text-body text-text">
          <Switch checked={filters.includeSeen} onChange={onIncludeSeen} label="Mostrar os já vistos" />
          Mostrar os já vistos
        </label>
        {canMark && (
          <Button
            size="sm"
            icon={<Eye aria-hidden className="size-4" />}
            disabled={selected.size === 0}
            loading={mark.isPending}
            onClick={() => markSeen([...selected], true)}
          >
            Marcar como visto{selected.size > 0 ? ` (${selected.size})` : ''}
          </Button>
        )}
      </div>
      {isPending ? (
        <TableLoading />
      ) : isError ? (
        <TableError what="os alertas" onRetry={() => void refetch()} />
      ) : data.totalCount === 0 ? (
        <EmptyState icon={BellOff} title="Nenhum alerta" description="Nada com esses filtros. Os já vistos aparecem ligando a opção acima." />
      ) : (
        <>
          <Table>
            <caption className="sr-only">Alertas</caption>
            <thead>
              <tr>
                {canMark && (
                  <Th className="w-10">
                    <input
                      type="checkbox"
                      className="size-4 accent-primary"
                      aria-label="Marcar todos os não vistos desta página"
                      checked={allSelected}
                      disabled={unseenOnPage.length === 0}
                      onChange={() => setSelected(allSelected ? new Set() : new Set(unseenOnPage.map((a) => a.id)))}
                    />
                  </Th>
                )}
                <Th>Prioridade</Th>
                <Th>Alerta</Th>
                <Th>Onde</Th>
                <Th>O que aconteceu</Th>
                <Th className="text-right">Ações</Th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((alert) => (
                <AlertRow
                  key={alert.id}
                  alert={alert}
                  canMark={canMark}
                  checked={selected.has(alert.id)}
                  onToggle={() => toggle(alert.id)}
                  onUnsee={() => markSeen([alert.id], false)}
                  onOpen={onOpen}
                />
              ))}
            </tbody>
          </Table>
          <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={onPage} />
        </>
      )}
    </Card>
  )
}

function AlertRow({
  alert,
  canMark,
  checked,
  onToggle,
  onUnsee,
  onOpen,
}: {
  alert: StockAlert
  canMark: boolean
  checked: boolean
  onToggle: () => void
  onUnsee: () => void
  onOpen: (productId: number) => void
}) {
  const where = alert.productDescription ?? (alert.brandName ? `Marca ${alert.brandName}` : '—')
  return (
    <tr className={cn(alert.seenAt && 'text-text-muted')}>
      {canMark && (
        <Td>
          {!alert.seenAt && (
            <input type="checkbox" className="size-4 accent-primary" aria-label={`Marcar ${where}`} checked={checked} onChange={onToggle} />
          )}
        </Td>
      )}
      <Td>
        <Badge tone={priorityInfo[alert.priority].tone}>{priorityInfo[alert.priority].label}</Badge>
      </Td>
      <Td className="min-w-40">{alert.typeLabel}</Td>
      <Td className="min-w-64">
        <span className="flex flex-col">
          <span>{where}</span>
          <span className="text-caption text-text-subtle">
            {alert.productCode && <span className="font-mono">{alert.productCode}</span>}
            {alert.productCode && alert.brandName && ' · '}
            {alert.productCode && alert.brandName}
            {alert.storeCode && ` · ${alert.storeCode} ${alert.storeName}`}
          </span>
        </span>
      </Td>
      <Td className="min-w-80 text-body">
        {alert.message}
        {alert.seenAt && (
          <span className="mt-1 block text-caption text-text-subtle">
            Visto por {alert.seenByEmail} em {formatDateTime(alert.seenAt)}
          </span>
        )}
      </Td>
      <Td className="whitespace-nowrap text-right">
        {alert.productId !== null && (
          <Button variant="ghost" size="sm" onClick={() => onOpen(alert.productId!)} aria-label={`Ver ficha de ${where}`}>
            Ver ficha
          </Button>
        )}
        {canMark && alert.seenAt && (
          <Button variant="ghost" size="sm" icon={<EyeOff aria-hidden className="size-4" />} onClick={onUnsee}>
            Desmarcar
          </Button>
        )}
      </Td>
    </tr>
  )
}
