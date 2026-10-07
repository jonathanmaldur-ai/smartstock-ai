/**
 * Cliente HTTP da API.
 *
 * - O access token fica só em memória (nunca em localStorage).
 * - Em 401, tenta renovar a sessão uma única vez pelo cookie HttpOnly e repete a requisição.
 * - Renovações simultâneas são agrupadas numa só chamada.
 * - Erros viram ApiError com a mensagem em português vinda do backend (Problem Details).
 */

export type SessionUser = {
  id: string
  email: string
  fullName: string
  role: Role
}

export type Role = 'Administrador' | 'Gerente' | 'Operador' | 'Consulta'

export type Session = {
  accessToken: string
  expiresAt: string
  user: SessionUser
}

export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly fieldErrors?: Record<string, string[]>

  constructor(status: number, message: string, code?: string, fieldErrors?: Record<string, string[]>) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }
}

type SessionListener = (session: Session | null) => void

let accessToken: string | null = null
let refreshInFlight: Promise<Session | null> | null = null
const listeners = new Set<SessionListener>()

export function setSession(session: Session | null) {
  accessToken = session?.accessToken ?? null
  listeners.forEach((listener) => listener(session))
}

export function onSessionChange(listener: SessionListener) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

/** Renova a sessão usando o cookie de refresh. Retorna null se não houver sessão válida. */
export function refreshSession(): Promise<Session | null> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' })
      const session = response.ok ? ((await response.json()) as Session) : null
      setSession(session)
      return session
    } catch {
      setSession(null)
      return null
    } finally {
      refreshInFlight = null
    }
  })()
  return refreshInFlight
}

type RequestOptions = Omit<RequestInit, 'body'> & { body?: unknown; skipAuthRetry?: boolean }

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await sendWithAuth(path, options)
  if (!response.ok) throw await toApiError(response)
  if (response.status === 204 || response.status === 202) return undefined as T

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

/** Baixa um arquivo de uma rota protegida (o link simples não levaria o token). */
export async function apiDownload(path: string, fileName: string) {
  const response = await sendWithAuth(path, {})
  if (!response.ok) throw await toApiError(response)

  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

async function sendWithAuth(path: string, options: RequestOptions): Promise<Response> {
  const { body, skipAuthRetry, headers, ...init } = options
  const isForm = body instanceof FormData

  const send = () =>
    fetch(`/api${path}`, {
      ...init,
      credentials: 'same-origin',
      headers: {
        Accept: 'application/json',
        // FormData (envio de arquivo): o navegador define o Content-Type com o boundary.
        ...(body !== undefined && !isForm ? { 'Content-Type': 'application/json' } : {}),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...headers,
      },
      body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    })

  let response = await send()

  if (response.status === 401 && !skipAuthRetry && accessToken) {
    const renewed = await refreshSession()
    if (renewed) response = await send()
  }

  return response
}

async function toApiError(response: Response): Promise<ApiError> {
  const fallback = defaultMessage(response.status)
  try {
    const problem = (await response.json()) as {
      title?: string
      code?: string
      errors?: Record<string, string[]>
    }
    const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined
    return new ApiError(response.status, firstFieldError ?? problem.title ?? fallback, problem.code, problem.errors)
  } catch {
    return new ApiError(response.status, fallback)
  }
}

function defaultMessage(status: number): string {
  if (status === 401) return 'Sua sessão expirou. Entre novamente.'
  if (status === 403) return 'Você não tem permissão para esta ação.'
  if (status === 404) return 'Não encontrado.'
  if (status === 429) return 'Muitas tentativas. Aguarde um minuto e tente de novo.'
  if (status >= 500) return 'Erro no servidor. Tente novamente em instantes.'
  return 'Não foi possível concluir a operação.'
}
