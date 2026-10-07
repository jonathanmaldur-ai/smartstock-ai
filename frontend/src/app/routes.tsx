import { Compass } from 'lucide-react'
import { lazy, Suspense, type ReactNode } from 'react'
import type { RouteObject } from 'react-router'
import { Link } from 'react-router'
import { AppShell } from '@/components/layout/AppShell'
import { importRoles } from '@/components/layout/navigation'
import type { Role } from '@/lib/api'
import { Card } from '@/components/ui/Card'
import { EmptyState, Skeleton } from '@/components/ui/Feedback'
import { DefinePasswordPage } from '@/features/auth/DefinePasswordPage'
import { ForgotPasswordPage } from '@/features/auth/ForgotPasswordPage'
import { LoginPage } from '@/features/auth/LoginPage'
import { RedirectIfAuthenticated, RequireAuth, RequireRole } from '@/features/auth/RouteGuards'
import { DashboardPage } from '@/features/dashboard/DashboardPage'

// As telas são carregadas só quando acessadas (code splitting).
const UsersPage = lazy(() => import('@/features/users/UsersPage').then((m) => ({ default: m.UsersPage })))
const AuditPage = lazy(() => import('@/features/audit/AuditPage').then((m) => ({ default: m.AuditPage })))
const ImportsPage = lazy(() => import('@/features/imports/ImportsPage').then((m) => ({ default: m.ImportsPage })))
const ProductsPage = lazy(() => import('@/features/catalog/ProductsPage').then((m) => ({ default: m.ProductsPage })))
const BrandsPage = lazy(() => import('@/features/catalog/BrandsPage').then((m) => ({ default: m.BrandsPage })))
const CategoriesPage = lazy(() => import('@/features/catalog/CategoriesPage').then((m) => ({ default: m.CategoriesPage })))
const StoresPage = lazy(() => import('@/features/catalog/StoresPage').then((m) => ({ default: m.StoresPage })))
const StockSituationPage = lazy(() => import('@/features/analysis/StockSituationPage').then((m) => ({ default: m.StockSituationPage })))
const SuggestionsPage = lazy(() => import('@/features/analysis/SuggestionsPage').then((m) => ({ default: m.SuggestionsPage })))
const PurchasesPage = lazy(() => import('@/features/analysis/PurchasesPage').then((m) => ({ default: m.PurchasesPage })))
const ReportsPage = lazy(() => import('@/features/reports/ReportsPage').then((m) => ({ default: m.ReportsPage })))
const ReportPrintPage = lazy(() => import('@/features/reports/ReportPrintPage').then((m) => ({ default: m.ReportPrintPage })))
const AssistantPage = lazy(() => import('@/features/assistant/AssistantPage').then((m) => ({ default: m.AssistantPage })))
const NegativeStockPage = lazy(() => import('@/features/analysis/NegativeStockPage').then((m) => ({ default: m.NegativeStockPage })))
const AlertsPage = lazy(() => import('@/features/analysis/AlertsPage').then((m) => ({ default: m.AlertsPage })))

function PageLoading() {
  return (
    <div className="flex flex-col gap-3">
      <Skeleton className="h-8 w-64" />
      <Skeleton className="h-64" />
    </div>
  )
}

function LazyPage({ children, roles }: { children: ReactNode; roles?: readonly Role[] }) {
  const page = <Suspense fallback={<PageLoading />}>{children}</Suspense>
  return roles ? <RequireRole roles={roles}>{page}</RequireRole> : page
}

const adminOnly: readonly Role[] = ['Administrador']

export const routes: RouteObject[] = [
  {
    path: '/login',
    element: (
      <RedirectIfAuthenticated>
        <LoginPage />
      </RedirectIfAuthenticated>
    ),
  },
  { path: '/esqueci-senha', element: <ForgotPasswordPage /> },
  { path: '/definir-senha', element: <DefinePasswordPage /> },
  {
    // Versão de impressão (PDF) dos relatórios: sem o menu, mas exige login.
    path: '/relatorios/imprimir',
    element: (
      <RequireAuth>
        <LazyPage>
          <ReportPrintPage />
        </LazyPage>
      </RequireAuth>
    ),
  },
  {
    element: (
      <RequireAuth>
        <AppShell />
      </RequireAuth>
    ),
    children: [
      { path: 'relatorios', element: <LazyPage><ReportsPage /></LazyPage> },
      { index: true, element: <DashboardPage /> },
      {
        path: 'importacoes',
        element: (
          <LazyPage roles={importRoles}>
            <ImportsPage />
          </LazyPage>
        ),
      },
      { path: 'assistente', element: <LazyPage><AssistantPage /></LazyPage> },
      { path: 'estoque', element: <LazyPage><StockSituationPage /></LazyPage> },
      { path: 'sugestoes', element: <LazyPage><SuggestionsPage /></LazyPage> },
      { path: 'compras', element: <LazyPage><PurchasesPage /></LazyPage> },
      { path: 'negativos', element: <LazyPage><NegativeStockPage /></LazyPage> },
      { path: 'alertas', element: <LazyPage><AlertsPage /></LazyPage> },
      { path: 'produtos', element: <LazyPage><ProductsPage /></LazyPage> },
      { path: 'marcas', element: <LazyPage><BrandsPage /></LazyPage> },
      { path: 'categorias', element: <LazyPage><CategoriesPage /></LazyPage> },
      { path: 'lojas', element: <LazyPage><StoresPage /></LazyPage> },
      {
        path: 'usuarios',
        element: (
          <LazyPage roles={adminOnly}>
            <UsersPage />
          </LazyPage>
        ),
      },
      {
        path: 'auditoria',
        element: (
          <LazyPage roles={adminOnly}>
            <AuditPage />
          </LazyPage>
        ),
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]

function NotFoundPage() {
  return (
    <Card>
      <EmptyState
        icon={Compass}
        title="Página não encontrada"
        description="O endereço acessado não existe."
        action={
          <Link to="/" className="text-body text-primary hover:underline">
            Voltar ao Dashboard
          </Link>
        }
      />
    </Card>
  )
}
