import { Alert, Skeleton } from '@/components/ui/Feedback'

/** Estados de carregamento e erro compartilhados pelas telas de cadastro. */
export function TableLoading({ rows = 6 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-3 p-6">
      {Array.from({ length: rows }, (_, i) => (
        <Skeleton key={i} className="h-10" />
      ))}
    </div>
  )
}

export function TableError({ what, onRetry }: { what: string; onRetry: () => void }) {
  return (
    <div className="p-6">
      <Alert tone="error" title={`Não foi possível carregar ${what}`}>
        <button type="button" className="underline" onClick={onRetry}>
          Tentar novamente
        </button>
      </Alert>
    </div>
  )
}
