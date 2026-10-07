import type { ReactNode } from 'react'
import { BrandLogo } from '@/components/layout/BrandLogo'
import { Card } from '@/components/ui/Card'

export function AuthLayout({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return (
    <div className="flex min-h-dvh items-center justify-center bg-background px-4 py-8">
      <div className="flex w-full max-w-md flex-col items-center gap-6">
        <BrandLogo variant="mascote" className="h-28" />
        <Card className="w-full p-6 tablet:p-8">
          <div className="mb-6 flex flex-col gap-1 text-center">
            <p className="font-display text-label font-semibold uppercase tracking-wider text-primary">SmartStock AI</p>
            <h1 className="text-heading text-text">{title}</h1>
            {description && <p className="text-body text-text-muted">{description}</p>}
          </div>
          {children}
        </Card>
        <p className="text-caption text-text-subtle">Gestão inteligente de estoque · Dorémi Brinquedos</p>
      </div>
    </div>
  )
}
