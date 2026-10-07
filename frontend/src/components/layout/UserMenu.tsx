import { ChevronDown, LogOut } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import { useAuth } from '@/features/auth/useAuth'

export function UserMenu() {
  const { user, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const menuId = useId()

  useEffect(() => {
    if (!open) return
    const close = (event: MouseEvent | KeyboardEvent) => {
      if (event instanceof KeyboardEvent ? event.key === 'Escape' : !containerRef.current?.contains(event.target as Node))
        setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', close)
    }
  }, [open])

  if (!user) return null
  const initials = user.fullName
    .split(' ')
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('')

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-controls={menuId}
        aria-haspopup="menu"
        className="flex h-10 items-center gap-2 rounded-md px-2 hover:bg-surface-muted"
      >
        <span className="flex size-8 items-center justify-center rounded-full bg-primary text-label text-primary-fg">
          {initials}
        </span>
        <span className="hidden flex-col items-start tablet:flex">
          <span className="text-label text-text">{user.fullName}</span>
          <span className="text-caption text-text-subtle">{user.role}</span>
        </span>
        <ChevronDown aria-hidden className="size-4 text-text-muted" />
      </button>

      {open && (
        <div
          id={menuId}
          role="menu"
          className="absolute right-0 z-40 mt-2 w-64 rounded-md border border-border bg-surface p-2 shadow-card"
        >
          <div className="border-b border-border px-2 pb-2">
            <p className="text-label text-text">{user.fullName}</p>
            <p className="truncate text-caption text-text-subtle">{user.email}</p>
            <p className="text-caption text-text-subtle">Perfil: {user.role}</p>
          </div>
          <button
            type="button"
            role="menuitem"
            onClick={() => void logout()}
            className="mt-2 flex h-10 w-full items-center gap-2 rounded-md px-2 text-body text-text hover:bg-surface-muted"
          >
            <LogOut aria-hidden className="size-4" />
            Sair
          </button>
        </div>
      )}
    </div>
  )
}
