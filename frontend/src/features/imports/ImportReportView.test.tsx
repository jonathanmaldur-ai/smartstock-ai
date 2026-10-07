import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Switch } from '@/components/ui/Switch'
import { ToastProvider } from '@/components/ui/Toast'
import { ImportReportView, reportScope } from './ImportReportView'
import type { ImportReport } from './importsApi'

const baseReport: ImportReport = {
  id: '1',
  type: 'Products',
  status: 'Validated',
  fileName: 'Produtos.xlsx',
  uploadedAt: '2026-09-24T12:00:00Z',
  uploadedByEmail: 'admin@example.com',
  totalRows: 60073,
  validRows: 59987,
  errorRows: 86,
  warningCount: 138,
  rejectionReason: null,
  duplicateOfBatchId: null,
  decidedAt: null,
  decidedByEmail: null,
  referenceDate: null,
  storeCode: null,
  storeName: null,
  periodStart: null,
  periodEnd: null,
  replacedRecords: 0,
  issueGroups: [
    { severity: 'Error', code: 'product.brand_unknown', message: 'Marca não cadastrada.', count: 86, sampleValues: ['036600', '034700'] },
  ],
}

function renderReport(report: ImportReport) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <ToastProvider>
        <ImportReportView report={report} onDecided={vi.fn()} />
      </ToastProvider>
    </QueryClientProvider>,
  )
}

describe('ImportReportView', () => {
  it('mostra os totais, os exemplos e pede confirmação quando está pendente', () => {
    renderReport(baseReport)

    expect(screen.getByText('59.987')).toBeInTheDocument()
    expect(screen.getByText('036600 · 034700')).toBeInTheDocument()
    expect(screen.getByText(/As 86 linhas com erro serão ignoradas/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Gravar dados' })).toBeInTheDocument()
  })

  it('arquivo recusado mostra o motivo e não permite gravar', () => {
    renderReport({ ...baseReport, status: 'Rejected', rejectionReason: 'O arquivo foi cortado.', issueGroups: [] })

    expect(screen.getByText('O arquivo foi cortado.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Gravar dados' })).not.toBeInTheDocument()
  })
})

describe('Switch', () => {
  it('alterna e anuncia o estado para leitores de tela', async () => {
    const onChange = vi.fn()
    render(<Switch checked={false} onChange={onChange} label="Nas análises" />)

    const control = screen.getByRole('switch', { name: 'Nas análises' })
    expect(control).toHaveAttribute('aria-checked', 'false')
    await userEvent.click(control)
    expect(onChange).toHaveBeenCalledWith(true)
  })
})

describe('reportScope', () => {
  it('resume loja, data e período sem deslocar o fuso', () => {
    const sales: ImportReport = {
      ...baseReport,
      type: 'Sales',
      storeCode: '04',
      storeName: 'Jaguariúna',
      referenceDate: '2026-09-23',
      periodStart: '2025-09-23',
      periodEnd: '2026-09-23',
    }

    expect(reportScope(sales)).toBe('Loja 04 Jaguariúna · dados de 23/09/2026 · período 23/09/2025 a 23/09/2026')
    expect(reportScope(baseReport)).toBeNull()
  })

  it('avisa que transferências substituem o período já importado', () => {
    renderReport({ ...baseReport, type: 'Transfers', periodStart: '2026-03-02', periodEnd: '2026-03-31' })

    expect(screen.getByText(/entre 02\/03\/2026 e 31\/03\/2026/)).toBeInTheDocument()
  })
})
