import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { AuthContext } from '@/features/auth/AuthProvider'
import { apiRequest, type Role } from '@/lib/api'
import type { AnalysisSummary, Suggestion, SuggestionRoute } from './analysisApi'
import { SuggestionsPage } from './SuggestionsPage'

const route: SuggestionRoute = {
  originStoreId: 5,
  originCode: '05',
  originName: 'Depósito',
  destinationStoreId: 9,
  destinationCode: '09',
  destinationName: 'Buriti',
  count: 312,
  units: 2400,
  criticalCount: 40,
  negativeCount: 0,
  topProducts: [{ description: 'HW CARROS BASICOS', quantity: 344 }],
}

const suggestion: Suggestion = {
  id: 7,
  productId: 1,
  productCode: '0027084120134',
  productDescription: 'HW CARROS BASICOS',
  productReference: 'C4982',
  brandName: 'MATTEL',
  categoryName: 'BRINQUEDOS',
  originCode: '05',
  originName: 'Depósito',
  destinationCode: '09',
  destinationName: 'Buriti',
  quantity: 344,
  priority: 'Critical',
  status: 'Suggested',
  reason: '09 Buriti está sem estoque e vende 11,44 un./dia.',
  originStock: 11160,
  originDailyAverage: 0,
  originCoverageDays: null,
  destinationStock: -6,
  destinationDailyAverage: 11.44,
  destinationCoverageDays: 0,
  destinationCoverageAfter: 30,
  destinationNegative: true,
  analysisDate: '2026-09-28',
  decidedAt: null,
  decidedByEmail: null,
  decisionNote: null,
  completedOn: null,
  completedQuantity: null,
}

const summary: AnalysisSummary = {
  analysisId: 'a1',
  stockDate: '2026-09-23',
  analysisDate: '2026-09-28',
  createdAt: '2026-09-28T12:00:00Z',
  createdByEmail: 'admin@example.com',
  isOutdated: false,
  situations: [],
  relevantRuptures: 5940,
  suggestions: { pending: 1, pendingUnits: 344, approved: 0, approvedUnits: 0, rejected: 0, completed: 0 },
  purchaseCount: 0,
  purchaseUnits: 0,
}

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

const api = vi.mocked(apiRequest)

function renderPage(role: Role) {
  const user = { id: '1', email: 'x@example.com', fullName: 'X', role }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthContext value={{ status: 'authenticated', user, login: vi.fn(), logout: vi.fn() }}>
        <ToastProvider>
          <SuggestionsPage />
        </ToastProvider>
      </AuthContext>
    </QueryClientProvider>,
  )
}

describe('SuggestionsPage', () => {
  beforeEach(() => {
    api.mockReset()
    api.mockImplementation((path: string) => {
      if (path.startsWith('/transfer-suggestions/routes?')) return Promise.resolve([route])
      if (path.startsWith('/transfer-suggestions?')) return Promise.resolve({ items: [suggestion], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
      if (path === '/transfer-suggestions/decision') return Promise.resolve({ decided: 312 })
      if (path === '/analysis') return Promise.resolve(summary)
      return Promise.resolve([])
    })
  })

  it('abre agrupada por rota, ocultando destinos com estoque negativo', async () => {
    renderPage('Consulta')

    expect(await screen.findByRole('button', { name: '05 Depósito → 09 Buriti' })).toBeInTheDocument()
    expect(screen.getByText('HW CARROS BASICOS (344 un.) · + 311 produtos')).toBeInTheDocument()
    expect(api).toHaveBeenCalledWith(expect.stringContaining('hideNegativeDestination=true'))
    expect(screen.queryByRole('button', { name: 'Aprovar rota' })).not.toBeInTheDocument()
  })

  it('administrador aprova a rota inteira', async () => {
    renderPage('Administrador')

    await userEvent.click(await screen.findByRole('button', { name: 'Aprovar rota' }))
    const dialog = screen.getByRole('dialog', { name: 'Aprovar sugestões' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Aprovar' }))

    expect(api).toHaveBeenCalledWith('/transfer-suggestions/decision', {
      method: 'POST',
      body: { approve: true, note: undefined, originStoreId: 5, destinationStoreId: 9, hideNegativeDestination: true },
    })
  })

  it('dentro da rota mostra o estoque do ERP e o aviso de negativo', async () => {
    renderPage('Consulta')

    await userEvent.click(await screen.findByRole('button', { name: '05 Depósito → 09 Buriti' }))

    expect(await screen.findByText('HW CARROS BASICOS')).toBeInTheDocument()
    expect(screen.getByText('-6 un.')).toBeInTheDocument()
    expect(screen.getByText('Confirme o estoque físico')).toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })
})
