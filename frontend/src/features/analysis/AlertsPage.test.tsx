import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { AuthContext } from '@/features/auth/AuthProvider'
import { apiRequest, type Role } from '@/lib/api'
import type { AlertSummary, StockAlert } from './alertsApi'
import { AlertsPage } from './AlertsPage'

const summary: AlertSummary = {
  analysisId: 'a2',
  stockDate: '2026-09-28',
  previousStockDate: '2026-09-23',
  types: [
    { type: 'SoldWithoutReplenishment', label: 'Vende todo dia sem reposição', priority: 'High', total: 14, unseen: 14 },
    { type: 'StoreImbalance', label: 'Sobra numa loja e falta em outra', priority: 'Medium', total: 2447, unseen: 2400 },
  ],
}

const alert: StockAlert = {
  id: 7,
  type: 'SoldWithoutReplenishment',
  typeLabel: 'Vende todo dia sem reposição',
  priority: 'High',
  productId: 1,
  productCode: '0027084120134',
  productDescription: 'HW CARROS BASICOS',
  storeCode: '06',
  storeName: 'Mogi Mirim',
  brandName: 'MATTEL',
  message: 'Vende 66,8 por dia e o estoque caiu de 120 para 0 un. desde 23/09, sem nenhuma transferência recebida.',
  seenAt: null,
  seenByEmail: null,
}

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

const api = vi.mocked(apiRequest)

function renderPage(role: Role = 'Gerente') {
  const user = { id: '1', email: 'x@example.com', fullName: 'X', role }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthContext value={{ status: 'authenticated', user, login: vi.fn(), logout: vi.fn() }}>
        <ToastProvider>
          <AlertsPage />
        </ToastProvider>
      </AuthContext>
    </QueryClientProvider>,
  )
}

describe('AlertsPage', () => {
  beforeEach(() => {
    api.mockReset()
    api.mockImplementation((path: string) => {
      if (path === '/alerts/summary') return Promise.resolve(summary)
      if (path.startsWith('/alerts?')) return Promise.resolve({ items: [alert], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
      if (path === '/alerts/seen') return Promise.resolve({ decided: 1 })
      if (path === '/analysis') return Promise.resolve({ analysisId: 'a2', isOutdated: false, situations: [], suggestions: {} })
      return Promise.resolve([])
    })
  })

  it('mostra os tipos com a data comparada e filtra ao clicar', async () => {
    renderPage()

    expect(await screen.findByText(/Comparado com o estoque de 23\/09\/2026/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /Sobra numa loja e falta em outra/ }))

    expect(api).toHaveBeenCalledWith(expect.stringContaining('type=StoreImbalance'))
  })

  it('marca os selecionados como vistos', async () => {
    renderPage()

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Marcar HW CARROS BASICOS' }))
    await userEvent.click(screen.getByRole('button', { name: 'Marcar como visto (1)' }))

    expect(api).toHaveBeenCalledWith('/alerts/seen', { method: 'POST', body: { ids: [7], seen: true } })
  })

  it('perfil Consulta vê os alertas mas não marca', async () => {
    renderPage('Consulta')

    expect(await screen.findByText(/caiu de 120 para 0/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Marcar como visto/ })).not.toBeInTheDocument()
  })
})
