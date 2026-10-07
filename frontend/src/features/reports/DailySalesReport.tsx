import { ArrowDown, ArrowUp, CalendarDays, Download, Minus, Printer } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { BarChart, ChartCard, LineChart } from '@/components/ui/Chart'
import { Alert, EmptyState } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { useStores } from '@/features/catalog/catalogApi'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { ApiError, apiDownload } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatDate, formatMoney, formatNumber } from '@/lib/format'
import { toQuery } from '@/lib/query'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { DAILY_KEY, useDailySales, useProductDays, useSaleLines, type DailySalesFilters, type DailySalesReport } from './reportsApi'

export const DAILY_TITLE = 'Vendas por dia'
export const DAILY_DESCRIPTION =
  'Quanto cada loja vendeu dia a dia: valor, vendas, ticket médio, produtos mais vendidos, comparação com o período anterior e o melhor dia da semana.'

const presets = [7, 30, 90] as const

/** Dias mínimos do período para mostrar a média por dia da semana (duas semanas). */
const MIN_WEEKDAY_PERIOD = 14

/** "2026-09-30" menos n dias, sem fuso. */
function minusDays(iso: string, days: number) {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() - days)
  return date.toISOString().slice(0, 10)
}

/** Painel de vendas por dia na central de relatórios (decisão 51). */
export function DailySalesPanel() {
  const toast = useToast()
  const stores = useStores()
  const [filters, setFilters] = useState<DailySalesFilters>({})
  const { data, isPending, isError, refetch, isFetching } = useDailySales(filters)
  const [downloading, setDownloading] = useState(false)
  const last = data?.lastAvailable

  const download = async () => {
    setDownloading(true)
    try {
      await apiDownload(`/reports/${DAILY_KEY}/excel?${toQuery(filters)}`, 'vendas-por-dia.xlsx')
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível gerar o arquivo.', 'error')
    } finally {
      setDownloading(false)
    }
  }

  const print = () => window.open(`/relatorios/imprimir?relatorio=${DAILY_KEY}&${toQuery(filters)}`, '_blank', 'noopener')
  const preset = (days: number) => last && setFilters({ ...filters, de: minusDays(last, days - 1), ate: last })
  const activePreset = presets.find((d) => last && filters.ate === last && filters.de === minusDays(last, d - 1))

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader
          title={DAILY_TITLE}
          description={DAILY_DESCRIPTION}
          actions={
            <>
              <Button variant="secondary" size="sm" icon={<Download aria-hidden className="size-4" />} loading={downloading} onClick={() => void download()}>
                Excel
              </Button>
              <Button variant="secondary" size="sm" icon={<Printer aria-hidden className="size-4" />} onClick={print} disabled={!data?.from}>
                Imprimir / PDF
              </Button>
            </>
          }
        />
        <div className="flex flex-col gap-4 border-b border-border p-4 tablet:px-6">
          <div className="flex flex-wrap gap-2" role="group" aria-label="Período rápido">
            {presets.map((days) => (
              <Button
                key={days}
                size="sm"
                variant={activePreset === days || (days === 30 && !filters.de && !filters.ate) ? 'primary' : 'secondary'}
                disabled={!last}
                onClick={() => preset(days)}
              >
                Últimos {days} dias
              </Button>
            ))}
          </div>
          <div className="grid gap-4 tablet:grid-cols-3">
            <TextField label="De" type="date" value={filters.de ?? data?.from ?? ''} onChange={(e) => setFilters({ ...filters, de: e.target.value || undefined })} />
            <TextField label="Até" type="date" value={filters.ate ?? data?.to ?? ''} onChange={(e) => setFilters({ ...filters, ate: e.target.value || undefined })} />
            <SelectField
              label="Loja"
              value={filters.storeId ?? ''}
              options={[
                { value: '', label: 'Rede toda' },
                ...(stores.data ?? []).filter((s) => s.status === 'Active').map((s) => ({ value: String(s.id), label: `${s.code} ${s.name}` })),
              ]}
              onChange={(e) => setFilters({ ...filters, storeId: e.target.value ? Number(e.target.value) : undefined })}
            />
          </div>
        </div>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="as vendas por dia" onRetry={() => void refetch()} />
        ) : !data.from ? (
          <EmptyState
            icon={CalendarDays}
            title="Nenhuma venda por dia importada"
            description="Exporte do ERP o relatório de vendas de cada loja e solte na pasta Enviar Dados\Vendas loja por dia."
          />
        ) : (
          <div className={cn('flex flex-col gap-4 p-4 tablet:p-6', isFetching && 'opacity-60')}>
            {filters.ate && data.to && filters.ate > data.to && (
              <Alert tone="info">
                Há vendas importadas até {formatDate(data.to)}: o período vai até esse dia, e a comparação usa o mesmo número de dias
                antes dele.
              </Alert>
            )}
            <DailySalesContent report={data} storeId={filters.storeId} />
          </div>
        )}
      </Card>
      {data?.from && <ProductDaysSection filters={filters} />}
    </div>
  )
}

/** Conteúdo do relatório: o mesmo na tela e na impressão (sem os botões dos gráficos). */
export function DailySalesContent({ report, compact = false, storeId }: { report: DailySalesReport; compact?: boolean; storeId?: number }) {
  const cell = compact ? 'px-2 py-1' : undefined
  const { current, previous } = report
  const dailyRows = report.days.map((d) => ({ ...d, ticket: d.sales ? d.net / d.sales : null }))
  const weekdays = report.weekdays.filter((w) => w.days > 0)
  // Média por dia da semana só faz sentido com pelo menos duas ocorrências de cada dia.
  const periodDays = (Date.parse(report.to!) - Date.parse(report.from!)) / 86_400_000 + 1
  const showWeekdays = periodDays >= MIN_WEEKDAY_PERIOD
  const bestWeekday = weekdays.length ? weekdays.reduce((a, b) => (b.averageNet > a.averageNet ? b : a)) : null
  const worstWeekday = weekdays.length ? weekdays.reduce((a, b) => (b.averageNet < a.averageNet ? b : a)) : null

  const dailyChart = (large: boolean) => (
    <LineChart
      height={large ? 240 : 140}
      format={(v) => formatMoney(v, 0)}
      singlePointHint="Escolha um período com mais de um dia para ver a linha."
      points={report.days.map((d) => ({ label: formatDate(d.date), value: d.net }))}
    />
  )
  const weekdayChart = () => (
    <BarChart
      format={(v) => formatMoney(v, 0)}
      data={report.weekdays.map((w) => ({
        key: String(w.dayOfWeek),
        label: w.label,
        value: w.averageNet,
        detail: w.days ? `${formatNumber(w.averageSales, 1)} vendas em média · ${w.days} dia(s)` : 'Sem venda no período',
      }))}
    />
  )

  return (
    <div className="flex flex-col gap-6">
      <p className="text-caption text-text-muted">
        {report.scope} · {formatDate(report.from!)} a {formatDate(report.to!)}
        {report.previousFrom && previous
          ? ` · comparado com ${formatDate(report.previousFrom)} a ${formatDate(report.previousTo!)}`
          : ' · sem vendas importadas no período anterior para comparar'}
        {` · vendas importadas de ${formatDate(report.firstAvailable!)} a ${formatDate(report.lastAvailable!)}`}
      </p>

      <dl className="grid gap-3 tablet:grid-cols-3 notebook:grid-cols-5">
        <Figure label="Valor vendido" value={formatMoney(current.net, 0)} delta={<Delta now={current.net} before={previous?.net} />} />
        <Figure label="Vendas (cupons)" value={formatNumber(current.sales, 0)} delta={<Delta now={current.sales} before={previous?.sales} />} />
        <Figure
          label="Ticket médio"
          value={current.averageTicket === null ? '—' : formatMoney(current.averageTicket)}
          delta={<Delta now={current.averageTicket} before={previous?.averageTicket} />}
        />
        <Figure
          label="Peças por venda"
          value={current.piecesPerSale === null ? '—' : formatNumber(current.piecesPerSale, 2)}
          delta={<Delta now={current.piecesPerSale} before={previous?.piecesPerSale} />}
        />
        <Figure
          label="Média por dia"
          value={current.netPerDay === null ? '—' : formatMoney(current.netPerDay, 0)}
          delta={<Delta now={current.netPerDay} before={previous?.netPerDay} />}
          hint={`${current.days} dia(s) com venda`}
        />
      </dl>

      {compact ? (
        <section className="flex flex-col gap-2">
          <h3 className="text-body font-semibold">Valor vendido por dia</h3>
          {dailyChart(false)}
        </section>
      ) : (
        <ChartCard
          title="Valor vendido por dia"
          description={report.scope === 'Rede toda' ? 'Rede toda: soma das lojas em cada dia.' : `${report.scope}: valor vendido em cada dia.`}
          rows={dailyRows}
          exportName="vendas-por-dia"
          columns={[
            { header: 'Data', value: (d) => formatDate(d.date) },
            { header: 'Valor vendido (R$)', value: (d) => Math.round(d.net * 100) / 100 },
            { header: 'Vendas', value: (d) => d.sales },
            { header: 'Peças', value: (d) => d.pieces },
            { header: 'Ticket médio (R$)', value: (d) => (d.ticket === null ? null : Math.round(d.ticket * 100) / 100) },
          ]}
        >
          {dailyChart}
        </ChartCard>
      )}

      <section className="flex flex-col gap-2">
        <h3 className="text-body font-semibold">Por loja</h3>
        <Table className={cn(compact && 'text-caption')}>
          <thead>
            <tr>
              <Th className={cell}>Loja</Th>
              <Th className={cn('text-right', cell)}>Valor vendido</Th>
              <Th className={cn('text-right', cell)}>Período anterior</Th>
              <Th className={cn('text-right', cell)}>Vendas</Th>
              <Th className={cn('text-right', cell)}>Ticket médio</Th>
              <Th className={cn('text-right', cell)}>Melhor dia</Th>
            </tr>
          </thead>
          <tbody>
            {report.stores.map((s) => (
              <tr key={s.storeId}>
                <Td className={cn('whitespace-nowrap', cell)}>{`${s.code} ${s.name}`}</Td>
                <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>{formatMoney(s.net, 0)}</Td>
                <Td className={cn('whitespace-nowrap text-right', cell)}>
                  <Delta now={s.net} before={s.previousNet} />
                </Td>
                <Td className={cn('text-right tabular-nums', cell)}>{formatNumber(s.sales, 0)}</Td>
                <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>{s.averageTicket === null ? '—' : formatMoney(s.averageTicket)}</Td>
                <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>
                  {s.bestDay ? `${formatDate(s.bestDay)} · ${formatMoney(s.bestDayNet ?? 0, 0)}` : '—'}
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      </section>

      <ProductsSection report={report} compact={compact} storeId={storeId} />

      {!showWeekdays ? null : compact ? (
        <section className="flex flex-col gap-2">
          <h3 className="text-body font-semibold">Média vendida por dia da semana</h3>
          {weekdayChart()}
        </section>
      ) : (
        <ChartCard
          title="Média vendida por dia da semana"
          description={
            bestWeekday && worstWeekday
              ? `Melhor dia: ${bestWeekday.label} (${formatMoney(bestWeekday.averageNet, 0)} em média). Mais fraco: ${worstWeekday.label}.`
              : undefined
          }
          rows={report.weekdays}
          exportName="vendas-dia-da-semana"
          columns={[
            { header: 'Dia da semana', value: (w) => w.label },
            { header: 'Média vendida (R$)', value: (w) => w.averageNet },
            { header: 'Média de vendas', value: (w) => w.averageSales },
            { header: 'Dias no período', value: (w) => w.days },
          ]}
        >
          {weekdayChart}
        </ChartCard>
      )}
    </div>
  )
}

/** Variação percentual com seta e sinal, nunca só a cor: verde subiu, vermelho caiu. */
function Delta({ now, before }: { now: number | null; before: number | null | undefined }) {
  if (now === null || before === null || before === undefined || before === 0) return <span className="text-caption text-text-subtle">—</span>
  const change = ((now - before) / before) * 100
  if (Math.abs(change) < 0.05)
    return (
      <span className="inline-flex items-center gap-1 text-caption text-text-subtle">
        <Minus aria-hidden className="size-3" />
        igual
      </span>
    )
  const up = change > 0
  const Icon = up ? ArrowUp : ArrowDown
  return (
    <span className={cn('inline-flex items-center gap-1 text-caption tabular-nums', up ? 'text-success' : 'text-error')}>
      <Icon aria-hidden className="size-3" />
      {`${up ? '+' : '−'}${formatNumber(Math.abs(change), 1)}%`}
    </span>
  )
}

function Figure({ label, value, delta, hint }: { label: string; value: string; delta: ReactNode; hint?: string }) {
  return (
    <div className="flex flex-col gap-1 rounded-md border border-border p-3">
      <dt className="text-caption text-text-muted">{label}</dt>
      <dd className="font-display text-heading tabular-nums">{value}</dd>
      <dd className="flex flex-wrap items-center gap-2 text-caption text-text-subtle">
        {delta}
        {hint && <span>{hint}</span>}
      </dd>
    </div>
  )
}

/** Produtos que mais venderam no período (relatório detalhado por itens), com busca e ficha do produto. */
function ProductsSection({ report, compact, storeId }: { report: DailySalesReport; compact: boolean; storeId?: number }) {
  const [search, setSearch] = useState('')
  const [productId, setProductId] = useState<number | null>(null)
  const [sales, setSales] = useState<SalesQuery | null>(null)
  const { products } = report
  const cell = compact ? 'px-2 py-1' : undefined
  const term = search.trim().toLowerCase()
  const items = (compact ? products.items.slice(0, 30) : products.items).filter(
    (p) => !term || p.code.includes(term) || p.description.toLowerCase().includes(term) || p.brand.toLowerCase().includes(term),
  )

  return (
    <section className="flex flex-col gap-2">
      <h3 className="text-body font-semibold">Produtos mais vendidos</h3>
      {!products.available ? (
        <Alert tone="info">
          Os produtos aparecem quando as vendas do período vêm do relatório <strong>Detalhado por itens</strong> do ERP.
        </Alert>
      ) : (
        <>
          <p className="text-caption text-text-muted">
            {`${products.total.toLocaleString('pt-BR')} produtos vendidos no período. `}
            {products.total > products.items.length &&
              `Mostrando os ${(compact ? Math.min(30, products.items.length) : products.items.length).toLocaleString('pt-BR')} que mais venderam em valor; o Excel traz todos.`}
          </p>
          {!compact && (
            <div className="tablet:max-w-sm">
              <TextField label="Buscar produto" type="search" placeholder="Código, descrição ou marca" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
          )}
          <Table className={cn(compact && 'text-caption')}>
            <thead>
              <tr>
                <Th className={cn('text-right', cell)}>#</Th>
                <Th className={cell}>Produto</Th>
                <Th className={cn('text-right', cell)}>Quantidade</Th>
                <Th className={cn('text-right', cell)}>Preço médio</Th>
                <Th className={cn('text-right', cell)}>Valor</Th>
                <Th className={cn('text-right', cell)}>Dias</Th>
                <Th className={cn('text-right', cell)}>Lojas</Th>
                {!compact && <Th className="text-right">Ações</Th>}
              </tr>
            </thead>
            <tbody>
              {items.map((p) => (
                <tr key={p.productId}>
                  <Td className={cn('text-right tabular-nums text-text-subtle', cell)}>{products.items.indexOf(p) + 1}</Td>
                  <Td className={cn('min-w-64', cell)}>
                    <span className="flex flex-col">
                      <span>{p.description}</span>
                      <span className="text-caption text-text-subtle">
                        <span className="font-mono">{p.code}</span> · {p.brand}
                      </span>
                    </span>
                  </Td>
                  <Td className={cn('text-right tabular-nums', cell)}>{formatNumber(p.quantity)}</Td>
                  <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>{p.quantity ? formatMoney(p.amount / p.quantity) : '—'}</Td>
                  <Td className={cn('whitespace-nowrap text-right tabular-nums', cell)}>{formatMoney(p.amount)}</Td>
                  <Td className={cn('text-right tabular-nums', cell)}>{p.days}</Td>
                  <Td className={cn('text-right tabular-nums', cell)}>{p.stores}</Td>
                  {!compact && (
                    <Td className="whitespace-nowrap text-right">
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={`Ver as vendas de ${p.description}`}
                        onClick={() =>
                          setSales({
                            productId: p.productId,
                            description: p.description,
                            de: report.from!,
                            ate: report.to!,
                            storeId,
                            summary: `${report.scope} · ${formatDate(report.from!)} a ${formatDate(report.to!)} · ${formatNumber(p.quantity)} un. · ${formatMoney(p.amount)}`,
                          })
                        }
                      >
                        Ver vendas
                      </Button>
                      <Button variant="ghost" size="sm" onClick={() => setProductId(p.productId)} aria-label={`Ver ficha de ${p.description}`}>
                        Ficha
                      </Button>
                    </Td>
                  )}
                </tr>
              ))}
            </tbody>
          </Table>
          {items.length === 0 && <p className="text-caption text-text-muted">Nenhum produto com essa busca.</p>}
        </>
      )}
      {!compact && <SalesModal query={sales} onClose={() => setSales(null)} />}
      {!compact && (
        <Modal open={productId !== null} onClose={() => setProductId(null)} title="Ficha do produto" size="lg">
          {productId !== null && <ProductOverviewView productId={productId} />}
        </Modal>
      )}
    </section>
  )
}

/** O que cada loja vendeu em cada dia: uma linha por dia, loja e produto (decisão 52). */
function ProductDaysSection({ filters }: { filters: DailySalesFilters }) {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [sales, setSales] = useState<SalesQuery | null>(null)
  const [productId, setProductId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search)
  const { data, isPending, isError, refetch, isFetching } = useProductDays({ ...filters, search: debouncedSearch || undefined, page })
  const showStore = filters.storeId === undefined

  return (
    <Card>
      <CardHeader
        title="Produtos vendidos por dia"
        description="O que cada loja vendeu em cada dia: quantidade e valor de cada produto, do dia mais recente para o mais antigo. O Excel traz tudo, na aba Produtos por dia."
      />
      <div className="border-b border-border p-4 tablet:px-6">
        <div className="tablet:max-w-sm">
          <TextField
            label="Buscar produto"
            type="search"
            placeholder="Código, descrição, referência ou marca"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
        </div>
      </div>
      {isPending ? (
        <TableLoading />
      ) : isError ? (
        <TableError what="os produtos por dia" onRetry={() => void refetch()} />
      ) : data.totalCount === 0 ? (
        <EmptyState
          icon={CalendarDays}
          title="Nenhum produto no período"
          description="Os produtos aparecem quando as vendas vêm do relatório Detalhado por itens do ERP."
        />
      ) : (
        <div className={cn(isFetching && 'opacity-60')}>
          <Table>
            <caption className="sr-only">Produtos vendidos por dia</caption>
            <thead>
              <tr>
                <Th>Data</Th>
                {showStore && <Th>Loja</Th>}
                <Th>Produto</Th>
                <Th className="text-right">Quantidade</Th>
                <Th className="text-right">Preço médio</Th>
                <Th className="text-right">Valor</Th>
                <Th className="text-right">Vendas</Th>
                <Th className="text-right">Ações</Th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((row, i) => {
                const previous = data.items[i - 1]
                const newGroup = !previous || previous.date !== row.date || previous.storeCode !== row.storeCode
                return (
                  <tr key={`${row.date}-${row.storeCode}-${row.productId}`} className={cn(newGroup && i > 0 && 'border-t-2 border-border-strong')}>
                    <Td className="whitespace-nowrap tabular-nums">{newGroup ? formatDate(row.date) : ''}</Td>
                    {showStore && <Td className="whitespace-nowrap">{newGroup ? `${row.storeCode} ${row.storeName}` : ''}</Td>}
                    <Td className="min-w-64">
                      <span className="flex flex-col">
                        <span>{row.description}</span>
                        <span className="text-caption text-text-subtle">
                          <span className="font-mono">{row.code}</span> · {row.brand}
                        </span>
                      </span>
                    </Td>
                    <Td className="text-right tabular-nums">{formatNumber(row.quantity)}</Td>
                    <Td className="whitespace-nowrap text-right tabular-nums">{row.quantity ? formatMoney(row.amount / row.quantity) : '—'}</Td>
                    <Td className="whitespace-nowrap text-right tabular-nums">{formatMoney(row.amount)}</Td>
                    <Td className="text-right tabular-nums">{row.sales}</Td>
                    <Td className="whitespace-nowrap text-right">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() =>
                          setSales({
                            productId: row.productId,
                            description: row.description,
                            de: row.date,
                            ate: row.date,
                            storeId: row.storeId,
                            summary: `${row.storeCode} ${row.storeName} · ${formatDate(row.date)} · ${formatNumber(row.quantity)} un. em ${row.sales} venda(s) · ${formatMoney(row.amount)}`,
                          })
                        }
                        aria-label={`Ver as vendas de ${row.description} em ${formatDate(row.date)}`}
                      >
                        Ver vendas
                      </Button>
                      <Button variant="ghost" size="sm" onClick={() => setProductId(row.productId)} aria-label={`Ver ficha de ${row.description}`}>
                        Ficha
                      </Button>
                    </Td>
                  </tr>
                )
              })}
            </tbody>
          </Table>
          <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
        </div>
      )}
      <SalesModal query={sales} onClose={() => setSales(null)} />
      <Modal open={productId !== null} onClose={() => setProductId(null)} title="Ficha do produto" size="lg">
        {productId !== null && <ProductOverviewView productId={productId} />}
      </Modal>
    </Card>
  )
}

/** O que abrir na janela de vendas: um produto, num período, numa loja ou na rede (decisão 53). */
export type SalesQuery = { productId: number; description: string; de: string; ate: string; storeId?: number; summary: string }

/** As vendas (cupons) em que o produto saiu, com data e loja quando há mais de um dia ou de uma loja. */
function SalesModal({ query, onClose }: { query: SalesQuery | null; onClose: () => void }) {
  const { data, isPending, isError } = useSaleLines(
    query ? { productId: query.productId, de: query.de, ate: query.ate, storeId: query.storeId } : null,
  )
  const showDate = query !== null && query.de !== query.ate
  const showStore = query?.storeId === undefined

  return (
    <Modal open={query !== null} onClose={onClose} title={query?.description ?? 'Vendas'} description={query?.summary} size="lg">
      {isPending ? (
        <TableLoading rows={4} />
      ) : isError ? (
        <p className="text-body text-error">Não foi possível carregar as vendas.</p>
      ) : (
        <div className="flex flex-col gap-2">
          {data.length >= 500 && <p className="text-caption text-text-muted">Mostrando as 500 primeiras vendas; o Excel traz todas, na aba Itens das vendas.</p>}
          <Table>
            <caption className="sr-only">Vendas do produto</caption>
            <thead>
              <tr>
                {showDate && <Th>Data</Th>}
                {showStore && <Th>Loja</Th>}
                <Th>Venda</Th>
                <Th className="text-right">Quantidade</Th>
                <Th className="text-right">Preço unitário</Th>
                <Th className="text-right">Valor</Th>
                <Th>Cliente</Th>
                <Th>Documento</Th>
                <Th>Pagamento</Th>
                <Th className="text-right">Total da venda</Th>
              </tr>
            </thead>
            <tbody>
              {data.map((s) => (
                <tr key={`${s.storeCode}-${s.saleNumber}`}>
                  {showDate && <Td className="whitespace-nowrap tabular-nums">{formatDate(s.date)}</Td>}
                  {showStore && <Td className="whitespace-nowrap">{`${s.storeCode} ${s.storeName}`}</Td>}
                  <Td className="font-mono">{s.saleNumber}</Td>
                  <Td className="text-right tabular-nums">{formatNumber(s.quantity)}</Td>
                  <Td className="whitespace-nowrap text-right tabular-nums">{formatMoney(s.unitPrice)}</Td>
                  <Td className="whitespace-nowrap text-right tabular-nums">{formatMoney(s.amount)}</Td>
                  <Td>{s.registeredCustomer ? 'Cadastrado' : 'Balcão'}</Td>
                  <Td className="whitespace-nowrap">{s.fiscalDocument ?? '—'}</Td>
                  <Td className="whitespace-nowrap">{s.payment ?? '—'}</Td>
                  <Td className="whitespace-nowrap text-right tabular-nums">
                    {formatMoney(s.saleTotal)}
                    <span className="block text-caption text-text-subtle">{s.saleItems} produto(s)</span>
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      )}
    </Modal>
  )
}
