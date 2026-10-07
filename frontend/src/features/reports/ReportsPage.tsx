import { Download, FileText, Printer } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Alert, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { SelectField } from '@/components/ui/Field'
import { useToast } from '@/components/ui/Toast'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { useCategories, useStores } from '@/features/catalog/catalogApi'
import { ApiError, apiDownload } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatDate } from '@/lib/format'
import { DAILY_DESCRIPTION, DAILY_TITLE, DailySalesPanel } from './DailySalesReport'
import { MONTHLY_DESCRIPTION, MONTHLY_TITLE, MonthlyReportPanel } from './MonthlyReport'
import { ReportTable } from './ReportTable'
import { DAILY_KEY, MONTHLY_KEY, reportQuery, useReport, useReportCatalog, type ReportFilters, type ReportInfo } from './reportsApi'

const MONTHLY_INFO: ReportInfo = { key: MONTHLY_KEY, title: MONTHLY_TITLE, description: MONTHLY_DESCRIPTION, group: 'Resumo do período' }
const DAILY_INFO: ReportInfo = { key: DAILY_KEY, title: DAILY_TITLE, description: DAILY_DESCRIPTION, group: MONTHLY_INFO.group }

/** Central de relatórios (Fase 4, decisões 11 e 42): Excel, CSV e PDF (pela impressão). */
export function ReportsPage() {
  const catalog = useReportCatalog()
  const [selected, setSelected] = useState<ReportInfo | null>(null)
  const [filters, setFilters] = useState<ReportFilters>({})

  // O mensal vem primeiro; depois os grupos na ordem do servidor (Situação do estoque, Cobertura e giro, Transferências).
  const groups: [string, ReportInfo[]][] = [[MONTHLY_INFO.group, [MONTHLY_INFO, DAILY_INFO]]]
  for (const report of catalog.data?.reports ?? []) {
    const group = groups.find(([name]) => name === report.group)
    if (group) group[1].push(report)
    else groups.push([report.group, [report]])
  }

  return (
    <>
      <PageHeader
        title="Relatórios"
        description="Escolha o relatório, filtre por loja, marca ou categoria e baixe em Excel, CSV ou PDF. Os dados são os da última análise."
      />
      <div className="grid gap-6 notebook:grid-cols-[18rem_1fr] notebook:items-start">
        <Card>
          {catalog.isPending ? (
            <div className="flex flex-col gap-2 p-4">
              {Array.from({ length: 6 }, (_, i) => (
                <Skeleton key={i} className="h-10" />
              ))}
            </div>
          ) : (
            <nav aria-label="Relatórios" className="flex flex-col gap-4 p-3">
              {groups.map(([group, reports]) => (
                <div key={group} className="flex flex-col gap-1">
                  <p className="px-2 text-label text-text-subtle">{group}</p>
                  {reports.map((r) => (
                    <button
                      key={r.key}
                      type="button"
                      aria-current={selected?.key === r.key ? 'page' : undefined}
                      onClick={() => setSelected(r)}
                      className={cn(
                        'rounded-md px-2 py-2 text-left text-body transition-colors hover:bg-surface-muted',
                        selected?.key === r.key ? 'bg-primary-soft font-medium text-primary' : 'text-text',
                      )}
                    >
                      {r.title}
                    </button>
                  ))}
                </div>
              ))}
            </nav>
          )}
        </Card>

        {selected?.key === MONTHLY_KEY ? (
          <MonthlyReportPanel />
        ) : selected?.key === DAILY_KEY ? (
          <DailySalesPanel />
        ) : selected ? (
          <ReportPanel report={selected} filters={filters} onFilters={setFilters} brands={catalog.data?.brands ?? []} />
        ) : (
          <Card>
            <EmptyState icon={FileText} title="Escolha um relatório" description="Os relatórios ficam na lista ao lado, separados por assunto." />
          </Card>
        )}
      </div>
    </>
  )
}

function ReportPanel({
  report,
  filters,
  onFilters,
  brands,
}: {
  report: ReportInfo
  filters: ReportFilters
  onFilters: (filters: ReportFilters) => void
  brands: { id: number; name: string }[]
}) {
  const toast = useToast()
  const stores = useStores()
  const categories = useCategories()
  const { data, isPending, isError, error, refetch, isFetching } = useReport(report.key, filters)
  const [downloading, setDownloading] = useState<'excel' | 'csv' | null>(null)

  const toNumber = (value: string) => (value ? Number(value) : undefined)
  const all = { value: '', label: 'Todas' }

  const download = async (format: 'excel' | 'csv') => {
    setDownloading(format)
    try {
      await apiDownload(`/reports/${report.key}/${format}?${reportQuery(filters)}`, `${report.key}.${format === 'excel' ? 'xlsx' : 'csv'}`)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível gerar o arquivo.', 'error')
    } finally {
      setDownloading(null)
    }
  }

  const print = () => window.open(`/relatorios/imprimir?${reportQuery({ ...filters })}&relatorio=${report.key}`, '_blank', 'noopener')

  return (
    <Card>
      <CardHeader
        title={report.title}
        description={report.description}
        actions={
          <>
            <Button variant="secondary" size="sm" icon={<Download aria-hidden className="size-4" />} loading={downloading === 'excel'} onClick={() => void download('excel')}>
              Excel
            </Button>
            <Button variant="secondary" size="sm" icon={<Download aria-hidden className="size-4" />} loading={downloading === 'csv'} onClick={() => void download('csv')}>
              CSV
            </Button>
            <Button variant="secondary" size="sm" icon={<Printer aria-hidden className="size-4" />} onClick={print}>
              Imprimir / PDF
            </Button>
          </>
        }
      />
      <div className="grid gap-4 border-b border-border p-4 tablet:grid-cols-3 tablet:px-6">
        <SelectField
          label="Loja"
          value={filters.storeId ?? ''}
          options={[all, ...(stores.data ?? []).filter((s) => s.status === 'Active').map((s) => ({ value: String(s.id), label: `${s.code} ${s.name}` }))]}
          onChange={(e) => onFilters({ ...filters, storeId: toNumber(e.target.value) })}
        />
        <SelectField
          label="Marca"
          value={filters.brandId ?? ''}
          options={[all, ...brands.map((b) => ({ value: String(b.id), label: b.name }))]}
          onChange={(e) => onFilters({ ...filters, brandId: toNumber(e.target.value) })}
        />
        <SelectField
          label="Categoria"
          value={filters.categoryId ?? ''}
          options={[
            all,
            ...(categories.data ?? [])
              .filter((c) => !c.excludedFromAnalysis)
              .toSorted((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
              .map((c) => ({ value: String(c.id), label: c.name })),
          ]}
          onChange={(e) => onFilters({ ...filters, categoryId: toNumber(e.target.value) })}
        />
      </div>

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
        <div className={cn('flex flex-col', isFetching && 'opacity-60')}>
          <p className="px-4 pt-4 text-caption text-text-muted tablet:px-6">
            {data.filterSummary} · análise de {data.analysisDate ? formatDate(data.analysisDate) : '—'} com o estoque de{' '}
            {data.stockDate ? formatDate(data.stockDate) : '—'} · {data.totalRows.toLocaleString('pt-BR')} linhas
          </p>
          {data.totalRows > data.rows.length && (
            <div className="px-4 pt-3 tablet:px-6">
              <Alert tone="info">
                Mostrando as {data.rows.length.toLocaleString('pt-BR')} primeiras linhas. O Excel e o CSV trazem todas as{' '}
                {data.totalRows.toLocaleString('pt-BR')}.
              </Alert>
            </div>
          )}
          {data.totalRows === 0 ? (
            <EmptyState icon={FileText} title="Nada encontrado" description="Nenhuma linha com esses filtros." />
          ) : (
            <div className="p-2">
              <ReportTable report={data} />
            </div>
          )}
        </div>
      )}
    </Card>
  )
}
