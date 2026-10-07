import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { Role } from '@/lib/api'
import { AuthContext } from './AuthProvider'
import { RequireAuth, RequireRole } from './RouteGuards'

function renderAt(path: string, auth: { status: 'anonymous' | 'authenticated'; role?: Role }) {
  const router = createMemoryRouter(
    [
      { path: '/login', element: <p>Tela de login</p> },
      {
        path: '/',
        element: (
          <RequireAuth>
            <p>Dashboard</p>
          </RequireAuth>
        ),
      },
      {
        path: '/usuarios',
        element: (
          <RequireAuth>
            <RequireRole roles={['Administrador']}>
              <p>Usuários</p>
            </RequireRole>
          </RequireAuth>
        ),
      },
    ],
    { initialEntries: [path] },
  )
  const user = auth.role ? { id: '1', email: 'x@example.com', fullName: 'X', role: auth.role } : null
  render(
    <AuthContext value={{ status: auth.status, user, login: vi.fn(), logout: vi.fn() }}>
      <RouterProvider router={router} />
    </AuthContext>,
  )
}

describe('proteção de rotas', () => {
  it('manda visitante anônimo para o login', () => {
    renderAt('/', { status: 'anonymous' })
    expect(screen.getByText('Tela de login')).toBeInTheDocument()
  })

  it.each<Role>(['Gerente', 'Operador', 'Consulta'])('perfil %s não acessa a tela de usuários', (role) => {
    renderAt('/usuarios', { status: 'authenticated', role })
    expect(screen.getByText('Dashboard')).toBeInTheDocument()
  })

  it('Administrador acessa a tela de usuários', () => {
    renderAt('/usuarios', { status: 'authenticated', role: 'Administrador' })
    expect(screen.getByText('Usuários')).toBeInTheDocument()
  })
})
