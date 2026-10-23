import { CalendarDays, Download } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { ChartCard, LineChart } from '@/components/ui/Chart'
import { Alert, EmptyState } from '@/components/ui/Feedback'
import { TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { ApiError, apiDownload } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatDate, formatNumber } from '@/lib/format'
import { toQuery } from '@/lib/query'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { QUANTITY_KEY, useQuantityProductDays, useQuantitySales, type DailySalesFilters, type QuantitySalesReport } from './reportsApi'
import { Delta, Figure, SalesFilters } from './SalesShared'

export const QUANTITY_TITLE = 'Vendas em quantidade'
export const QUANTITY_DESCRIPTION =
  'Quantas peças cada loja vendeu dia a dia, os produtos mais vendidos e a comparação com o período anterior. Só quantidades, sem valores.'

/** Vendas em quantidade (decisão 62): o relatório de vendas de Gerente, Operador e Consulta, sem nenhum valor em R$. */
export function QuantitySalesPanel() {
  const toast = useToast()
  const [filters, setFilters] = useState<DailySalesFilters>({})
  const { data, isPending, isError, refetch, isFetching } = useQuantitySales(filters)
  const [downloading, setDownloading] = useState(false)

  const download = async () => {
    setDownloading(true)
    try {
      await apiDownload(`/reports/${QUANTITY_KEY}/excel?${toQuery(filters)}`, 'vendas-em-quantidade.xlsx')
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível gerar o arquivo.', 'error')
    } finally {
      setDownloading(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader
          title={QUANTITY_TITLE}
          description={QUANTITY_DESCRIPTION}
          actions={
            <Button variant="secondary" size="sm" icon={<Download aria-hidden className="size-4" />} loading={downloading} onClick={() => void download()}>
              Excel
            </Button>
          }
        />
        <SalesFilters filters={filters} onChange={setFilters} last={data?.lastAvailable} from={data?.from} to={data?.to} />
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="as vendas em quantidade" onRetry={() => void refetch()} />
        ) : !data.from ? (
          <EmptyState icon={CalendarDays} title="Nenhuma venda por dia importada" description="As vendas aparecem aqui quando o TI importar o relatório de vendas do ERP." />
        ) : (
          <div className={cn('flex flex-col gap-6 p-4 tablet:p-6', isFetching && 'opacity-60')}>
            {filters.ate && data.to && filters.ate > data.to && (
              <Alert tone="info">Há vendas importadas até {formatDate(data.to)}: o período vai até esse dia.</Alert>
            )}
            <QuantityContent report={data} />
          </div>
        )}
      </Card>
      {data?.from && <QuantityProductDaysSection filters={filters} />}
    </div>
  )
}

function QuantityContent({ report }: { report: QuantitySalesReport }) {
  const { current, previous } = report
  const [productId, setProductId] = useState<number | null>(null)
  const [search, setSearch] = useState('')
  const term = search.trim().toLowerCase()
  const products = report.products.items.filter(
    (p) => !term || p.code.includes(term) || p.description.toLowerCase().includes(term) || p.brand.toLowerCase().includes(term),
  )

  return (
    <>
      <p className="text-caption text-text-muted">
        {report.scope} · {formatDate(report.from!)} a {formatDate(report.to!)}
        {report.previousFrom && previous
          ? ` · comparado com ${formatDate(report.previousFrom)} a ${formatDate(report.previousTo!)}`
          : ' · sem vendas importadas no período anterior para comparar'}
      </p>

      <dl className="grid gap-3 tablet:grid-cols-3">
        <Figure label="Peças vendidas" value={formatNumber(current.pieces, 0)} delta={<Delta now={current.pieces} before={previous?.pieces} />} />
        <Figure
          label="Média de peças por dia"
          value={current.piecesPerDay === null ? '—' : formatNumber(current.piecesPerDay, 0)}
          delta={<Delta now={current.piecesPerDay} before={previous?.piecesPerDay} />}
        />
        <Figure label="Dias com venda" value={formatNumber(current.days, 0)} delta={null} />
      </dl>

      <ChartCard
        title="Peças vendidas por dia"
        description={report.scope === 'Rede toda' ? 'Rede toda: soma das lojas em cada dia.' : `${report.scope}: peças vendidas em cada dia.`}
        rows={report.days}
        exportName="pecas-por-dia"
        columns={[
          { header: 'Data', value: (d) => formatDate(d.date) },
          { header: 'Peças', value: (d) => d.pieces },
        ]}
      >
        {(large) => (
          <LineChart
            height={large ? 240 : 140}
            format={(v) => formatNumber(v, 0)}
            singlePointHint="Escolha um período com mais de um dia para ver a linha."
            points={report.days.map((d) => ({ label: formatDate(d.date), value: d.pieces }))}
          />
        )}
      </ChartCard>

      <section className="flex flex-col gap-2">
        <h3 className="text-body font-semibold">Por loja</h3>
        <Table>
          <thead>
            <tr>
              <Th>Loja</Th>
              <Th className="text-right">Peças vendidas</Th>
              <Th className="text-right">Período anterior</Th>
              <Th className="text-right">Melhor dia</Th>
            </tr>
          </thead>
          <tbody>
            {report.stores.map((s) => (
              <tr key={s.storeId}>
                <Td className="whitespace-nowrap">{`${s.code} ${s.name}`}</Td>
                <Td className="text-right tabular-nums">{formatNumber(s.pieces, 0)}</Td>
                <Td className="whitespace-nowrap text-right">
                  <Delta now={s.pieces} before={s.previousPieces} />
                </Td>
                <Td className="whitespace-nowrap text-right tabular-nums">
                  {s.bestDay ? `${formatDate(s.bestDay)} · ${formatNumber(s.bestDayPieces ?? 0, 0)} peças` : '—'}
                </Td>
              </tr>
            ))}
          </tbody>
        </Table>
      </section>

      <section className="flex flex-col gap-2">
        <h3 className="text-body font-semibold">Produtos mais vendidos</h3>
        {!report.products.available ? (
          <Alert tone="info">Os produtos aparecem quando as vendas do período vêm do relatório detalhado por itens do ERP.</Alert>
        ) : (
          <>
            <p className="text-caption text-text-muted">
              {`${report.products.total.toLocaleString('pt-BR')} produtos vendidos no período. `}
              {report.products.total > report.products.items.length &&
                `Mostrando os ${report.products.items.length.toLocaleString('pt-BR')} que mais venderam em quantidade; o Excel traz todos.`}
            </p>
            <div className="tablet:max-w-sm">
              <TextField label="Buscar produto" type="search" placeholder="Código, descrição ou marca" value={search} onChange={(e) => setSearch(e.target.value)} />
            </div>
            <Table>
              <thead>
                <tr>
                  <Th className="text-right">#</Th>
                  <Th>Produto</Th>
                  <Th className="text-right">Quantidade</Th>
                  <Th className="text-right">Dias</Th>
                  <Th className="text-right">Lojas</Th>
                  <Th className="text-right">Ações</Th>
                </tr>
              </thead>
              <tbody>
                {products.map((p) => (
                  <tr key={p.productId}>
                    <Td className="text-right tabular-nums text-text-subtle">{report.products.items.indexOf(p) + 1}</Td>
                    <Td className="min-w-64">
                      <ProductName description={p.description} code={p.code} brand={p.brand} />
                    </Td>
                    <Td className="text-right tabular-nums">{formatNumber(p.quantity)}</Td>
                    <Td className="text-right tabular-nums">{p.days}</Td>
                    <Td className="text-right tabular-nums">{p.stores}</Td>
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => setProductId(p.productId)} aria-label={`Ver ficha de ${p.description}`}>
                        Ficha
                      </Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            {products.length === 0 && <p className="text-caption text-text-muted">Nenhum produto com essa busca.</p>}
          </>
        )}
      </section>
      <ProductModal productId={productId} onClose={() => setProductId(null)} />
    </>
  )
}

/** O que cada loja vendeu em cada dia, em quantidade: uma linha por dia, loja e produto. */
function QuantityProductDaysSection({ filters }: { filters: DailySalesFilters }) {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [productId, setProductId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search)
  const { data, isPending, isError, refetch, isFetching } = useQuantityProductDays({ ...filters, search: debouncedSearch || undefined, page })
  const showStore = filters.storeId === undefined

  return (
    <Card>
      <CardHeader
        title="Produtos vendidos por dia"
        description="O que cada loja vendeu em cada dia, em quantidade, do dia mais recente para o mais antigo. O Excel traz tudo, na aba Produtos por dia."
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
        <EmptyState icon={CalendarDays} title="Nenhum produto no período" description="Os produtos aparecem quando as vendas vêm do relatório detalhado por itens do ERP." />
      ) : (
        <div className={cn(isFetching && 'opacity-60')}>
          <Table>
            <caption className="sr-only">Produtos vendidos por dia, em quantidade</caption>
            <thead>
              <tr>
                <Th>Data</Th>
                {showStore && <Th>Loja</Th>}
                <Th>Produto</Th>
                <Th className="text-right">Quantidade</Th>
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
                      <ProductName description={row.description} code={row.code} brand={row.brand} />
                    </Td>
                    <Td className="text-right tabular-nums">{formatNumber(row.quantity)}</Td>
                    <Td className="text-right">
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
      <ProductModal productId={productId} onClose={() => setProductId(null)} />
    </Card>
  )
}

function ProductName({ description, code, brand }: { description: string; code: string; brand: string }) {
  return (
    <span className="flex flex-col">
      <span>{description}</span>
      <span className="text-caption text-text-subtle">
        <span className="font-mono">{code}</span> · {brand}
      </span>
    </span>
  )
}

function ProductModal({ productId, onClose }: { productId: number | null; onClose: () => void }) {
  return (
    <Modal open={productId !== null} onClose={onClose} title="Ficha do produto" size="lg">
      {productId !== null && <ProductOverviewView productId={productId} />}
    </Modal>
  )
}
