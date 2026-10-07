import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '@/lib/api'
import { AuthContext } from './AuthProvider'
import { LoginPage } from './LoginPage'

function renderLogin(login = vi.fn()) {
  const router = createMemoryRouter([{ path: '/', element: <LoginPage /> }])
  render(
    <AuthContext value={{ status: 'anonymous', user: null, login, logout: vi.fn() }}>
      <RouterProvider router={router} />
    </AuthContext>,
  )
  return login
}

describe('LoginPage', () => {
  it('valida os campos antes de chamar a API', async () => {
    const login = renderLogin()

    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByText('Informe o e-mail.')).toBeInTheDocument()
    expect(screen.getByText('Informe a senha.')).toBeInTheDocument()
    expect(login).not.toHaveBeenCalled()
  })

  it('mostra a mensagem de erro da API', async () => {
    renderLogin(vi.fn().mockRejectedValue(new ApiError(401, 'E-mail ou senha inválidos.')))

    await userEvent.type(screen.getByLabelText('E-mail'), 'ana@example.com')
    await userEvent.type(screen.getByLabelText('Senha'), 'errada')
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('E-mail ou senha inválidos.')
  })
})
