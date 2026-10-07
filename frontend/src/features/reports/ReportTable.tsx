import { Table, Td, Th } from '@/components/ui/Table'
import { cn } from '@/lib/cn'
import { formatDate, formatNumber } from '@/lib/format'
import type { ReportCell, ReportColumnKind, ReportData } from './reportsApi'

export function formatCell(value: ReportCell, kind: ReportColumnKind) {
  if (value === null || value === '') return '—'
  if (kind === 'Date' && typeof value === 'string') return formatDate(value)
  if (typeof value === 'number') return formatNumber(value, kind === 'Integer' ? 0 : 2)
  return value
}

/** Tabela de um relatório: números à direita, datas em dd/mm/aaaa. Usada na tela e na impressão. */
export function ReportTable({ report, compact = false }: { report: ReportData; compact?: boolean }) {
  return (
    <Table className={cn(compact && 'text-caption')}>
      <caption className="sr-only">{report.title}</caption>
      <thead>
        <tr>
          {report.columns.map((c) => (
            <Th key={c.header} className={cn(c.kind !== 'Text' && 'text-right', compact && 'px-2 py-1')}>
              {c.header}
            </Th>
          ))}
        </tr>
      </thead>
      <tbody>
        {report.rows.map((row, r) => (
          <tr key={r}>
            {row.map((value, i) => {
              const kind = report.columns[i]?.kind ?? 'Text'
              return (
                <Td key={i} className={cn(kind !== 'Text' && 'whitespace-nowrap text-right tabular-nums', compact && 'px-2 py-1')}>
                  {formatCell(value, kind)}
                </Td>
              )
            })}
          </tr>
        ))}
      </tbody>
    </Table>
  )
}
