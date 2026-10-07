import { X } from 'lucide-react'
import { useEffect, useId, useRef, type ReactNode } from 'react'
import { IconButton } from './Button'

type ModalProps = {
  open: boolean
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
  footer?: ReactNode
  size?: 'md' | 'lg'
}

/**
 * Modal acessível sobre o <dialog> nativo: prende o foco, fecha com Esc e devolve o foco ao sair.
 */
export function Modal({ open, title, description, onClose, children, footer, size = 'md' }: ModalProps) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()
  const descriptionId = useId()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal?.()
    if (!open && dialog.open) dialog.close?.()
  }, [open])

  return (
    <dialog
      ref={ref}
      aria-labelledby={titleId}
      aria-describedby={description ? descriptionId : undefined}
      onCancel={(event) => {
        event.preventDefault()
        onClose()
      }}
      onClick={(event) => {
        if (event.target === ref.current) onClose()
      }}
      className={`m-auto w-[calc(100%-2rem)] ${size === 'lg' ? 'max-w-4xl' : 'max-w-lg'} rounded-lg border border-border bg-surface p-0 text-text shadow-card backdrop:bg-overlay`}
    >
      {open && (
        <div className="flex max-h-[85dvh] flex-col">
          <div className="flex items-start justify-between gap-4 border-b border-border p-4 tablet:p-6">
            <div className="flex flex-col gap-1">
              <h2 id={titleId} className="text-subtitle">
                {title}
              </h2>
              {description && (
                <p id={descriptionId} className="text-body text-text-muted">
                  {description}
                </p>
              )}
            </div>
            <IconButton label="Fechar" onClick={onClose} className="-mr-2 -mt-2">
              <X aria-hidden className="size-4" />
            </IconButton>
          </div>
          <div className="overflow-y-auto p-4 tablet:p-6">{children}</div>
          {footer && (
            <div className="flex flex-wrap justify-end gap-2 border-t border-border p-4 tablet:px-6">{footer}</div>
          )}
        </div>
      )}
    </dialog>
  )
}
