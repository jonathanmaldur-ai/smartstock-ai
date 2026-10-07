import { cn } from '@/lib/cn'

/**
 * Logo Dorémi Brinquedos. Proporção e cores nunca são alteradas.
 * No modo escuro o logo fica sobre uma "caixa" clara, como orienta o manual da marca
 * para fundos que prejudicam a leitura.
 */
export function BrandLogo({ variant = 'horizontal', className }: { variant?: 'horizontal' | 'mascote'; className?: string }) {
  const src = variant === 'mascote' ? '/brand/doremi-brinquedos-mascote.png' : '/brand/doremi-brinquedos.png'
  return (
    <span className={cn('inline-flex rounded-md dark:bg-white dark:p-2', className)}>
      <img src={src} alt="Dorémi Brinquedos" className="h-full max-w-full object-contain" />
    </span>
  )
}
