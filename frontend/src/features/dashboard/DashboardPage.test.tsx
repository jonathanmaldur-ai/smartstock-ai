import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { AuthContext } from '@/features/auth/AuthProvider'
import { apiRequest } from '@/lib/api'
import type { Dashboard } from './dashboardApi'
import { DashboardPage } from './DashboardPage'
import { shortagePercent } from './dashboardApi'

const dashboard: Dashboard = {
  freshness: {
    stockDate: '2026-09-10',
    stockAgeDays: 18,
    stockOutdated: true,
    salesDate: '2026-09-23',
    salesAgeDays: 5,
    salesOutdated: false,
    transfersDate: '2026-09-23',
    transfersAgeDays: 5,
    transfersOutdated: false,
  },
  kpis: {
    analysisDate: '2026-09-28',
    stockDate: '2026-09-10',
    stockUnits: 1446939,
    sold12Months: 1042011,
    annualTurnover: 0.72,
    networkCoverageDays: 507,
    relevantRuptures: 5940,
    pendingSuggestions: 5751,
    pendingUnits: 19855,
    negativeItems: 15987,
  },
  stores: [
    { storeId: 1, code: '01', name: 'Matriz', type: 'Store', stockUnits: 264529, sold12Months: 184394, coverageDays: 523, rupture: 30, belowMinimum: 10, normal: 50, excess: 10, stagnant: 5 },
    { storeId: 5, code: '05', name: 'Depósito', type: 'Warehouse', stockUnits: 434945, sold12Months: 0, coverageDays: null, rupture: 0, belowMinimum: 0, normal: 0, excess: 0, stagnant: 0 },
  ],
  topProducts: [{ productId: 9, code: '0027084120134', description: 'HW CARROS BASICOS', brandName: 'MATTEL', sold12Months: 24372, stockUnits: 11160, ruptureStores: 3 }],
  trend: [{ analysisDate: '2026-09-28', stockDate: '2026-09-10', relevantRuptures: 5940, negativeItems: 15987, suggestions: 5751 }],
}

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

function renderDashboard() {
  const user = { id: '1', email: 'x@example.com', fullName: 'Usuário Teste', role: 'Consulta' as const }
  const router = createMemoryRouter([{ path: '/', element: <DashboardPage /> }])
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthContext value={{ status: 'authenticated', user, login: vi.fn(), logout: vi.fn() }}>
        <ToastProvider>
          <RouterProvider router={router} />
        </ToastProvider>
      </AuthContext>
    </QueryClientProvider>,
  )
}

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.mocked(apiRequest).mockReset()
    vi.mocked(apiRequest).mockImplementation(() => Promise.resolve(dashboard))
  })

  it('mostra os KPIs e avisa que o estoque está desatualizado', async () => {
    renderDashboard()

    expect(await screen.findByRole('link', { name: /Rupturas que importam/ })).toBeInTheDocument()
    expect(screen.getByText('5.940')).toBeInTheDocument()
    expect(screen.getByText('0,7×')).toBeInTheDocument()
    expect(screen.getByText('Dados desatualizados')).toBeInTheDocument()
    expect(screen.getByText(/estoque de 10\/09\/2026 \(18 dias/)).toBeInTheDocument()
  })

  it('gráfico de falta por loja ignora o Depósito e tem visão em tabela', async () => {
    renderDashboard()

    expect(await screen.findByRole('button', { name: 'Ver tabela: Itens em falta por loja' })).toBeInTheDocument()
    expect(screen.getByLabelText('01 Matriz: 40%')).toBeInTheDocument()
    expect(screen.queryByLabelText(/05 Depósito: /)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Ver tabela: Itens em falta por loja' }))
    expect(screen.getByRole('table', { name: 'Itens em falta por loja' })).toBeInTheDocument()
  })

  it('tendência com uma análise só explica quando a linha aparece', async () => {
    renderDashboard()
    expect((await screen.findAllByText(/A linha aparece a partir da segunda análise/)).length).toBe(3)
  })
})

describe('shortagePercent', () => {
  it('considera só os itens que vendem (ruptura, abaixo do mínimo, normal e excesso)', () => {
    expect(shortagePercent(dashboard.stores[0]!)).toBeCloseTo(40)
  })
})
