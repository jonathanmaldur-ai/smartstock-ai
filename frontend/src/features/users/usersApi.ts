import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { apiRequest, type Role } from '@/lib/api'

export type User = {
  id: string
  fullName: string
  email: string
  role: Role
  isActive: boolean
  hasPassword: boolean
  createdAt: string
  lastLoginAt: string | null
}

export type UserInput = { fullName: string; email?: string; role: Role }

export const roleOptions: readonly { value: Role; label: string; description: string }[] = [
  { value: 'Administrador', label: 'Administrador', description: 'Controle total, inclusive usuários e configurações.' },
  { value: 'Gerente', label: 'Gerente', description: 'Visualização completa e aprovação de ações.' },
  { value: 'Operador', label: 'Operador', description: 'Consulta e importação de arquivos.' },
  { value: 'Consulta', label: 'Consulta', description: 'Apenas visualização.' },
]

const usersKey = ['users'] as const

export function useUsers() {
  return useQuery({ queryKey: usersKey, queryFn: () => apiRequest<User[]>('/users') })
}

function useUsersMutation<TVariables, TResult>(mutationFn: (variables: TVariables) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: usersKey }),
  })
}

export const useCreateUser = () =>
  useUsersMutation((input: UserInput) =>
    apiRequest<{ user: User; inviteSent: boolean }>('/users', { method: 'POST', body: input }),
  )

export const useUpdateUser = () =>
  useUsersMutation(({ id, input }: { id: string; input: UserInput }) =>
    apiRequest<User>(`/users/${id}`, { method: 'PUT', body: input }),
  )

export const useSetUserActive = () =>
  useUsersMutation(({ id, active }: { id: string; active: boolean }) =>
    apiRequest<User>(`/users/${id}/${active ? 'activate' : 'deactivate'}`, { method: 'POST' }),
  )

export const useResendInvite = () =>
  useUsersMutation((id: string) => apiRequest<void>(`/users/${id}/resend-invite`, { method: 'POST' }))
