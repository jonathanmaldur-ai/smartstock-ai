import { ArrowLeft, ArrowLeftRight, Check, Download, X } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Alert, Badge, EmptyState } from '@/components/ui/Feedback'
import { TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Switch } from '@/components/ui/Switch'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ApiError, apiDownload } from '@/lib/api'
import { formatDate, formatDateTime, formatNumber } from '@/lib/format'
import { toQuery } from '@/lib/query'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { AnalysisBanner } from './AnalysisBanner'
import { AnalysisFilters } from './AnalysisFilters'
import {
  approveRoles,
  priorityInfo,
  statusInfo,
  useDecideSuggestions,
  useSuggestionRoutes,
  useSuggestions,
  type AlertPriority,
  type Suggestion,
  type SuggestionRoute,
  type SuggestionStatus,
} from './analysisApi'

const statuses: readonly SuggestionStatus[] = ['Suggested', 'Approved', 'Completed', 'Rejected', 'Superseded']
const priorities: readonly AlertPriority[] = ['Critical', 'High', 'Medium']

const days = (value: number | null) => (value === null ? '—' : `${formatNumber(value, 0)} dias`)
const routeLabel = (r: { originCode: string; originName: string; destinationCode: string; destinationName: string }) =>
  `${r.originCode} ${r.originName} → ${r.destinationCode} ${r.destinationName}`

type CommonFilters = { status?: SuggestionStatus; priority?: AlertPriority; categoryId?: number; search: string; hideNegativeDestination: boolean }

/** O que a decisão vai afetar: sugestões marcadas ou uma rota inteira. */
type DecisionTarget = { approve: boolean } & ({ kind: 'ids'; suggestions: Suggestion[] } | { kind: 'route'; route: SuggestionRoute })

export function SuggestionsPage() {
  const { user } = useAuth()
  const toast = useToast()
  const [status, setStatus] = useState<SuggestionStatus | undefined>('Suggested')
  const [priority, setPriority] = useState<AlertPriority | undefined>()
  const [categoryId, setCategoryId] = useState<number | undefined>()
  const [search, setSearch] = useState('')
  const [showNegative, setShowNegative] = useState(false)
  const [route, setRoute] = useState<SuggestionRoute | null>(null)
  const [decision, setDecision] = useState<DecisionTarget | null>(null)
  const [exporting, setExporting] = useState(false)
  const debouncedSearch = useDebouncedValue(search)
  const canDecide = hasRole(user?.role, approveRoles)

  const filters: CommonFilters = { status, priority, categoryId, search: debouncedSearch, hideNegativeDestination: !showNegative }

  const exportExcel = async () => {
    setExporting(true)
    try {
      const query = toQuery({ ...filters, originStoreId: route?.originStoreId, destinationStoreId: route?.destinationStoreId })
      const name = route ? `separacao-${route.originCode}-para-${route.destinationCode}.xlsx` : 'sugestoes-transferencia.xlsx'
      await apiDownload(`/transfer-suggestions/export?${query}`, name)
    } catch {
      toast.show('Não foi possível gerar o Excel.', 'error')
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <PageHeader
        title="Sugestões de transferência"
        description="Agrupadas por rota: cada rota é uma carga de uma unidade para outra. O SmartStock só recomenda; a transferência é feita no ERP."
        actions={
          <Button variant="secondary" icon={<Download aria-hidden className="size-4" />} loading={exporting} onClick={() => void exportExcel()}>
            {route ? 'Excel desta rota' : 'Exportar Excel'}
          </Button>
        }
      />
      <AnalysisBanner />

      <AnalysisFilters
        search={search}
        onSearch={setSearch}
        fields={[
          {
            kind: 'options',
            label: 'Status',
            value: status,
            options: statuses.map((s) => ({ value: s, label: statusInfo[s].label })),
            onChange: (value) => setStatus(value as SuggestionStatus | undefined),
          },
          {
            kind: 'options',
            label: 'Prioridade',
            value: priority,
            options: priorities.map((p) => ({ value: p, label: priorityInfo[p].label })),
            onChange: (value) => setPriority(value as AlertPriority | undefined),
          },
          { kind: 'category', label: 'Categoria', value: categoryId, onChange: setCategoryId },
        ]}
      />

      <div className="mb-6 flex items-center gap-3">
        <Switch checked={showNegative} onChange={setShowNegative} label="Mostrar lojas com estoque negativo" />
        <span className="text-body text-text">Mostrar lojas com estoque negativo</span>
        <span className="text-caption text-text-subtle">(ocultas enquanto os negativos são corrigidos no ERP)</span>
      </div>

      {route ? (
        <RouteDetail
          route={route}
          filters={filters}
          canDecide={canDecide}
          onBack={() => setRoute(null)}
          onDecide={setDecision}
        />
      ) : (
        <RoutesList filters={filters} canDecide={canDecide} onOpen={setRoute} onDecide={setDecision} />
      )}

      <DecisionModal target={decision} hideNegativeDestination={!showNegative} onClose={() => setDecision(null)} />
    </>
  )
}

function RoutesList({
  filters,
  canDecide,
  onOpen,
  onDecide,
}: {
  filters: CommonFilters
  canDecide: boolean
  onOpen: (route: SuggestionRoute) => void
  onDecide: (target: DecisionTarget) => void
}) {
  const { data, isPending, isError, refetch } = useSuggestionRoutes(filters)
  const pending = filters.status === 'Suggested'

  return (
    <Card>
      {isPending ? (
        <TableLoading />
      ) : isError ? (
        <TableError what="as rotas" onRetry={() => void refetch()} />
      ) : data.length === 0 ? (
        <EmptyState icon={ArrowLeftRight} title="Nenhuma sugestão encontrada" description="Ajuste os filtros ou gere uma nova análise." />
      ) : (
        <Table>
          <caption className="sr-only">Sugestões agrupadas por rota</caption>
          <thead>
            <tr>
              <Th>Rota (de → para)</Th>
              <Th className="text-right">Produtos</Th>
              <Th className="text-right">Unidades</Th>
              <Th className="text-right">Críticos</Th>
              <Th className="text-right">Ações</Th>
            </tr>
          </thead>
          <tbody>
            {data.map((r) => (
              <tr key={`${r.originStoreId}-${r.destinationStoreId}`}>
                <Td className="min-w-64">
                  <span className="flex flex-col gap-1">
                    <button type="button" className="text-left font-medium text-primary hover:underline" onClick={() => onOpen(r)}>
                      {routeLabel(r)}
                    </button>
                    <span className="text-caption text-text-muted">
                      {r.topProducts.map((p) => `${p.description} (${formatNumber(p.quantity, 0)} un.)`).join(' · ')}
                      {r.count > r.topProducts.length && ` · + ${(r.count - r.topProducts.length).toLocaleString('pt-BR')} produtos`}
                    </span>
                  </span>
                </Td>
                <Td className="text-right tabular-nums">{r.count.toLocaleString('pt-BR')}</Td>
                <Td className="text-right tabular-nums">{formatNumber(r.units, 0)}</Td>
                <Td className="text-right tabular-nums">
                  {r.criticalCount > 0 ? <Badge tone="error">{r.criticalCount.toLocaleString('pt-BR')}</Badge> : '—'}
                </Td>
                <Td className="text-right">
                  <div className="flex justify-end gap-2">
                    <Button variant="secondary" size="sm" onClick={() => onOpen(r)}>
                      Ver produtos
                    </Button>
                    {canDecide && pending && (
                      <Button size="sm" onClick={() => onDecide({ kind: 'route', route: r, approve: true })}>
                        Aprovar rota
                      </Button>
                    )}
                  </div>
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
    </Card>
  )
}

function RouteDetail({
  route,
  filters,
  canDecide,
  onBack,
  onDecide,
}: {
  route: SuggestionRoute
  filters: CommonFilters
  canDecide: boolean
  onBack: () => void
  onDecide: (target: DecisionTarget) => void
}) {
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [detail, setDetail] = useState<Suggestion | null>(null)
  const { data, isPending, isError, refetch } = useSuggestions({
    ...filters,
    originStoreId: route.originStoreId,
    destinationStoreId: route.destinationStoreId,
    page,
  })

  const items = data?.items ?? []
  const pendingOnPage = items.filter((s) => s.status === 'Suggested')
  const selectedItems = items.filter((s) => selected.has(s.id))
  const allSelected = pendingOnPage.length > 0 && pendingOnPage.every((s) => selected.has(s.id))
  const toggle = (id: number) =>
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  const clearAfter = (target: DecisionTarget) => {
    onDecide(target)
    setSelected(new Set())
  }

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Button variant="ghost" icon={<ArrowLeft aria-hidden className="size-4" />} onClick={onBack}>
          Todas as rotas
        </Button>
      </div>

      <Card>
        <CardHeader
          title={routeLabel(route)}
          description={`${route.count.toLocaleString('pt-BR')} produtos · ${formatNumber(route.units, 0)} unidades`}
          actions={
            canDecide &&
            filters.status === 'Suggested' && (
              <>
                <Button variant="secondary" icon={<X aria-hidden className="size-4" />} onClick={() => clearAfter({ kind: 'route', route, approve: false })}>
                  Rejeitar rota
                </Button>
                <Button icon={<Check aria-hidden className="size-4" />} onClick={() => clearAfter({ kind: 'route', route, approve: true })}>
                  Aprovar rota inteira
                </Button>
              </>
            )
          }
        />

        {canDecide && selected.size > 0 && (
          <div className="flex flex-col gap-3 border-b border-border bg-surface-muted p-4 tablet:flex-row tablet:items-center tablet:justify-between">
            <p className="text-body text-text">
              {selected.size} marcado{selected.size > 1 ? 's' : ''} · {formatNumber(selectedItems.reduce((t, s) => t + s.quantity, 0))} unidades
            </p>
            <div className="flex gap-2">
              <Button variant="secondary" size="sm" onClick={() => clearAfter({ kind: 'ids', suggestions: selectedItems, approve: false })}>
                Rejeitar marcados
              </Button>
              <Button size="sm" onClick={() => clearAfter({ kind: 'ids', suggestions: selectedItems, approve: true })}>
                Aprovar marcados
              </Button>
            </div>
          </div>
        )}

        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="os produtos da rota" onRetry={() => void refetch()} />
        ) : data.totalCount === 0 ? (
          <EmptyState icon={ArrowLeftRight} title="Nenhum produto nesta rota" description="Ajuste os filtros." />
        ) : (
          <>
            <Table>
              <caption className="sr-only">Produtos da rota {routeLabel(route)}</caption>
              <thead>
                <tr>
                  {canDecide && (
                    <Th className="w-10">
                      <input
                        type="checkbox"
                        className="size-4 accent-primary"
                        aria-label="Marcar todos os pendentes desta página"
                        checked={allSelected}
                        disabled={pendingOnPage.length === 0}
                        onChange={() => setSelected(allSelected ? new Set() : new Set(pendingOnPage.map((s) => s.id)))}
                      />
                    </Th>
                  )}
                  <Th>Prioridade</Th>
                  <Th>Produto</Th>
                  <Th className="text-right">Enviar</Th>
                  <Th className="text-right">No destino hoje</Th>
                  <Th className="text-right">Dura hoje → depois</Th>
                  <Th className="text-right">Na origem</Th>
                  <Th>Status</Th>
                  <Th className="text-right">Motivo</Th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((s) => (
                  <tr key={s.id}>
                    {canDecide && (
                      <Td>
                        {s.status === 'Suggested' && (
                          <input
                            type="checkbox"
                            className="size-4 accent-primary"
                            aria-label={`Marcar ${s.productDescription}`}
                            checked={selected.has(s.id)}
                            onChange={() => toggle(s.id)}
                          />
                        )}
                      </Td>
                    )}
                    <Td>
                      <Badge tone={priorityInfo[s.priority].tone}>{priorityInfo[s.priority].label}</Badge>
                    </Td>
                    <Td className="min-w-64">
                      <span className="flex flex-col">
                        <span>{s.productDescription}</span>
                        <span className="text-caption text-text-subtle">
                          <span className="font-mono">{s.productCode}</span>
                          {s.productReference && ` · Ref. ${s.productReference}`} · {s.brandName}
                        </span>
                      </span>
                    </Td>
                    <Td className="text-right font-medium tabular-nums">{formatNumber(s.quantity)} un.</Td>
                    <Td className="whitespace-nowrap text-right tabular-nums">
                      <span className="flex flex-col items-end gap-1">
                        <span className={s.destinationStock < 0 ? 'font-medium text-error' : undefined}>{formatNumber(s.destinationStock)} un.</span>
                        {s.destinationNegative && <Badge tone="warning">Confirme o estoque físico</Badge>}
                      </span>
                    </Td>
                    <Td className="whitespace-nowrap text-right tabular-nums">
                      {days(s.destinationCoverageDays)} → {days(s.destinationCoverageAfter)}
                    </Td>
                    <Td className="whitespace-nowrap text-right tabular-nums">
                      <span className="flex flex-col items-end">
                        <span>{formatNumber(s.originStock)} un.</span>
                        <span className="text-caption text-text-subtle">
                          {s.originCoverageDays === null ? 'não vende' : `dura ${days(s.originCoverageDays)}`}
                        </span>
                      </span>
                    </Td>
                    <Td>
                      <Badge tone={statusInfo[s.status].tone}>{statusInfo[s.status].label}</Badge>
                    </Td>
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => setDetail(s)} aria-label={`Ver motivo de ${s.productDescription}`}>
                        Ver
                      </Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={(p) => {
                setPage(p)
                setSelected(new Set())
              }}
            />
          </>
        )}
      </Card>

      <Modal open={detail !== null} onClose={() => setDetail(null)} title="Por que esta sugestão?" size="lg">
        {detail && <SuggestionDetail suggestion={detail} />}
      </Modal>
    </div>
  )
}

function DecisionModal({
  target,
  hideNegativeDestination,
  onClose,
}: {
  target: DecisionTarget | null
  hideNegativeDestination: boolean
  onClose: () => void
}) {
  const toast = useToast()
  const decide = useDecideSuggestions()
  const [note, setNote] = useState('')
  const approve = target?.approve ?? true
  const summary =
    target?.kind === 'route'
      ? `Todos os ${target.route.count.toLocaleString('pt-BR')} produtos pendentes da rota ${routeLabel(target.route)} (${formatNumber(target.route.units, 0)} unidades).`
      : target?.kind === 'ids'
        ? `${target.suggestions.length} produto${target.suggestions.length > 1 ? 's' : ''} marcado${target.suggestions.length > 1 ? 's' : ''} (${formatNumber(target.suggestions.reduce((t, s) => t + s.quantity, 0))} unidades).`
        : ''

  const confirm = async () => {
    if (!target) return
    try {
      const body =
        target.kind === 'route'
          ? { approve, note: note || undefined, originStoreId: target.route.originStoreId, destinationStoreId: target.route.destinationStoreId, hideNegativeDestination }
          : { approve, note: note || undefined, ids: target.suggestions.map((s) => s.id) }
      const result = await decide.mutateAsync(body)
      toast.show(`${result.decided.toLocaleString('pt-BR')} sugest${result.decided > 1 ? 'ões' : 'ão'} ${approve ? 'aprovada' : 'rejeitada'}${result.decided > 1 ? 's' : ''}.`)
      setNote('')
      onClose()
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível registrar a decisão.', 'error')
    }
  }

  return (
    <Modal
      open={target !== null}
      onClose={onClose}
      title={approve ? 'Aprovar sugestões' : 'Rejeitar sugestões'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button variant={approve ? 'primary' : 'danger'} loading={decide.isPending} onClick={() => void confirm()}>
            {approve ? 'Aprovar' : 'Rejeitar'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <p className="text-body text-text">{summary}</p>
        {approve && (
          <Alert tone="info">
            Aprovar registra a sua decisão, mas não movimenta estoque: a transferência continua sendo feita no ERP. Até ela aparecer no
            arquivo de transferências, as próximas análises contam essas unidades como "a caminho".
          </Alert>
        )}
        <TextField label="Observação (opcional)" value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
      </div>
    </Modal>
  )
}

function SuggestionDetail({ suggestion: s }: { suggestion: Suggestion }) {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={priorityInfo[s.priority].tone}>Prioridade {priorityInfo[s.priority].label.toLowerCase()}</Badge>
        <Badge tone={statusInfo[s.status].tone}>{statusInfo[s.status].label}</Badge>
        {s.destinationNegative && <Badge tone="warning">Confirme o estoque físico do destino</Badge>}
      </div>
      <p className="text-subtitle text-text">
        {s.productDescription} <span className="font-mono text-body text-text-muted">{s.productCode}</span>
      </p>
      <p className="text-body text-text">
        Enviar <strong>{formatNumber(s.quantity)} un.</strong> de <strong>{s.originCode} {s.originName}</strong> para{' '}
        <strong>{s.destinationCode} {s.destinationName}</strong>.
      </p>
      <Alert tone="info" title="Motivo">
        {s.reason}
      </Alert>
      <Table>
        <caption className="sr-only">Dados usados na sugestão</caption>
        <thead>
          <tr>
            <Th />
            <Th className="text-right">Estoque no ERP</Th>
            <Th className="text-right">Vende por dia</Th>
            <Th className="text-right">Dura hoje</Th>
            <Th className="text-right">Depois</Th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <Td>Origem · {s.originCode}</Td>
            <Td className="text-right tabular-nums">{formatNumber(s.originStock)} un.</Td>
            <Td className="text-right tabular-nums">{formatNumber(s.originDailyAverage)}</Td>
            <Td className="text-right tabular-nums">{s.originCoverageDays === null ? 'não vende' : days(s.originCoverageDays)}</Td>
            <Td className="text-right tabular-nums">{formatNumber(s.originStock - s.quantity)} un.</Td>
          </tr>
          <tr>
            <Td>Destino · {s.destinationCode}</Td>
            <Td className="text-right tabular-nums">{formatNumber(s.destinationStock)} un.</Td>
            <Td className="text-right tabular-nums">{formatNumber(s.destinationDailyAverage)}</Td>
            <Td className="text-right tabular-nums">{days(s.destinationCoverageDays)}</Td>
            <Td className="text-right tabular-nums">{days(s.destinationCoverageAfter)}</Td>
          </tr>
        </tbody>
      </Table>
      <p className="text-caption text-text-subtle">
        "Dura" = quantos dias o estoque aguenta no ritmo de venda dos últimos 12 meses (projetado até {formatDate(s.analysisDate)}).
        Categoria: {s.categoryName}. Marca: {s.brandName}.
      </p>
      {s.decidedAt && (
        <p className="text-caption text-text-muted">
          {s.status === 'Completed' ? 'Aprovada' : statusInfo[s.status].label} em {formatDateTime(s.decidedAt)} por {s.decidedByEmail}
          {s.decisionNote && ` · "${s.decisionNote}"`}
        </p>
      )}
      {s.status === 'Completed' && s.completedOn && (
        <Alert tone="success" title="Realizada">
          A saída apareceu no arquivo de transferências em {formatDate(s.completedOn)}
          {s.completedQuantity !== null && `, com ${formatNumber(s.completedQuantity)} un.`}
        </Alert>
      )}
      {s.status === 'Approved' && (
        <Alert tone="warning" title="Aguardando a transferência">
          Aprovada, mas a saída ainda não apareceu no arquivo de transferências importado. Enquanto isso, as análises contam essas
          unidades como "a caminho".
        </Alert>
      )}
    </div>
  )
}
