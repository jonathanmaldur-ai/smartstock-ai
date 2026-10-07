import { FileSpreadsheet } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/layout/AppShell'
import { Card } from '@/components/ui/Card'
import { BarChart, ChartCard, LineChart } from '@/components/ui/Chart'
import { Alert, Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { Modal } from '@/components/ui/Modal'
import { ActionCard } from '@/features/analysis/ActionCard'
import { useAlertSummary } from '@/features/analysis/alertsApi'
import { priorityInfo } from '@/features/analysis/analysisApi'
import { useAuth } from '@/features/auth/useAuth'
import { ProductOverviewView } from '@/features/catalog/ProductOverviewView'
import { formatDate, formatNumber } from '@/lib/format'
import { shortagePercent, useDashboard, type Dashboard, type DataFreshness, type StoreDashboardRow, type TopProduct, type TrendPoint } from './dashboardApi'

/** Dashboard executivo (Módulo 2.4, decisão 39): a primeira tela do dia. */
export function DashboardPage() {
  const { user } = useAuth()
  const firstName = user?.fullName.split(' ')[0]
  const { data, isPending, isError, refetch } = useDashboard()

  return (
    <>
      <PageHeader
        title={`Olá, ${firstName}`}
        description={
          data?.kpis
            ? `Visão geral da rede Dorémi Brinquedos · análise de ${formatDate(data.kpis.analysisDate)} com o estoque de ${formatDate(data.kpis.stockDate)}.`
            : 'Visão geral do estoque da rede Dorémi Brinquedos.'
        }
      />
      {isPending ? (
        <div className="grid gap-4 tablet:grid-cols-3">
          {Array.from({ length: 6 }, (_, i) => (
            <Skeleton key={i} className="h-32" />
          ))}
        </div>
      ) : isError ? (
        <Alert tone="error" title="Não foi possível carregar o dashboard">
          <button type="button" className="underline" onClick={() => void refetch()}>
            Tentar novamente
          </button>
        </Alert>
      ) : (
        <DashboardContent data={data} />
      )}
    </>
  )
}

function DashboardContent({ data }: { data: Dashboard }) {
  const [productId, setProductId] = useState<number | null>(null)

  if (!data.freshness.stockDate)
    return (
      <Card>
        <EmptyState
          icon={FileSpreadsheet}
          title="Nenhum estoque importado ainda"
          description="Importe as planilhas do ERP (marcas, produtos, estoque, vendas e transferências) para ver os indicadores."
          action={
            <Link to="/importacoes" className="text-body text-primary hover:underline">
              Ir para Importações
            </Link>
          }
        />
      </Card>
    )

  return (
    <div className="flex flex-col gap-6">
      <FreshnessAlert freshness={data.freshness} />
      {!data.kpis ? (
        <Alert tone="info" title="Gere a primeira análise">
          Os dados já foram importados. Em <Link to="/estoque" className="underline">Situação do estoque</Link>, clique em "Gerar nova análise"
          para ver os indicadores.
        </Alert>
      ) : (
        <>
          <Kpis kpis={data.kpis} />
          <AlertsStrip />
          <div className="grid gap-6 notebook:grid-cols-2">
            <ShortageChart stores={data.stores} />
            <SalesChart stores={data.stores} />
            <CoverageChart stores={data.stores} />
            <TopProductsChart products={data.topProducts} onOpen={setProductId} />
          </div>
          <Trend trend={data.trend} />
        </>
      )}

      <Modal open={productId !== null} onClose={() => setProductId(null)} title="Ficha do produto" size="lg">
        {productId !== null && <ProductOverviewView productId={productId} />}
      </Modal>
    </div>
  )
}

/** Alertas não vistos por tipo (decisão 47): atalho para a central. */
function AlertsStrip() {
  const { data } = useAlertSummary()
  const types = (data?.types ?? []).filter((t) => t.unseen > 0)
  if (types.length === 0) return null

  return (
    <Card className="flex flex-col gap-3 p-4 tablet:flex-row tablet:items-center tablet:justify-between tablet:px-6">
      <p className="text-label text-text">Alertas não vistos</p>
      <ul className="flex flex-wrap gap-x-5 gap-y-2">
        {types.map((t) => (
          <li key={t.type} className="flex items-center gap-2 text-body">
            <Badge tone={priorityInfo[t.priority].tone}>{priorityInfo[t.priority].label}</Badge>
            <span className="text-text-muted">{t.label}</span>
            <span className="font-medium tabular-nums text-text">{t.unseen.toLocaleString('pt-BR')}</span>
          </li>
        ))}
      </ul>
      <Link to="/alertas" className="shrink-0 text-body text-primary hover:underline">
        Ver alertas
      </Link>
    </Card>
  )
}

/** Decisão 19: estoque com mais de 7 dias, vendas ou transferências com mais de 30. */
function FreshnessAlert({ freshness: f }: { freshness: DataFreshness }) {
  const outdated = [
    f.stockOutdated && `estoque de ${formatDate(f.stockDate!)} (${f.stockAgeDays} dias; o ideal é importar toda semana)`,
    f.salesOutdated && `vendas de ${formatDate(f.salesDate!)} (${f.salesAgeDays} dias)`,
    f.transfersOutdated && `transferências até ${formatDate(f.transfersDate!)} (${f.transfersAgeDays} dias)`,
  ].filter(Boolean)
  if (outdated.length === 0) return null

  return (
    <Alert tone="warning" title="Dados desatualizados">
      Hora de importar: {outdated.join('; ')}. <Link to="/importacoes" className="underline">Ir para Importações</Link>
    </Alert>
  )
}

function Kpis({ kpis }: { kpis: NonNullable<Dashboard['kpis']> }) {
  return (
    <div className="grid gap-4 tablet:grid-cols-2 notebook:grid-cols-3">
      <ActionCard tone="text-text" value={kpis.stockUnits} title="Estoque total da rede" description="Unidades nas lojas e no Depósito" to="/estoque" />
      <ActionCard
        tone="text-text"
        value={kpis.annualTurnover ?? 0}
        display={kpis.annualTurnover === null ? '—' : `${formatNumber(kpis.annualTurnover, 1)}×`}
        title="Giro anual"
        description={`Venda de 12 meses (${formatNumber(kpis.sold12Months, 0)} un.) ÷ estoque`}
      />
      <ActionCard
        tone="text-text"
        value={kpis.networkCoverageDays ?? 0}
        display={kpis.networkCoverageDays === null ? '—' : `${formatNumber(kpis.networkCoverageDays, 0)} dias`}
        title="Cobertura da rede"
        description="Quanto tempo o estoque total dura no ritmo de venda atual"
      />
      <ActionCard tone="text-error" value={kpis.relevantRuptures} title="Rupturas que importam" description="Vendem 12+ por ano e estão sem estoque na loja" to="/estoque" />
      <ActionCard
        tone="text-primary"
        value={kpis.pendingUnits}
        title="Unidades para transferir"
        description={`${kpis.pendingSuggestions.toLocaleString('pt-BR')} sugestões aguardando aprovação`}
        to="/sugestoes"
      />
      <ActionCard tone="text-error" value={kpis.negativeItems} title="Itens negativos" description="Produto × loja com estoque abaixo de zero" to="/negativos" />
    </div>
  )
}

const selling = (stores: StoreDashboardRow[]) => stores.filter((s) => s.type !== 'Warehouse')
const storeLabel = (s: StoreDashboardRow) => `${s.code} ${s.name}`

function ShortageChart({ stores }: { stores: StoreDashboardRow[] }) {
  const rows = selling(stores).toSorted((a, b) => shortagePercent(b) - shortagePercent(a))
  return (
    <ChartCard
      title="Itens em falta por loja"
      description="Dos produtos que vendem na loja, quantos % estão em ruptura ou abaixo do mínimo (15 dias)."
      rows={rows}
      exportName="itens-em-falta-por-loja"
      columns={[
        { header: 'Loja', value: storeLabel },
        { header: '% em falta', value: (s) => Math.round(shortagePercent(s) * 10) / 10 },
        { header: 'Ruptura', value: (s) => s.rupture },
        { header: 'Abaixo do mínimo', value: (s) => s.belowMinimum },
        { header: 'Normal', value: (s) => s.normal },
        { header: 'Excesso', value: (s) => s.excess },
        { header: 'Parado', value: (s) => s.stagnant },
      ]}
    >
      {() => (
        <BarChart
          format={(v) => `${formatNumber(v, 1)}%`}
          data={rows.map((s) => ({
            key: s.code,
            label: storeLabel(s),
            value: shortagePercent(s),
            detail: `${s.rupture.toLocaleString('pt-BR')} em ruptura · ${s.belowMinimum.toLocaleString('pt-BR')} abaixo do mínimo`,
          }))}
        />
      )}
    </ChartCard>
  )
}

function SalesChart({ stores }: { stores: StoreDashboardRow[] }) {
  const rows = selling(stores).toSorted((a, b) => b.sold12Months - a.sold12Months)
  return (
    <ChartCard
      title="Vendas de 12 meses por loja"
      description="Unidades vendidas no período do último relatório de vendas de cada loja."
      rows={rows}
      exportName="vendas-12-meses-por-loja"
      columns={[
        { header: 'Loja', value: storeLabel },
        { header: 'Vendas (un.)', value: (s) => s.sold12Months },
        { header: 'Estoque (un.)', value: (s) => s.stockUnits },
      ]}
    >
      {() => (
        <BarChart
          format={(v) => `${formatNumber(v, 0)} un.`}
          data={rows.map((s) => ({ key: s.code, label: storeLabel(s), value: s.sold12Months, detail: `Estoque: ${formatNumber(s.stockUnits, 0)} un.` }))}
        />
      )}
    </ChartCard>
  )
}

function CoverageChart({ stores }: { stores: StoreDashboardRow[] }) {
  const rows = selling(stores).filter((s) => s.coverageDays !== null).toSorted((a, b) => (b.coverageDays ?? 0) - (a.coverageDays ?? 0))
  return (
    <ChartCard
      title="Cobertura por loja"
      description="Quantos dias o estoque da loja dura no ritmo de venda dela. Muito alto indica excesso; muito baixo, falta."
      rows={rows}
      exportName="cobertura-por-loja"
      columns={[
        { header: 'Loja', value: storeLabel },
        { header: 'Cobertura (dias)', value: (s) => s.coverageDays },
        { header: 'Estoque (un.)', value: (s) => s.stockUnits },
        { header: 'Vendas 12 meses (un.)', value: (s) => s.sold12Months },
      ]}
    >
      {() => (
        <BarChart
          format={(v) => `${formatNumber(v, 0)} dias`}
          data={rows.map((s) => ({ key: s.code, label: storeLabel(s), value: s.coverageDays ?? 0 }))}
        />
      )}
    </ChartCard>
  )
}

function TopProductsChart({ products, onOpen }: { products: TopProduct[]; onOpen: (productId: number) => void }) {
  return (
    <ChartCard
      title="10 produtos mais vendidos da rede"
      description="Clique num produto para abrir a ficha. A dica mostra em quantas lojas ele está em ruptura."
      rows={products}
      exportName="produtos-mais-vendidos"
      columns={[
        { header: 'Produto', value: (p) => `${p.code} ${p.description}` },
        { header: 'Vendas 12 meses (un.)', value: (p) => p.sold12Months },
        { header: 'Estoque (un.)', value: (p) => p.stockUnits },
        { header: 'Lojas em ruptura', value: (p) => p.ruptureStores },
      ]}
    >
      {() => (
        <BarChart
          format={(v) => `${formatNumber(v, 0)} un.`}
          onSelect={(key) => onOpen(Number(key))}
          data={products.map((p) => ({
            key: String(p.productId),
            label: p.description,
            value: p.sold12Months,
            detail: `${p.brandName} · estoque ${formatNumber(p.stockUnits, 0)} un. · ${p.ruptureStores} loja${p.ruptureStores === 1 ? '' : 's'} em ruptura`,
          }))}
        />
      )}
    </ChartCard>
  )
}

/** Três medidas de escalas diferentes: um gráfico para cada (nunca dois eixos). */
function Trend({ trend }: { trend: TrendPoint[] }) {
  const series: { title: string; value: (p: TrendPoint) => number; exportName: string }[] = [
    { title: 'Rupturas que importam', value: (p) => p.relevantRuptures, exportName: 'tendencia-rupturas' },
    { title: 'Itens negativos', value: (p) => p.negativeItems, exportName: 'tendencia-negativos' },
    { title: 'Sugestões de transferência', value: (p) => p.suggestions, exportName: 'tendencia-sugestoes' },
  ]
  return (
    <section className="flex flex-col gap-3" aria-labelledby="trend-title">
      <h2 id="trend-title" className="text-subtitle text-text">
        Tendência a cada análise
      </h2>
      <div className="grid gap-6 notebook:grid-cols-3">
        {series.map((s) => (
          <ChartCard
            key={s.title}
            title={s.title}
            rows={trend}
            exportName={s.exportName}
            columns={[
              { header: 'Análise', value: (p) => formatDate(p.analysisDate) },
              { header: 'Estoque de', value: (p) => formatDate(p.stockDate) },
              { header: s.title, value: s.value },
            ]}
          >
            {(expanded) => (
              <LineChart
                height={expanded ? 220 : 120}
                format={(v) => formatNumber(v, 0)}
                points={trend.map((p) => ({ label: formatDate(p.analysisDate), value: s.value(p) }))}
              />
            )}
          </ChartCard>
        ))}
      </div>
    </section>
  )
}
