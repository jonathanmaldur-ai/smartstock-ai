import { ArrowDown, ArrowUp, Download, Minus, Printer } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Alert } from '@/components/ui/Feedback'
import { SelectField } from '@/components/ui/Field'
import { Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ApiError, apiDownload } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatDate, formatDateTime, formatNumber } from '@/lib/format'
import { MONTHLY_KEY, monthlyQuery, useMonthlyReport, type AnalysisRef, type MonthlyReport, type StoreMetrics } from './reportsApi'

export const MONTHLY_TITLE = 'Relatório mensal'
export const MONTHLY_DESCRIPTION =
  'A análise atual comparada com uma anterior: indicadores da rede, lojas e o que aconteceu com as transferências aprovadas.'

const analysisLabel = (a: AnalysisRef) => `Estoque de ${formatDate(a.stockDate)} (análise de ${formatDateTime(a.createdAt)})`

/** Painel do relatório mensal na central de relatórios (Fase 4.2). */
export function MonthlyReportPanel() {
  const toast = useToast()
  const [compareWith, setCompareWith] = useState<string | null>(null)
  const { data, isPending, isError, error, refetch, isFetching } = useMonthlyReport(true, compareWith)
  const [downloading, setDownloading] = useState(false)

  const download = async () => {
    setDownloading(true)
    try {
      await apiDownload(`/reports/${MONTHLY_KEY}/excel?${monthlyQuery(compareWith)}`, 'relatorio-mensal.xlsx')
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível gerar o arquivo.', 'error')
    } finally {
      setDownloading(false)
    }
  }

  const print = () => window.open(`/relatorios/imprimir?relatorio=${MONTHLY_KEY}&${monthlyQuery(compareWith)}`, '_blank', 'noopener')

  return (
    <Card>
      <CardHeader
        title={MONTHLY_TITLE}
        description={MONTHLY_DESCRIPTION}
        actions={
          <>
            <Button variant="secondary" size="sm" icon={<Download aria-hidden className="size-4" />} loading={downloading} onClick={() => void download()}>
              Excel
            </Button>
            <Button variant="secondary" size="sm" icon={<Printer aria-hidden className="size-4" />} onClick={print} disabled={!data}>
              Imprimir / PDF
            </Button>
          </>
        }
      />
      {data && data.options.length > 0 && (
        <div className="border-b border-border p-4 tablet:px-6">
          <div className="tablet:max-w-md">
            <SelectField
              label="Comparar com"
              value={compareWith ?? data.previous?.id ?? ''}
              options={data.options.map((o) => ({ value: o.id, label: analysisLabel(o) }))}
              onChange={(e) => setCompareWith(e.target.value || null)}
            />
          </div>
        </div>
      )}
      {isPending ? (
        <TableLoading />
      ) : isError ? (
        error instanceof ApiError && error.status === 409 ? (
          <div className="p-6">
            <Alert tone="info">{error.message}</Alert>
          </div>
        ) : (
          <TableError what="o relatório" onRetry={() => void refetch()} />
        )
      ) : (
        <div className={cn('p-4 tablet:p-6', isFetching && 'opacity-60')}>
          <MonthlyReportContent report={data} />
        </div>
      )}
    </Card>
  )
}

/** Conteúdo do relatório mensal: o mesmo na tela e na impressão. */
export function MonthlyReportContent({ report, compact = false }: { report: MonthlyReport; compact?: boolean }) {
  const cell = compact ? 'px-2 py-1' : undefined
  const { transfers } = report
  return (
    <div className="flex flex-col gap-6">
      <p className="text-caption text-text-muted">
        {report.previous
          ? `Comparando o estoque de ${formatDate(report.previous.stockDate)} com o de ${formatDate(report.current.stockDate)}.`
          : `Estoque de ${formatDate(report.current.stockDate)}. Ainda não há análise anterior para comparar; a partir da próxima importação o relatório mostra a evolução.`}
      </p>

      <section className="flex flex-col gap-2">
        <h3 className="text-body font-semibold">Indicadores da rede</h3>
        <Table className={cn(compact && 'text-caption')}>
          <thead>
            <tr>
              <Th className={cell}>Indicador</Th>
              <Th className={cn('text-right', cell)}>Antes</Th>
              <Th className={cn('text-right', cell)}>Agora</Th>
              <Th className={cn('text-right', cell)}>Variação</Th>
            </tr>
          </thead>
          <tbody>
            {report.indicators.map((i) => (
              <tr key={i.name}>
                <Td className={cell}>{i.name}</Td>
                <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>
                  {i.previous === null ? '—' : `${formatNumber(i.previous, 0)} ${i.unit}`}
                </Td>
                <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>{`${formatNumber(i.current, 0)} ${i.unit}`}</Td>
                <Td className={cn('text-right', cell)}>
                  <Delta current={i.current} previous={i.previous} lowerIsBetter={i.lowerIsBetter} />
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="text-body font-semibold">Por loja</h3>
        <p className="text-caption text-text-muted">Valor de agora e, ao lado, quanto mudou desde a análise anterior.</p>
        <Table className={cn(compact && 'text-caption')}>
          <thead>
            <tr>
              <Th className={cell}>Loja</Th>
              <Th className={cn('text-right', cell)}>Rupturas que importam</Th>
              <Th className={cn('text-right', cell)}>Negativos</Th>
              <Th className={cn('text-right', cell)}>Excesso</Th>
              <Th className={cn('text-right', cell)}>Parados</Th>
              <Th className={cn('text-right', cell)}>Estoque (un.)</Th>
              <Th className={cn('text-right', cell)}>Cobertura (dias)</Th>
            </tr>
          </thead>
          <tbody>
            {report.stores.map((s) => (
              <tr key={s.code}>
                <Td className={cn('whitespace-nowrap', cell)}>{`${s.code} ${s.name}`}</Td>
                <StoreCell store={s} pick={(m) => m.relevantRuptures} lowerIsBetter className={cell} />
                <StoreCell store={s} pick={(m) => m.negativeItems} lowerIsBetter className={cell} />
                <StoreCell store={s} pick={(m) => m.excess} lowerIsBetter className={cell} />
                <StoreCell store={s} pick={(m) => m.stagnant} lowerIsBetter className={cell} />
                <StoreCell store={s} pick={(m) => m.stockUnits} lowerIsBetter={null} className={cell} />
                <StoreCell store={s} pick={(m) => m.coverageDays} lowerIsBetter={null} className={cell} />
              </tr>
            ))}
          </tbody>
        </Table>
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="text-body font-semibold">Transferências</h3>
        <p className="text-caption text-text-muted">
          {transfers.since
            ? `Sugestões aprovadas ou rejeitadas desde ${formatDateTime(transfers.since)}.`
            : 'Todas as sugestões já aprovadas ou rejeitadas.'}{' '}
          "Realizada" é a aprovada que apareceu no arquivo de transferências do ERP.
        </p>
        <dl className="grid gap-3 tablet:grid-cols-2 notebook:grid-cols-3">
          <Figure label="Aprovadas" value={`${formatNumber(transfers.approved, 0)} (${formatNumber(transfers.approvedUnits, 0)} un.)`} />
          <Figure label="Realizadas" value={`${formatNumber(transfers.completed, 0)} (${formatNumber(transfers.completedUnits, 0)} un.)`} />
          <Figure
            label="Precisão"
            value={transfers.precisionPercent === null ? '—' : `${formatNumber(transfers.precisionPercent, 1)}%`}
            hint="Das aprovadas, quantas foram realizadas"
          />
          <Figure
            label="Tempo até realizar"
            value={transfers.averageDaysToComplete === null ? '—' : `${formatNumber(transfers.averageDaysToComplete, 1)} dias`}
            hint="Da aprovação até aparecer no ERP"
          />
          <Figure label="Rejeitadas" value={formatNumber(transfers.rejected, 0)} />
          <Figure label="Aguardando aprovação agora" value={formatNumber(transfers.pendingApproval, 0)} />
        </dl>
      </section>
    </div>
  )
}

function StoreCell({
  store,
  pick,
  lowerIsBetter,
  className,
}: {
  store: { current: StoreMetrics; previous: StoreMetrics | null }
  pick: (m: StoreMetrics) => number | null
  lowerIsBetter: boolean | null
  className?: string
}) {
  const current = pick(store.current)
  const previous = store.previous ? pick(store.previous) : null
  return (
    <Td className={cn('whitespace-nowrap text-right tabular-nums', className)}>
      {current === null ? '—' : formatNumber(current, 0)}
      {current !== null && previous !== null && (
        <span className="ml-2">
          <Delta current={current} previous={previous} lowerIsBetter={lowerIsBetter} />
        </span>
      )}
    </Td>
  )
}

/** Variação com seta e sinal, nunca só a cor: verde quando melhorou, vermelho quando piorou, neutra quando não há "melhor". */
export function Delta({ current, previous, lowerIsBetter }: { current: number; previous: number | null; lowerIsBetter: boolean | null }) {
  if (previous === null) return <span className="text-text-subtle">—</span>
  const diff = current - previous
  if (diff === 0)
    return (
      <span className="inline-flex items-center gap-1 text-caption text-text-subtle">
        <Minus aria-hidden className="size-3" />0
      </span>
    )
  const better = lowerIsBetter === null ? null : lowerIsBetter ? diff < 0 : diff > 0
  const Icon = diff > 0 ? ArrowUp : ArrowDown
  return (
    <span
      className={cn('inline-flex items-center gap-1 text-caption tabular-nums', better === null ? 'text-text-muted' : better ? 'text-success' : 'text-error')}
      title={better === null ? undefined : better ? 'Melhorou' : 'Piorou'}
    >
      <Icon aria-hidden className="size-3" />
      {`${diff > 0 ? '+' : '−'}${formatNumber(Math.abs(diff), 0)}`}
    </span>
  )
}

function Figure({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-md border border-border p-3">
      <dt className="text-caption text-text-muted">{label}</dt>
      <dd className="font-display text-heading tabular-nums">{value}</dd>
      {hint && <dd className="text-caption text-text-subtle">{hint}</dd>}
    </div>
  )
}
