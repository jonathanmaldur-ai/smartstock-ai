import { Menu, Moon, Sun } from 'lucide-react'
import { useState } from 'react'
import { Outlet } from 'react-router'
import { IconButton } from '@/components/ui/Button'
import { Sidebar } from './Sidebar'
import { UserMenu } from './UserMenu'
import { useTheme } from './useTheme'

const COLLAPSED_KEY = 'smartstock.sidebar-collapsed'

function readCollapsed() {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === 'true'
  } catch {
    return false
  }
}

/** Estrutura oficial do Design System: Sidebar + Topbar + Área de conteúdo. */
export function AppShell() {
  const [collapsed, setCollapsed] = useState(readCollapsed)
  const [mobileOpen, setMobileOpen] = useState(false)
  const { theme, toggle } = useTheme()

  const toggleCollapsed = () => {
    setCollapsed((current) => {
      try {
        localStorage.setItem(COLLAPSED_KEY, String(!current))
      } catch {
        // Preferência só nesta sessão.
      }
      return !current
    })
  }

  return (
    <div className="flex h-dvh overflow-hidden bg-background">
      <a
        href="#conteudo"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:rounded-md focus:bg-surface focus:p-3"
      >
        Pular para o conteúdo
      </a>

      <div className="hidden notebook:flex">
        <Sidebar collapsed={collapsed} onToggleCollapsed={toggleCollapsed} />
      </div>

      {mobileOpen && (
        <div className="fixed inset-0 z-40 notebook:hidden">
          <button
            type="button"
            aria-label="Fechar menu"
            className="absolute inset-0 bg-overlay"
            onClick={() => setMobileOpen(false)}
          />
          <div className="relative h-full w-64">
            <Sidebar mobile collapsed={false} onToggleCollapsed={() => undefined} onNavigate={() => setMobileOpen(false)} />
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 shrink-0 items-center justify-between gap-4 border-b border-border bg-surface px-4 tablet:px-6">
          <IconButton label="Abrir menu" className="notebook:hidden" onClick={() => setMobileOpen(true)}>
            <Menu aria-hidden className="size-5" />
          </IconButton>
          <div className="flex-1" />
          <div className="flex items-center gap-2">
            <IconButton label={theme === 'dark' ? 'Usar modo claro' : 'Usar modo escuro'} onClick={toggle}>
              {theme === 'dark' ? <Sun aria-hidden className="size-5" /> : <Moon aria-hidden className="size-5" />}
            </IconButton>
            <UserMenu />
          </div>
        </header>

        <main id="conteudo" className="flex-1 overflow-y-auto">
          <div className="mx-auto w-full max-w-[1600px] p-4 tablet:p-6 desktop:p-8">
            <Outlet />
          </div>
        </main>
      </div>
    </div>
  )
}

export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: React.ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="flex flex-col gap-1">
        <h1 className="text-heading text-text">{title}</h1>
        {description && <p className="text-body text-text-muted">{description}</p>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}
