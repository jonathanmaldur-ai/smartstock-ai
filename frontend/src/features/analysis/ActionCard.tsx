import { Link } from 'react-router'
import { cn } from '@/lib/cn'
import { formatNumber } from '@/lib/format'

type ActionCardProps = {
  value: number
  /** Texto no lugar do número inteiro formatado (ex.: "1,4×"). */
  display?: string
  title: string
  description: string
  /** Classe de cor do número (ex.: text-error). */
  tone: string
  to?: string
  onClick?: () => void
}

/** Número de ação em destaque: leva para a lista correspondente (link) ou aplica um filtro (botão). */
export function ActionCard({ value, display, title, description, tone, to, onClick }: ActionCardProps) {
  const content = (
    <>
      <span className="text-label text-text-muted">{title}</span>
      <span className={cn('font-display text-display tabular-nums', tone)}>{display ?? formatNumber(value, 0)}</span>
      <span className="text-caption text-text-muted">{description}</span>
    </>
  )
  const className = 'flex flex-col gap-1 rounded-lg border border-border bg-surface p-5 text-left shadow-card transition-colors hover:bg-surface-muted'
  if (to)
    return (
      <Link to={to} className={className}>
        {content}
      </Link>
    )
  if (onClick)
    return (
      <button type="button" onClick={onClick} className={className}>
        {content}
      </button>
    )
  return <div className={cn(className, 'hover:bg-surface')}>{content}</div>
}
