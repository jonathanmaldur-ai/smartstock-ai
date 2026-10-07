import { Printer } from 'lucide-react'
import { useEffect, useRef, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { BrandLogo } from '@/components/layout/BrandLogo'
import { Button } from '@/components/ui/Button'
import { Alert, Skeleton } from '@/components/ui/Feedback'
import { formatDate, formatDateTime } from '@/lib/format'
import { DAILY_DESCRIPTION, DAILY_TITLE, DailySalesContent } from './DailySalesReport'
import { MONTHLY_DESCRIPTION, MONTHLY_TITLE, MonthlyReportContent } from './MonthlyReport'
import { ReportTable } from './ReportTable'
import { DAILY_KEY, MONTHLY_KEY, useDailySales, useMonthlyReport, useReport } from './reportsApi'

/**
 * Versão de impressão de um relatório (decisão 42): o navegador salva em PDF pelo "Imprimir".
 * Sem menu, em tema claro, com cabeçalho da marca; abre a janela de impressão sozinha ao carregar.
 */
export function ReportPrintPage() {
  const [params] = useSearchParams()
  const key = params.get('relatorio')
  if (key === MONTHLY_KEY) return <MonthlyPrint compareWith={params.get('comparar')} />
  if (key === DAILY_KEY) return <DailyPrint />
  return <TablePrint />
}

function DailyPrint() {
  const [params] = useSearchParams()
  const storeId = params.get('storeId')
  const { data, isPending, isError } = useDailySales({
    de: params.get('de') ?? undefined,
    ate: params.get('ate') ?? undefined,
    storeId: storeId ? Number(storeId) : undefined,
  })
  usePrintWhenReady(data?.from ? DAILY_TITLE : null)

  if (isPending || isError || !data?.from) return <PrintState loading={isPending && !isError} />

  return (
    <PrintLayout title={DAILY_TITLE} description={DAILY_DESCRIPTION} details={`Emitido em ${formatDateTime(new Date().toISOString())}`}>
      <DailySalesContent report={data} compact />
    </PrintLayout>
  )
}

function TablePrint() {
  const [params] = useSearchParams()
  const key = params.get('relatorio')
  const number = (name: string) => (params.get(name) ? Number(params.get(name)) : undefined)
  const { data, isPending, isError } = useReport(key, { storeId: number('storeId'), brandId: number('brandId'), categoryId: number('categoryId') })
  usePrintWhenReady(data?.title ?? null)

  if (isPending || isError || !data) return <PrintState loading={isPending && !isError} />

  return (
    <PrintLayout
      title={data.title}
      description={data.description}
      details={`${data.filterSummary} · análise de ${data.analysisDate ? formatDate(data.analysisDate) : '—'} com o estoque de ${
        data.stockDate ? formatDate(data.stockDate) : '—'
      } · emitido em ${formatDateTime(new Date().toISOString())}`}
    >
      {data.totalRows > data.rows.length && (
        <p className="text-caption text-text-muted">
          Impressão com as {data.rows.length.toLocaleString('pt-BR')} primeiras de {data.totalRows.toLocaleString('pt-BR')} linhas. Para a lista
          completa, use o Excel.
        </p>
      )}
      <ReportTable report={data} compact />
    </PrintLayout>
  )
}

function MonthlyPrint({ compareWith }: { compareWith: string | null }) {
  const { data, isPending, isError } = useMonthlyReport(true, compareWith)
  usePrintWhenReady(data ? MONTHLY_TITLE : null)

  if (isPending || isError || !data) return <PrintState loading={isPending && !isError} />

  return (
    <PrintLayout title={MONTHLY_TITLE} description={MONTHLY_DESCRIPTION} details={`Emitido em ${formatDateTime(new Date().toISOString())}`}>
      <MonthlyReportContent report={data} compact />
    </PrintLayout>
  )
}

function PrintLayout({ title, description, details, children }: { title: string; description: string; details: string; children: ReactNode }) {
  return (
    <main className="mx-auto flex max-w-[1100px] flex-col gap-4 bg-white p-6 text-text print:max-w-none print:p-0">
      <header className="flex items-start justify-between gap-4 border-b border-border pb-3">
        <div className="flex flex-col gap-1">
          <h1 className="font-display text-heading">{title}</h1>
          <p className="text-caption text-text-muted">{description}</p>
          <p className="text-caption text-text-muted">{details}</p>
        </div>
        <BrandLogo className="h-10 shrink-0" />
      </header>
      <div className="print:hidden">
        <Button icon={<Printer aria-hidden className="size-4" />} onClick={() => window.print()}>
          Imprimir / Salvar em PDF
        </Button>
      </div>
      {children}
      <footer className="border-t border-border pt-2 text-caption text-text-subtle">SmartStock AI · Dorémi Brinquedos</footer>
    </main>
  )
}

/** Tema claro sempre; abre a janela de impressão uma única vez, quando os dados chegam. */
function usePrintWhenReady(title: string | null) {
  const printed = useRef(false)
  useEffect(() => {
    document.documentElement.dataset.theme = 'light'
    if (title && !printed.current) {
      printed.current = true
      document.title = `${title} · SmartStock AI`
      setTimeout(() => window.print?.(), 300)
    }
  }, [title])
}

function PrintState({ loading }: { loading: boolean }) {
  return <div className="p-8">{loading ? <Skeleton className="h-96" /> : <Alert tone="error">Não foi possível carregar o relatório.</Alert>}</div>
}
