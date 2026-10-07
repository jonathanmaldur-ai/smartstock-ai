import {
  Activity,
  Bell,
  ArrowLeftRight,
  FileText,
  FolderTree,
  LayoutDashboard,
  MessageSquare,
  MinusCircle,
  Package,
  ScrollText,
  ShoppingCart,
  Store,
  Tags,
  Upload,
  Users,
  type LucideIcon,
} from 'lucide-react'
import type { Role } from '@/lib/api'

export type NavItem = {
  to: string
  label: string
  icon: LucideIcon
  roles: readonly Role[]
}

export type NavSection = { title?: string; items: readonly NavItem[] }

const everyone: readonly Role[] = ['Administrador', 'Gerente', 'Operador', 'Consulta']
export const importRoles: readonly Role[] = ['Administrador', 'Operador']

/**
 * Menu lateral, em seções. Cada fase do roadmap adiciona os seus módulos aqui
 * (Estoque, Transferências, Relatórios, IA, Configurações).
 */
export const navSections: readonly NavSection[] = [
  {
    items: [
      { to: '/', label: 'Dashboard', icon: LayoutDashboard, roles: everyone },
      { to: '/assistente', label: 'Assistente', icon: MessageSquare, roles: everyone },
      { to: '/importacoes', label: 'Importações', icon: Upload, roles: importRoles },
    ],
  },
  {
    title: 'Análise',
    items: [
      { to: '/alertas', label: 'Alertas', icon: Bell, roles: everyone },
      { to: '/estoque', label: 'Situação do estoque', icon: Activity, roles: everyone },
      { to: '/sugestoes', label: 'Sugestões de transferência', icon: ArrowLeftRight, roles: everyone },
      { to: '/compras', label: 'Sugestões de compra', icon: ShoppingCart, roles: everyone },
      { to: '/negativos', label: 'Estoque negativo', icon: MinusCircle, roles: everyone },
      { to: '/relatorios', label: 'Relatórios', icon: FileText, roles: everyone },
    ],
  },
  {
    title: 'Cadastros',
    items: [
      { to: '/produtos', label: 'Produtos', icon: Package, roles: everyone },
      { to: '/marcas', label: 'Marcas', icon: Tags, roles: everyone },
      { to: '/categorias', label: 'Categorias', icon: FolderTree, roles: everyone },
      { to: '/lojas', label: 'Lojas', icon: Store, roles: everyone },
    ],
  },
  {
    title: 'Administração',
    items: [
      { to: '/usuarios', label: 'Usuários', icon: Users, roles: ['Administrador'] },
      { to: '/auditoria', label: 'Auditoria', icon: ScrollText, roles: ['Administrador'] },
    ],
  },
]
