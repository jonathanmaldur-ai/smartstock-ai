import { Mail, Pencil, Power, UserPlus, Users } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button, IconButton } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Alert, Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { Modal } from '@/components/ui/Modal'
import { Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { UserFormModal } from './UserFormModal'
import { useResendInvite, useSetUserActive, useUsers, type User } from './usersApi'

export function UsersPage() {
  const { data: users, isPending, isError, refetch } = useUsers()
  const [formUser, setFormUser] = useState<User | null | undefined>(undefined)
  const [confirmUser, setConfirmUser] = useState<User | null>(null)

  return (
    <>
      <PageHeader
        title="Usuários"
        description="Quem acessa a plataforma e com qual perfil. Usuários não são excluídos: são desativados."
        actions={
          <Button icon={<UserPlus aria-hidden className="size-4" />} onClick={() => setFormUser(null)}>
            Novo usuário
          </Button>
        }
      />

      <Card>
        {isPending ? (
          <div className="flex flex-col gap-3 p-6">
            {Array.from({ length: 4 }, (_, i) => (
              <Skeleton key={i} className="h-10" />
            ))}
          </div>
        ) : isError ? (
          <div className="p-6">
            <Alert tone="error" title="Não foi possível carregar os usuários">
              <button type="button" className="underline" onClick={() => void refetch()}>
                Tentar novamente
              </button>
            </Alert>
          </div>
        ) : users.length === 0 ? (
          <EmptyState icon={Users} title="Nenhum usuário" />
        ) : (
          <UsersTable users={users} onEdit={setFormUser} onToggleActive={setConfirmUser} />
        )}
      </Card>

      <UserFormModal open={formUser !== undefined} user={formUser ?? null} onClose={() => setFormUser(undefined)} />
      <ToggleActiveModal user={confirmUser} onClose={() => setConfirmUser(null)} />
    </>
  )
}

function UsersTable({
  users,
  onEdit,
  onToggleActive,
}: {
  users: User[]
  onEdit: (user: User) => void
  onToggleActive: (user: User) => void
}) {
  const { user: me } = useAuth()
  const toast = useToast()
  const resendInvite = useResendInvite()

  const resend = async (user: User) => {
    try {
      await resendInvite.mutateAsync(user.id)
      toast.show(`Convite reenviado para ${user.email}.`)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Falha ao reenviar o convite.', 'error')
    }
  }

  return (
    <Table>
      <caption className="sr-only">Lista de usuários</caption>
      <thead>
        <tr>
          <Th>Nome</Th>
          <Th>Perfil</Th>
          <Th>Situação</Th>
          <Th>Último acesso</Th>
          <Th className="text-right">Ações</Th>
        </tr>
      </thead>
      <tbody>
        {users.map((user) => (
          <tr key={user.id} className={user.isActive ? undefined : 'opacity-60'}>
            <Td>
              <div className="flex flex-col">
                <span className="font-medium">{user.fullName}</span>
                <span className="text-caption text-text-subtle">{user.email}</span>
              </div>
            </Td>
            <Td>{user.role}</Td>
            <Td>
              {!user.isActive ? (
                <Badge>Desativado</Badge>
              ) : user.hasPassword ? (
                <Badge tone="success">Ativo</Badge>
              ) : (
                <Badge tone="warning">Convite pendente</Badge>
              )}
            </Td>
            <Td className="whitespace-nowrap text-text-muted">
              {user.lastLoginAt ? formatDateTime(user.lastLoginAt) : 'Nunca'}
            </Td>
            <Td>
              <div className="flex justify-end gap-1">
                {user.isActive && !user.hasPassword && (
                  <IconButton label={`Reenviar convite para ${user.fullName}`} onClick={() => void resend(user)}>
                    <Mail aria-hidden className="size-4" />
                  </IconButton>
                )}
                <IconButton label={`Editar ${user.fullName}`} onClick={() => onEdit(user)}>
                  <Pencil aria-hidden className="size-4" />
                </IconButton>
                {user.id !== me?.id && (
                  <IconButton
                    label={user.isActive ? `Desativar ${user.fullName}` : `Reativar ${user.fullName}`}
                    onClick={() => onToggleActive(user)}
                  >
                    <Power aria-hidden className="size-4" />
                  </IconButton>
                )}
              </div>
            </Td>
          </tr>
        ))}
      </tbody>
    </Table>
  )
}

function ToggleActiveModal({ user, onClose }: { user: User | null; onClose: () => void }) {
  const toast = useToast()
  const setActive = useSetUserActive()
  const [error, setError] = useState<string | null>(null)
  const deactivating = user?.isActive ?? false

  const confirm = async () => {
    if (!user) return
    setError(null)
    try {
      await setActive.mutateAsync({ id: user.id, active: !user.isActive })
      toast.show(deactivating ? `${user.fullName} foi desativado(a).` : `${user.fullName} foi reativado(a).`)
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível concluir.')
    }
  }

  return (
    <Modal
      open={user !== null}
      onClose={() => {
        setError(null)
        onClose()
      }}
      title={deactivating ? 'Desativar usuário' : 'Reativar usuário'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button variant={deactivating ? 'danger' : 'primary'} loading={setActive.isPending} onClick={() => void confirm()}>
            {deactivating ? 'Desativar' : 'Reativar'}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error && <Alert tone="error">{error}</Alert>}
        <p className="text-body text-text">
          {deactivating
            ? `${user?.fullName} perde o acesso imediatamente e as sessões abertas são encerradas. O histórico dele é mantido.`
            : `${user?.fullName} volta a acessar a plataforma com a senha que já tinha.`}
        </p>
      </div>
    </Modal>
  )
}
