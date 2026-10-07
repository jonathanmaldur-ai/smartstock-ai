import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { AuthContext } from '@/features/auth/AuthProvider'
import type { Role } from '@/lib/api'
import type { ProductOverview } from './catalogApi'
import { ProductOverviewView } from './ProductOverviewView'

const overview: ProductOverview = {
  product: {
    id: 1,
    code: '7898534538969',
    description: 'BRINQUEDO VEICULOS - CARRINHO',
    unit: 'PC',
    reference: 'R3439',
    isActive: true,
    brandCode: '001900',
    brandName: 'MARCA TESTE',
    categoryName: 'BRINQUEDOS',
    categoryExcluded: false,
    subcategoryName: null,
    isSeasonal: false,
  },
  stockDate: '2026-09-23',
  stores: [
    { storeCode: '01', storeName: 'Matriz', storeType: 'Store', stock: 8, sold12Months: 1095, salesPeriodEnd: '2026-09-23', dailyAverage: 3, coverageDays: 2.67 },
    { storeCode: '05', storeName: 'Depósito', storeType: 'Warehouse', stock: -12, sold12Months: null, salesPeriodEnd: null, dailyAverage: null, coverageDays: null },
  ],
  recentTransfers: [
    { date: '2026-03-30', isCancellation: true, originCode: '01', destinationCode: '11', direction: 'In', quantity: 32, userName: 'USUARIO1' },
  ],
}

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(() => Promise.resolve(overview)),
}))

function renderOverview(role: Role = 'Consulta') {
  const user = { id: '1', email: 'x@example.com', fullName: 'X', role }
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <AuthContext value={{ status: 'authenticated', user, login: vi.fn(), logout: vi.fn() }}>
        <ToastProvider>
          <ProductOverviewView productId={1} />
        </ToastProvider>
      </AuthContext>
    </QueryClientProvider>,
  )
}

describe('ProductOverviewView', () => {
  it('mostra estoque, VMD e cobertura por loja, com o total da rede', async () => {
    renderOverview()

    const matriz = (await screen.findByText('01 Matriz')).closest('tr')!
    expect(within(matriz).getByText('3 dias')).toBeInTheDocument()
    expect(within(matriz).getByText('1.095')).toBeInTheDocument()
    expect(screen.getByText('Estoque de 23/09/2026')).toBeInTheDocument()

    const rede = screen.getByText('Rede').closest('tr')!
    expect(within(rede).getByText('-4')).toBeInTheDocument()
  })

  it('destaca estoque negativo e mostra os cancelamentos', async () => {
    renderOverview()

    expect(await screen.findByText('-12')).toHaveClass('text-error')
    expect(screen.getByText('Cancelamento')).toBeInTheDocument()
    expect(screen.getByText('01 → 11')).toBeInTheDocument()
  })

  it('só o administrador vê a opção de produto sazonal', async () => {
    renderOverview('Administrador')
    expect(await screen.findByRole('switch', { name: 'Sazonal / fora de época' })).toBeInTheDocument()
  })

  it('outros perfis não alteram o produto', async () => {
    renderOverview('Gerente')
    await screen.findByText('01 Matriz')
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
  })
})
