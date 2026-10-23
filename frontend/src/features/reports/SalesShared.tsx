import { ArrowDown, ArrowUp, Minus } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/Button'
import { SelectField, TextField } from '@/components/ui/Field'
import { useStores } from '@/features/catalog/catalogApi'
import { cn } from '@/lib/cn'
import { formatNumber } from '@/lib/format'
import type { DailySalesFilters } from './reportsApi'

/** Peças compartilhadas pelos relatórios de vendas do dia a dia (Vendas por dia e Vendas em quantidade). */

const presets = [7, 30, 90] as const

/** "2026-09-30" menos n dias, sem fuso. */
function minusDays(iso: string, days: number) {
  const date = new Date(`${iso}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() - days)
  return date.toISOString().slice(0, 10)
}

/** Atalhos de 7, 30 e 90 dias, datas De e Até e a loja (ou a rede toda). */
export function SalesFilters({
  filters,
  onChange,
  last,
  from,
  to,
}: {
  filters: DailySalesFilters
  onChange: (filters: DailySalesFilters) => void
  last?: string | null
  from?: string | null
  to?: string | null
}) {
  const stores = useStores()
  const preset = (days: number) => last && onChange({ ...filters, de: minusDays(last, days - 1), ate: last })
  const activePreset = presets.find((d) => last && filters.ate === last && filters.de === minusDays(last, d - 1))

  return (
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
        <TextField label="De" type="date" value={filters.de ?? from ?? ''} onChange={(e) => onChange({ ...filters, de: e.target.value || undefined })} />
        <TextField label="Até" type="date" value={filters.ate ?? to ?? ''} onChange={(e) => onChange({ ...filters, ate: e.target.value || undefined })} />
        <SelectField
          label="Loja"
          value={filters.storeId ?? ''}
          options={[
            { value: '', label: 'Rede toda' },
            ...(stores.data ?? []).filter((s) => s.status === 'Active').map((s) => ({ value: String(s.id), label: `${s.code} ${s.name}` })),
          ]}
          onChange={(e) => onChange({ ...filters, storeId: e.target.value ? Number(e.target.value) : undefined })}
        />
      </div>
    </div>
  )
}

/** Variação percentual com seta e sinal, nunca só a cor: verde subiu, vermelho caiu. */
export function Delta({ now, before }: { now: number | null; before: number | null | undefined }) {
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

export function Figure({ label, value, delta, hint }: { label: string; value: string; delta: ReactNode; hint?: string }) {
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
