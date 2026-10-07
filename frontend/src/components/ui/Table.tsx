import { ChevronLeft, ChevronRight } from 'lucide-react'
import type { HTMLAttributes, TdHTMLAttributes, ThHTMLAttributes } from 'react'
import { cn } from '@/lib/cn'
import { IconButton } from './Button'

/** Tabela com rolagem horizontal em telas pequenas. */
export function Table({ className, ...props }: HTMLAttributes<HTMLTableElement>) {
  return (
    <div className="w-full overflow-x-auto">
      <table className={cn('w-full border-collapse text-left text-body', className)} {...props} />
    </div>
  )
}

export function Th({ className, ...props }: ThHTMLAttributes<HTMLTableCellElement>) {
  return (
    <th
      scope="col"
      className={cn(
        'whitespace-nowrap border-b border-border bg-surface-muted px-4 py-3 text-label text-text-muted',
        className,
      )}
      {...props}
    />
  )
}

export function Td({ className, ...props }: TdHTMLAttributes<HTMLTableCellElement>) {
  return <td className={cn('border-b border-border px-4 py-3 align-middle text-text', className)} {...props} />
}

export function Pagination({
  page,
  totalPages,
  totalCount,
  onPageChange,
}: {
  page: number
  totalPages: number
  totalCount: number
  onPageChange: (page: number) => void
}) {
  return (
    <nav aria-label="Paginação" className="flex items-center justify-between gap-4 px-4 py-3 text-body text-text-muted">
      <span>
        {totalCount.toLocaleString('pt-BR')} registro{totalCount === 1 ? '' : 's'}
      </span>
      <div className="flex items-center gap-2">
        <IconButton label="Página anterior" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft aria-hidden className="size-4" />
        </IconButton>
        <span aria-current="page">
          {page} de {Math.max(totalPages, 1)}
        </span>
        <IconButton label="Próxima página" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
          <ChevronRight aria-hidden className="size-4" />
        </IconButton>
      </div>
    </nav>
  )
}
