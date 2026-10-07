import { X } from 'lucide-react'
import { createContext, use, useCallback, useMemo, useState, type ReactNode } from 'react'
import { cn } from '@/lib/cn'
import type { Tone } from './Feedback'

type Toast = { id: number; tone: Tone; message: string }
type ToastContextValue = { show: (message: string, tone?: Tone) => void }

const ToastContext = createContext<ToastContextValue | null>(null)
const DURATION_MS = 5000

const toneClasses: Record<Tone, string> = {
  info: 'border-l-info',
  success: 'border-l-success',
  warning: 'border-l-warning',
  error: 'border-l-error',
  neutral: 'border-l-border-strong',
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])

  const dismiss = useCallback((id: number) => setToasts((current) => current.filter((t) => t.id !== id)), [])

  const show = useCallback(
    (message: string, tone: Tone = 'success') => {
      const id = Date.now() + Math.random()
      setToasts((current) => [...current, { id, tone, message }])
      window.setTimeout(() => dismiss(id), DURATION_MS)
    },
    [dismiss],
  )

  const value = useMemo(() => ({ show }), [show])

  return (
    <ToastContext value={value}>
      {children}
      <div
        aria-live="polite"
        className="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex flex-col items-end gap-2 tablet:left-auto tablet:w-96"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            role={toast.tone === 'error' ? 'alert' : 'status'}
            className={cn(
              'pointer-events-auto flex w-full items-start gap-3 rounded-md border border-l-4 border-border bg-surface p-3 text-body text-text shadow-card',
              toneClasses[toast.tone],
            )}
          >
            <p className="flex-1">{toast.message}</p>
            <button
              type="button"
              aria-label="Fechar aviso"
              onClick={() => dismiss(toast.id)}
              className="text-text-muted hover:text-text"
            >
              <X aria-hidden className="size-4" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext>
  )
}

export function useToast() {
  const context = use(ToastContext)
  if (!context) throw new Error('useToast precisa estar dentro de <ToastProvider>.')
  return context
}
