import { PanelLeftClose, PanelLeftOpen } from 'lucide-react'
import { Link, NavLink } from 'react-router'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { cn } from '@/lib/cn'
import { BrandLogo } from './BrandLogo'
import { navSections } from './navigation'

type SidebarProps = {
  collapsed: boolean
  onToggleCollapsed: () => void
  onNavigate?: () => void
  /** No celular o menu é uma gaveta: sempre expandido e sem botão de recolher. */
  mobile?: boolean
}

export function Sidebar({ collapsed, onToggleCollapsed, onNavigate, mobile = false }: SidebarProps) {
  const { user } = useAuth()
  const sections = navSections
    .map((section) => ({ ...section, items: section.items.filter((item) => hasRole(user?.role, item.roles)) }))
    .filter((section) => section.items.length > 0)
  const compact = collapsed && !mobile

  return (
    <aside
      className={cn(
        'flex h-full flex-col border-r border-border bg-surface transition-[width] duration-200',
        compact ? 'w-16' : 'w-64',
      )}
    >
      <div className={cn('flex h-16 items-center border-b border-border', compact ? 'justify-center' : 'px-4')}>
        {/* O logo leva à página inicial (Dashboard). */}
        <Link to="/" onClick={onNavigate} aria-label="Ir para a página inicial" className="flex items-center gap-3 rounded-md">
          {compact ? (
            <BrandLogo className="h-6 w-12 dark:h-8 dark:p-1" />
          ) : (
            <>
              <BrandLogo className="h-9" />
              <span className="font-display text-label font-semibold text-text-muted">SmartStock AI</span>
            </>
          )}
        </Link>
      </div>

      <nav aria-label="Menu principal" className="flex flex-1 flex-col gap-4 overflow-y-auto p-2">
        {sections.map((section, index) => (
        <div key={section.title ?? index} className="flex flex-col gap-1">
          {section.title &&
            (compact ? (
              <hr className="mx-2 border-border" />
            ) : (
              <p className="px-3 pt-2 text-caption font-medium uppercase tracking-wider text-text-subtle">{section.title}</p>
            ))}
        <ul className="flex flex-col gap-1">
          {section.items.map(({ to, label, icon: Icon }) => (
            <li key={to}>
              <NavLink
                to={to}
                end={to === '/'}
                onClick={onNavigate}
                title={compact ? label : undefined}
                className={({ isActive }) =>
                  cn(
                    'flex h-10 items-center gap-3 rounded-md text-body font-medium transition-colors',
                    compact ? 'justify-center' : 'px-3',
                    isActive
                      ? 'bg-primary-soft text-primary-active dark:text-primary'
                      : 'text-text-muted hover:bg-surface-muted hover:text-text',
                  )
                }
              >
                <Icon aria-hidden className="size-5 shrink-0" />
                <span className={cn(compact && 'sr-only')}>{label}</span>
              </NavLink>
            </li>
          ))}
        </ul>
        </div>
        ))}
      </nav>

      {!mobile && (
        <div className="border-t border-border p-2">
          <button
            type="button"
            onClick={onToggleCollapsed}
            aria-label={collapsed ? 'Expandir menu' : 'Recolher menu'}
            className={cn(
              'flex h-10 w-full items-center gap-3 rounded-md text-body text-text-muted hover:bg-surface-muted hover:text-text',
              compact ? 'justify-center' : 'px-3',
            )}
          >
            {collapsed ? <PanelLeftOpen aria-hidden className="size-5" /> : <PanelLeftClose aria-hidden className="size-5" />}
            {!compact && <span>Recolher menu</span>}
          </button>
        </div>
      )}
    </aside>
  )
}
