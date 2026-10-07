import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { AuthContext } from '@/features/auth/AuthProvider'
import { apiRequest } from '@/lib/api'
import type { NegativeItem, NegativeSummary } from './negativesApi'
import { NegativeStockPage } from './NegativeStockPage'

const summary: NegativeSummary = {
  analysisId: 'a1',
  stockDate: '2026-09-30',
  previousStockDate: '2026-09-23',
  items: 15987,
  units: -390831,
  criticalItems: 9279,
  stores: [
    { storeId: 6, code: '06', name: 'Mogi Mirim', items: 2000, units: -95368, criticalItems: 945, percentOfItems: 12.5, previousItems: 2064, previousUnits: -95000 },
  ],
  causes: [{ cause: 'SaleWithoutEntry', items: 7278 }],
}

const item: NegativeItem = {
  productId: 1,
  productCode: '7899776000597',
  productDescription: 'ESTALOS DE SALAO FANTASMINHA',
  productReference: '2448',
  brandName: 'FANTASMINHA',
  categoryName: 'BRINQUEDOS',
  storeCode: '09',
  storeName: 'Buriti Shopping',
  quantity: -1551,
  sold12Months: 2006,
  priority: 'Critical',
  causes: ['SaleWithoutEntry'],
  pendingTransferUnits: 12,
  duplicateProductCode: null,
}

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

const api = vi.mocked(apiRequest)

function renderPage() {
  const user = { id: '1', email: 'x@example.com', fullName: 'X', role: 'Administrador' as const }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthContext value={{ status: 'authenticated', user, login: vi.fn(), logout: vi.fn() }}>
        <ToastProvider>
          <NegativeStockPage />
        </ToastProvider>
      </AuthContext>
    </QueryClientProvider>,
  )
}

describe('NegativeStockPage', () => {
  beforeEach(() => {
    api.mockReset()
    api.mockImplementation((path: string) => {
      if (path === '/analysis/negatives/summary') return Promise.resolve(summary)
      if (path.startsWith('/analysis/negatives/ranking')) return Promise.resolve([{ id: 1, name: 'FANTASMINHA', items: 10, units: -3000 }])
      if (path.startsWith('/analysis/negatives?')) return Promise.resolve({ items: [item], page: 1, pageSize: 25, totalCount: 1, totalPages: 1 })
      if (path === '/analysis') return Promise.resolve({ ...summary, isOutdated: false, situations: [], suggestions: {}, analysisDate: '2026-09-30', createdAt: '2026-09-30T10:00:00Z' })
      return Promise.resolve([])
    })
  })

  it('mostra a loja com a tendência contra a importação anterior e as causas', async () => {
    renderPage()

    const row = (await screen.findByRole('button', { name: '06 Mogi Mirim' })).closest('tr')!
    expect(within(row).getByText('-64 itens')).toHaveClass('text-success')
    expect(screen.getByRole('button', { name: /Vende sem entrada.*7.278/ })).toBeInTheDocument()
    expect(await screen.findByText('12 un. enviadas sem entrada')).toBeInTheDocument()
  })

  it('o cartão de críticos filtra a lista', async () => {
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /Críticos/ }))

    expect(api).toHaveBeenCalledWith(expect.stringContaining('priority=Critical'))
  })
})
