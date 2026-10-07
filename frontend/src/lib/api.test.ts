import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiRequest, refreshSession, setSession, type Session } from './api'

const session: Session = {
  accessToken: 'novo-token',
  expiresAt: new Date().toISOString(),
  user: { id: '1', email: 'a@example.com', fullName: 'Ana', role: 'Consulta' },
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('api', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockReset()
    setSession(null)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('agrupa renovações simultâneas numa única chamada', async () => {
    fetchMock.mockResolvedValue(json(200, session))

    const [a, b] = await Promise.all([refreshSession(), refreshSession()])

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(a).toEqual(session)
    expect(b).toEqual(session)
  })

  it('renova a sessão ao receber 401 e repete a requisição com o novo token', async () => {
    setSession({ ...session, accessToken: 'token-vencido' })
    fetchMock
      .mockResolvedValueOnce(new Response(null, { status: 401 }))
      .mockResolvedValueOnce(json(200, session))
      .mockResolvedValueOnce(json(200, { ok: true }))

    const result = await apiRequest<{ ok: boolean }>('/users')

    expect(result).toEqual({ ok: true })
    const retryHeaders = fetchMock.mock.calls[2]?.[1]?.headers as Record<string, string>
    expect(retryHeaders.Authorization).toBe('Bearer novo-token')
  })

  it('usa a mensagem em português vinda do backend', async () => {
    fetchMock.mockResolvedValue(json(409, { title: 'O e-mail já está cadastrado.', code: 'user.email_taken' }))

    const error = await apiRequest('/users', { method: 'POST', body: {} }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).message).toBe('O e-mail já está cadastrado.')
    expect((error as ApiError).code).toBe('user.email_taken')
  })

  it('mostra o primeiro erro de campo em respostas de validação', async () => {
    fetchMock.mockResolvedValue(json(400, { title: 'Dados inválidos.', errors: { Email: ['E-mail inválido.'] } }))

    const error = (await apiRequest('/auth/login', { method: 'POST', body: {} }).catch((e: unknown) => e)) as ApiError

    expect(error.message).toBe('E-mail inválido.')
  })
})
