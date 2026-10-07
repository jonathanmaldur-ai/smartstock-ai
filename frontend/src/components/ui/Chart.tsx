import { Download, Maximize2, Table2, BarChart3 } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { downloadCsv, type CsvColumn } from '@/lib/csv'
import { IconButton } from './Button'
import { Card } from './Card'
import { Modal } from './Modal'
import { Table, Td, Th } from './Table'

/**
 * Gráficos do Design System (capítulo 14): uma medida por gráfico, cor única do tema (primary),
 * marcas finas, grade discreta, dica ao passar o mouse, tabela, ampliar e exportação.
 * Uma medida por gráfico evita depender de distinguir cores (daltonismo) e nunca usa dois eixos.
 */

export type BarDatum = { key: string; label: string; value: number; detail?: string }

type ChartCardProps<T> = {
  title: string
  description?: string
  /** Linhas da tabela e da exportação (os mesmos números do gráfico). */
  rows: readonly T[]
  columns: readonly CsvColumn<T>[]
  exportName: string
  children: (expanded: boolean) => ReactNode
}

/** Moldura dos gráficos: título, alternar gráfico/tabela, ampliar e exportar CSV. */
export function ChartCard<T>({ title, description, rows, columns, exportName, children }: ChartCardProps<T>) {
  const [showTable, setShowTable] = useState(false)
  const [expanded, setExpanded] = useState(false)

  const body = (large: boolean) => (showTable ? <DataTable title={title} rows={rows} columns={columns} /> : children(large))

  return (
    <Card className="flex flex-col">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border p-4 tablet:px-6">
        <div className="flex min-w-0 flex-col gap-1">
          <h2 className="text-subtitle text-text">{title}</h2>
          {description && <p className="text-caption text-text-muted">{description}</p>}
        </div>
        <div className="flex gap-1">
          <IconButton label={showTable ? `Ver gráfico: ${title}` : `Ver tabela: ${title}`} aria-pressed={showTable} onClick={() => setShowTable((v) => !v)}>
            {showTable ? <BarChart3 aria-hidden className="size-4" /> : <Table2 aria-hidden className="size-4" />}
          </IconButton>
          <IconButton label={`Ampliar: ${title}`} onClick={() => setExpanded(true)}>
            <Maximize2 aria-hidden className="size-4" />
          </IconButton>
          <IconButton label={`Exportar em CSV: ${title}`} onClick={() => downloadCsv(`${exportName}.csv`, columns, rows)}>
            <Download aria-hidden className="size-4" />
          </IconButton>
        </div>
      </div>
      <div className="flex-1 p-4 tablet:px-6">{body(false)}</div>
      <Modal open={expanded} onClose={() => setExpanded(false)} title={title} description={description} size="lg">
        {body(true)}
      </Modal>
    </Card>
  )
}

function DataTable<T>({ title, rows, columns }: { title: string; rows: readonly T[]; columns: readonly CsvColumn<T>[] }) {
  return (
    <Table>
      <caption className="sr-only">{title}</caption>
      <thead>
        <tr>
          {columns.map((c, i) => (
            <Th key={c.header} className={i > 0 ? 'text-right' : undefined}>
              {c.header}
            </Th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, r) => (
          <tr key={r}>
            {columns.map((c, i) => {
              const value = c.value(row)
              return (
                <Td key={c.header} className={i > 0 ? 'text-right tabular-nums' : undefined}>
                  {typeof value === 'number' ? value.toLocaleString('pt-BR', { maximumFractionDigits: 1 }) : (value ?? '—')}
                </Td>
              )
            })}
          </tr>
        ))}
      </tbody>
    </Table>
  )
}

/**
 * Barras horizontais de uma medida (≤ 24px, ponta arredondada, base reta), valor na ponta.
 * A dica aparece ao passar o mouse ou focar a linha (teclado).
 */
export function BarChart({ data, format, onSelect }: { data: readonly BarDatum[]; format: (value: number) => string; onSelect?: (key: string) => void }) {
  const max = Math.max(...data.map((d) => d.value), 0)
  return (
    <ul className="flex flex-col gap-1" aria-label="Gráfico de barras">
      {data.map((d) => {
        const width = max > 0 ? Math.max((d.value / max) * 100, d.value > 0 ? 1 : 0) : 0
        const content = (
          <>
            <span className="w-36 shrink-0 truncate text-left text-caption text-text-muted tablet:w-44" title={d.label}>
              {d.label}
            </span>
            <span className="relative flex h-6 flex-1 items-center">
              <span className="block h-4 rounded-r-[4px] bg-primary transition-opacity group-hover:opacity-80" style={{ width: `${width}%` }} aria-hidden />
              <span className="ml-2 shrink-0 text-caption tabular-nums text-text">{format(d.value)}</span>
            </span>
            <Tooltip>
              <strong className="block">{d.label}</strong>
              {format(d.value)}
              {d.detail && <span className="block text-text-muted">{d.detail}</span>}
            </Tooltip>
          </>
        )
        const className = 'group relative flex w-full items-center gap-3 rounded-md px-1 py-0.5 hover:bg-surface-muted focus-visible:bg-surface-muted'
        return (
          <li key={d.key}>
            {onSelect ? (
              <button type="button" className={className} onClick={() => onSelect(d.key)} aria-label={`${d.label}: ${format(d.value)}`}>
                {content}
              </button>
            ) : (
              <div className={className} tabIndex={0} aria-label={`${d.label}: ${format(d.value)}`}>
                {content}
              </div>
            )}
          </li>
        )
      })}
    </ul>
  )
}

function Tooltip({ children }: { children: ReactNode }) {
  return (
    <span
      role="tooltip"
      className="pointer-events-none absolute bottom-full left-40 z-10 mb-1 hidden min-w-40 rounded-md border border-border bg-surface p-2 text-caption text-text shadow-card group-hover:block group-focus-visible:block"
    >
      {children}
    </span>
  )
}

export type LinePoint = { label: string; value: number }

/**
 * Linha de uma medida no tempo (2px, marcadores de 8px com anel da superfície), um eixo só.
 * Com um ponto só, mostra o número e explica que a tendência aparece com as próximas análises.
 */
export function LineChart({
  points,
  format,
  height = 120,
  singlePointHint = 'A linha aparece a partir da segunda análise.',
}: {
  points: readonly LinePoint[]
  format: (value: number) => string
  height?: number
  /** Texto quando há um ponto só (ex.: "Escolha um período maior para ver a linha."). */
  singlePointHint?: string
}) {
  const [active, setActive] = useState<number | null>(null)
  const gradientId = useId()
  if (points.length < 2)
    return (
      <p className="text-caption text-text-muted">
        {points[0] ? `${format(points[0].value)} em ${points[0].label}. ` : ''}
        {singlePointHint}
      </p>
    )

  const width = 320
  const pad = { top: 12, right: 12, bottom: 20, left: 12 }
  const values = points.map((p) => p.value)
  const min = Math.min(...values, 0)
  const max = Math.max(...values)
  const x = (i: number) => pad.left + (i * (width - pad.left - pad.right)) / (points.length - 1)
  const y = (v: number) => pad.top + (1 - (v - min) / (max - min || 1)) * (height - pad.top - pad.bottom)
  const path = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(i)},${y(p.value)}`).join(' ')
  const last = points.length - 1
  const shown = points[active ?? last]!

  return (
    <div className="flex flex-col gap-1">
      <p className="text-caption text-text-muted" aria-live="polite">
        <span className="font-medium text-text">{format(shown.value)}</span> em {shown.label}
      </p>
      <svg viewBox={`0 0 ${width} ${height}`} className="h-auto w-full" role="img" aria-label={points.map((p) => `${p.label}: ${format(p.value)}`).join('; ')}>
        <defs>
          <linearGradient id={gradientId} x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="var(--color-primary)" stopOpacity="0.12" />
            <stop offset="100%" stopColor="var(--color-primary)" stopOpacity="0" />
          </linearGradient>
        </defs>
        <line x1={pad.left} x2={width - pad.right} y1={height - pad.bottom} y2={height - pad.bottom} stroke="var(--color-border)" strokeWidth="1" />
        <path d={`${path} L${x(last)},${height - pad.bottom} L${x(0)},${height - pad.bottom} Z`} fill={`url(#${gradientId})`} />
        <path d={path} fill="none" stroke="var(--color-primary)" strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
        {points.map((p, i) => (
          <g key={p.label + i} onMouseEnter={() => setActive(i)} onMouseLeave={() => setActive(null)}>
            <rect x={x(i) - 12} y={0} width={24} height={height} fill="transparent" />
            <circle cx={x(i)} cy={y(p.value)} r={p === shown ? 5 : 4} fill="var(--color-primary)" stroke="var(--color-surface)" strokeWidth="2" />
          </g>
        ))}
        <text x={pad.left} y={height - 4} className={cn('fill-text-muted')} fontSize="10">
          {points[0]!.label}
        </text>
        <text x={width - pad.right} y={height - 4} textAnchor="end" className="fill-text-muted" fontSize="10">
          {points[last]!.label}
        </text>
      </svg>
    </div>
  )
}
