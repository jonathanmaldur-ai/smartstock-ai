import { Loader2 } from 'lucide-react'
import { cn } from '@/lib/cn'

export function Spinner({ className, label = 'Carregando' }: { className?: string; label?: string }) {
  return (
    <span role="status" className="inline-flex items-center">
      <Loader2 aria-hidden className={cn('size-4 animate-spin', className)} />
      <span className="sr-only">{label}</span>
    </span>
  )
}

export function FullPageSpinner({ label }: { label: string }) {
  return (
    <div className="flex min-h-dvh items-center justify-center gap-3 text-text-muted">
      <Spinner className="size-6 text-primary" label={label} />
      <span aria-hidden>{label}…</span>
    </div>
  )
}
