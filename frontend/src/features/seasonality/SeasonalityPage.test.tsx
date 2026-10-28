import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { AuthContext } from '@/features/auth/AuthProvider'
import { apiRequest, type Role } from '@/lib/api'
import { SeasonalityPage } from './SeasonalityPage'
import type { SeasonalProduct, SeasonalSuggestion, Season } from './seasonalityApi'

const seasons: Season[] = [
  { id: 1, name: 'Natal', startMonth: 10, endMonth: 12, worldCupOnly: false, keywords: ['NATAL'], matchDescription: true, isActive: true, inSeasonNow: true, productCount: 3, pendingSuggestions: 1 },
  { id: 2, name: 'Copa do Mundo', startMonth: 5, endMonth: 7, worldCupOnly: true, keywords: ['FIGURINHAS'], matchDescription: true, isActive: true, inSeasonNow: false, productCount: 0, pendingSuggestions: 1 },
]

const suggestion = (id: number, seasonId: number | null, description: string): SeasonalSuggestion => ({
  id,
  productId: id * 10,
  code: `790${id}`,
  description,
  brand: 'PANINI',
  category: 'FIGURINHAS',
  seasonId,
  seasonName: seasons.find((s) => s.id === seasonId)?.name ?? null,
  kind: 'SalesStopped',
  evidence: 'Vendeu 2.532 un. em 12 meses e 3 nos últimos 60 dias',
  stock: 4800,
  sold12Months: 2532,
  updatedAt: '2026-10-28T09:00:00Z',
})

const product: SeasonalProduct = {
  productId: 10,
  code: '7901',
  description: 'ENVELOPE FIGURINHAS COPA 2026',
  brand: 'PANINI',
  category: 'FIGURINHAS',
  seasonId: 2,
  seasonName: 'Copa do Mundo',
  markedOutOfSeason: false,
  state: 'OutOfSeason',
  stock: 4800,
  sold12Months: 2532,
}

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

const api = vi.mocked(apiRequest)
const paged = <T,>(items: T[]) => ({ items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 })

function renderPage(role: Role = 'Administrador') {
  const user = { id: '1', email: 'x@example.com', fullName: 'X', role }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthContext value={{ status: 'authenticated', user, login: vi.fn(), logout: vi.fn() }}>
        <ToastProvider>
          <MemoryRouter>
            <SeasonalityPage />
          </MemoryRouter>
        </ToastProvider>
      </AuthContext>
    </QueryClientProvider>,
  )
}

describe('Sazonalidade', () => {
  beforeEach(() => {
    api.mockReset()
    api.mockImplementation((path: string) => {
      if (path === '/seasonality/seasons') return Promise.resolve(seasons)
      if (path.startsWith('/seasonality/suggestions/decision')) return Promise.resolve({ decided: 1 })
      if (path.startsWith('/seasonality/suggestions?seasonId=2'))
        return Promise.resolve(paged([suggestion(2, 2, 'ENVELOPE FIGURINHAS COPA 2026')]))
      if (path.startsWith('/seasonality/suggestions'))
        return Promise.resolve(paged([suggestion(1, 1, 'BOLA DE NATAL'), suggestion(2, 2, 'ENVELOPE FIGURINHAS COPA 2026')]))
      if (path.startsWith('/seasonality/products')) return Promise.resolve(paged([product]))
      return Promise.resolve([])
    })
  })

  it('mostra as épocas e as sugestões com o motivo, e confirma as marcadas', async () => {
    renderPage()

    expect(await screen.findByRole('button', { name: /Natal.*Em época/ })).toBeInTheDocument()
    expect(await screen.findByText('BOLA DE NATAL')).toBeInTheDocument()
    expect(screen.getAllByText(/3 nos últimos 60 dias/)).toHaveLength(2)

    await userEvent.click(screen.getByRole('checkbox', { name: 'Marcar BOLA DE NATAL' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar marcados' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar' }))

    await vi.waitFor(() =>
      expect(api).toHaveBeenCalledWith('/seasonality/suggestions/decision', { method: 'POST', body: { ids: [1], confirm: true } }),
    )
  })

  it('a época escolhida filtra as sugestões e permite decidir todas de uma vez', async () => {
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: /Copa do Mundo/ }))

    expect(await screen.findByRole('heading', { name: 'Sugestões: Copa do Mundo' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Confirmar todas' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Confirmar' }))
    await vi.waitFor(() =>
      expect(api).toHaveBeenCalledWith('/seasonality/suggestions/decision', { method: 'POST', body: { seasonId: 2, ids: [], confirm: true } }),
    )
  })

  it('produtos sazonais mostram se estão fora de época', async () => {
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Produtos sazonais' }))

    expect(await screen.findByText('ENVELOPE FIGURINHAS COPA 2026')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).getByText('Fora de época')).toBeInTheDocument()
  })

  it('quem não é Administrador só consulta', async () => {
    renderPage('Gerente')

    expect(await screen.findByText('BOLA DE NATAL')).toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirmar marcados' })).not.toBeInTheDocument()
  })
})
