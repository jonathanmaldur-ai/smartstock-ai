import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from '@/lib/api'
import { AssistantPage } from './AssistantPage'

vi.mock('@/lib/api', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/lib/api')>()),
  apiRequest: vi.fn(),
}))

const api = vi.mocked(apiRequest)

function renderChat() {
  const router = createMemoryRouter([{ path: '/', element: <AssistantPage /> }])
  render(
    <QueryClientProvider client={new QueryClient()}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('AssistantPage', () => {
  beforeEach(() => {
    api.mockReset()
    api.mockResolvedValue({
      text: '312 itens em ruptura na 09 Buriti Shopping. Os mais importantes:',
      understood: 'Entendi: rupturas · loja 09 Buriti Shopping',
      table: { columns: ['Produto', 'Loja'], rows: [['HW CARROS BASICOS (0027084120134)', '09 Buriti Shopping']] },
      links: [{ label: 'Situação do estoque', href: '/estoque' }],
      suggestions: ['O que transferir para Buriti Shopping?'],
    })
  })

  it('envia a pergunta e mostra a resposta com o que entendeu, a tabela e o link', async () => {
    renderChat()

    await userEvent.type(screen.getByLabelText('Sua pergunta'), 'rupturas no buriti')
    await userEvent.click(screen.getByRole('button', { name: 'Perguntar' }))

    expect(api).toHaveBeenCalledWith('/assistant', { method: 'POST', body: { question: 'rupturas no buriti' } })
    expect(await screen.findByText('Entendi: rupturas · loja 09 Buriti Shopping')).toBeInTheDocument()
    expect(screen.getByText('HW CARROS BASICOS (0027084120134)')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Abrir Situação do estoque →' })).toHaveAttribute('href', '/estoque')
  })

  it('não quebra quando o navegador faz scrollIntoView devolver uma Promise (Chrome recente)', async () => {
    Element.prototype.scrollIntoView = vi.fn(() => Promise.resolve()) as unknown as Element['scrollIntoView']
    try {
      renderChat()
      await userEvent.click(screen.getByRole('button', { name: 'Rupturas no Buriti' }))
      expect(await screen.findByText('Entendi: rupturas · loja 09 Buriti Shopping')).toBeInTheDocument()
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('exemplos e sugestões viram perguntas com um clique', async () => {
    renderChat()

    await userEvent.click(screen.getByRole('button', { name: 'Rupturas no Buriti' }))
    await userEvent.click(await screen.findByRole('button', { name: 'O que transferir para Buriti Shopping?' }))

    expect(api).toHaveBeenLastCalledWith('/assistant', { method: 'POST', body: { question: 'O que transferir para Buriti Shopping?' } })
  })
})
