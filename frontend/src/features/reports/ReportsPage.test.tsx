import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ToastProvider } from '@/components/ui/Toast'
import { apiRequest } from '@/lib/api'
import { ReportPrintPage } from './ReportPrintPage'
import { ReportsPage } from './ReportsPage'
import type { DailySalesReport, MonthlyReport, ReportCatalog, ReportData } from './reportsApi'

const totals = (net: number, sales: number) => ({
  net,
  gross: net,
  sales,
  pieces: sales * 2,
  days: 30,
  averageTicket: net / sales,
  piecesPerSale: 2,
  netPerDay: net / 30,
})

const daily: DailySalesReport = {
  from: '2026-09-01',
  to: '2026-09-30',
  previousFrom: '2026-08-02',
  previousTo: '2026-08-31',
  firstAvailable: '2026-07-01',
  lastAvailable: '2026-09-30',
  scope: 'Rede toda',
  current: totals(110000, 1000),
  previous: totals(100000, 1000),
  days: [
    { date: '2026-09-29', net: 3000, sales: 30, pieces: 60 },
    { date: '2026-09-30', net: 4000, sales: 40, pieces: 80 },
  ],
  stores: [
    {
      storeId: 11,
      code: '11',
      name: 'Taubaté',
      net: 50000,
      sales: 400,
      pieces: 800,
      days: 30,
      averageTicket: 125,
      previousNet: 40000,
      bestDay: '2026-09-12',
      bestDayNet: 4200,
    },
  ],
  weekdays: ['Domingo', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado'].map((label, dayOfWeek) => ({
    dayOfWeek,
    label,
    averageNet: dayOfWeek === 6 ? 6000 : 3000,
    averageSales: 30,
    days: 4,
  })),
  products: {
    available: true,
    total: 1520,
    items: [{ productId: 7, code: '0027084120134', description: 'HW CARROS BASICOS', brand: 'MATTEL', quantity: 812, amount: 11586.5, days: 30, stores: 10 }],
  },
}

const catalog: ReportCatalog = {
  reports: [
    { key: 'ruptura', title: 'Ruptura', description: 'Produtos sem estoque que vendem.', group: 'Situação do estoque' },
    { key: 'por-loja', title: 'Cobertura e giro por loja', description: 'Por loja.', group: 'Cobertura e giro' },
  ],
  brands: [{ id: 3, name: 'MATTEL' }],
}

const report: ReportData = {
  key: 'ruptura',
  title: 'Ruptura',
  description: 'Produtos sem estoque que vendem.',
  filterSummary: 'Rede toda',
  analysisDate: '2026-09-28',
  stockDate: '2026-09-23',
  columns: [
    { header: 'Loja', kind: 'Text' },
    { header: 'Código', kind: 'Text' },
    { header: 'Venda 12 meses', kind: 'Decimal' },
  ],
  rows: [['09 Buriti Shopping', '0027084120134', 4177]],
  totalRows: 34264,
}

const analysis = (id: string, stockDate: string) => ({ id, analysisDate: stockDate, stockDate, createdAt: `${stockDate}T10:00:00Z` })
const metrics = (relevantRuptures: number) => ({ relevantRuptures, negativeItems: 0, excess: 0, stagnant: 0, stockUnits: 100, coverageDays: 30 })

const monthly: MonthlyReport = {
  current: analysis('b', '2026-09-23'),
  previous: analysis('a', '2026-08-23'),
  options: [analysis('a', '2026-08-23')],
  indicators: [
    { name: 'Rupturas que importam', current: 80, previous: 100, lowerIsBetter: true, unit: 'itens' },
    { name: 'Itens com estoque negativo', current: 60, previous: 50, lowerIsBetter: true, unit: 'itens' },
  ],
  stores: [{ code: '06', name: 'Mogi Mirim', current: metrics(5), previous: metrics(8) }],
  transfers: {
    since: '2026-08-23T10:00:00Z',
    approved: 10,
    approvedUnits: 200,
    completed: 7,
    completedUnits: 150,
    rejected: 2,
    pendingApproval: 30,
    precisionPercent: 70,
    averageDaysToComplete: 3.5,
  },
}
vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

const api = vi.mocked(apiRequest)

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { path: '/relatorios', element: <ReportsPage /> },
      { path: '/relatorios/imprimir', element: <ReportPrintPage /> },
    ],
    { initialEntries: [path] },
  )
  render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <ToastProvider>
        <RouterProvider router={router} />
      </ToastProvider>
    </QueryClientProvider>,
  )
}

describe('Relatórios', () => {
  beforeEach(() => {
    api.mockReset()
    api.mockImplementation((path: string) => {
      if (path === '/reports') return Promise.resolve(catalog)
      if (path.startsWith('/reports/ruptura')) return Promise.resolve(report)
      if (path.startsWith('/reports/mensal')) return Promise.resolve(monthly)
      if (path.startsWith('/reports/vendas-diarias/produtos/vendas'))
        return Promise.resolve([
          { date: '2026-09-30', storeCode: '01', storeName: 'Matriz', saleNumber: '0163646300', quantity: 12, unitPrice: 34.98, amount: 419.76, saleTotal: 10615.01, saleItems: 64, registeredCustomer: true, payment: 'CREDITO 2X', fiscalDocument: 'NF-e nº 007420' },
        ])
      if (path.startsWith('/reports/vendas-diarias/produtos'))
        return Promise.resolve({
          items: [{ date: '2026-09-30', storeId: 1, storeCode: '01', storeName: 'Matriz', productId: 9, code: '7908402412304', description: 'CESTINHA PICNIC', brand: 'MARCA TESTE', quantity: 108, amount: 3777.84, sales: 9 }],
          page: 1,
          pageSize: 50,
          totalCount: 1,
          totalPages: 1,
        })
      if (path.startsWith('/reports/vendas-diarias')) return Promise.resolve(daily)
      return Promise.resolve([])
    })
  })

  it('lista os relatórios por assunto e mostra a prévia com aviso de linhas', async () => {
    renderAt('/relatorios')

    expect(await screen.findByText('Situação do estoque')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Ruptura' }))

    expect(await screen.findByText('0027084120134')).toBeInTheDocument()
    expect(screen.getByText('4.177')).toBeInTheDocument()
    expect(screen.getByText(/O Excel e o CSV trazem todas as 34\.264/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Imprimir / PDF' })).toBeInTheDocument()
  })

  it('a versão de impressão usa o relatório e os filtros da URL e abre a impressão', async () => {
    const print = vi.fn()
    window.print = print
    renderAt('/relatorios/imprimir?relatorio=ruptura&brandId=3')

    expect(await screen.findByRole('heading', { name: 'Ruptura' })).toBeInTheDocument()
    expect(api).toHaveBeenCalledWith('/reports/ruptura?brandId=3')
    await vi.waitFor(() => expect(print).toHaveBeenCalled())
  })

  it('o relatório mensal mostra a variação com seta e sinal e o desempenho das transferências', async () => {
    renderAt('/relatorios')

    await userEvent.click(await screen.findByRole('button', { name: 'Relatório mensal' }))

    expect(await screen.findByRole('cell', { name: 'Rupturas que importam' })).toBeInTheDocument()
    expect(screen.getByTitle('Piorou')).toHaveTextContent('+10')
    expect(screen.getAllByTitle('Melhorou').map((e) => e.textContent)).toEqual(['−20', '−3'])
    expect(screen.getByText('70%')).toBeInTheDocument()
    expect(screen.getByLabelText('Comparar com')).toHaveValue('a')
  })

  it('imprime o relatório mensal comparando com a análise da URL', async () => {
    window.print = vi.fn()
    renderAt('/relatorios/imprimir?relatorio=mensal&comparar=a')

    expect(await screen.findByRole('heading', { name: 'Relatório mensal' })).toBeInTheDocument()
    expect(api).toHaveBeenCalledWith('/reports/mensal?comparar=a')
  })

  it('vendas por dia mostra a variação contra o período anterior e o melhor dia da semana', async () => {
    renderAt('/relatorios')

    await userEvent.click(await screen.findByRole('button', { name: 'Vendas por dia' }))

    expect((await screen.findAllByText('+10%')).length).toBe(3) // valor, ticket médio e média por dia
    expect(screen.getByText('+25%')).toBeInTheDocument()
    expect(screen.getByText(/Melhor dia: Sábado/)).toBeInTheDocument()
    expect(screen.getByText('HW CARROS BASICOS')).toBeInTheDocument()
    expect(screen.getByText(/1.520 produtos vendidos no período/)).toBeInTheDocument()
    expect(await screen.findByText('CESTINHA PICNIC')).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: '01 Matriz' })).toBeInTheDocument() // rede toda: mostra a loja de cada dia
    expect(screen.getByRole('cell', { name: /34,98/ })).toBeInTheDocument() // preço médio
    await userEvent.click(screen.getByRole('button', { name: /Ver as vendas de CESTINHA PICNIC/ }))
    expect(await screen.findByText('0163646300')).toBeInTheDocument()
    expect(screen.getByText('Cadastrado')).toBeInTheDocument()
    expect(api).toHaveBeenCalledWith('/reports/vendas-diarias/produtos/vendas?productId=9&de=2026-09-30&ate=2026-09-30&storeId=1')
    await userEvent.keyboard('{Escape}')
    await userEvent.click(screen.getByRole('button', { name: 'Ver as vendas de HW CARROS BASICOS' }))
    expect(api).toHaveBeenCalledWith('/reports/vendas-diarias/produtos/vendas?productId=7&de=2026-09-01&ate=2026-09-30')
    await userEvent.click(screen.getByRole('button', { name: 'Últimos 7 dias' }))
    expect(api).toHaveBeenCalledWith('/reports/vendas-diarias?de=2026-09-24&ate=2026-09-30')
  })
})
