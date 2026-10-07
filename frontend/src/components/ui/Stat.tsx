import { cn } from '@/lib/cn'
import type { Tone } from './Feedback'

const toneText: Record<Tone, string> = {
  info: 'text-primary',
  success: 'text-success',
  warning: 'text-warning',
  error: 'text-error',
  neutral: 'text-text',
}

/** Número em destaque com rótulo (KPI compacto). */
export function Stat({ label, value, tone = 'neutral' }: { label: string; value: number; tone?: Tone }) {
  return (
    <div className="flex flex-col gap-1 rounded-md border border-border bg-surface p-4">
      <span className="text-caption text-text-muted">{label}</span>
      <span className={cn('font-display text-heading tabular-nums', toneText[tone])}>{value.toLocaleString('pt-BR')}</span>
    </div>
  )
}
