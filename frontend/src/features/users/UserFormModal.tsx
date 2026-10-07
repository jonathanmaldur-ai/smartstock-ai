import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '@/components/ui/Button'
import { Alert } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { useToast } from '@/components/ui/Toast'
import { ApiError, type Role } from '@/lib/api'
import { roleOptions, useCreateUser, useUpdateUser, type User } from './usersApi'

const CORPORATE_DOMAIN = '@example.com'

const schema = z.object({
  fullName: z.string().trim().min(1, 'Informe o nome.').max(150, 'Use até 150 caracteres.'),
  email: z.string().trim(),
  role: z.enum(['Administrador', 'Gerente', 'Operador', 'Consulta']),
})

type FormValues = z.infer<typeof schema>

type Props = { open: boolean; user: User | null; onClose: () => void }

/** Cria (user = null) ou edita um usuário. O e-mail não muda depois de criado. */
export function UserFormModal({ open, user, onClose }: Props) {
  const isEdit = user !== null
  const toast = useToast()
  const createUser = useCreateUser()
  const updateUser = useUpdateUser()
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError: setFieldError,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (!open) return
    setError(null)
    reset(user ? { fullName: user.fullName, email: user.email, role: user.role } : { fullName: '', email: '', role: 'Consulta' })
  }, [open, user, reset])

  const selectedRole = watch('role') as Role | undefined

  const onSubmit = handleSubmit(async (values) => {
    setError(null)
    if (!isEdit && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(values.email)) {
      setFieldError('email', { message: 'Informe um e-mail válido.' })
      return
    }
    try {
      if (isEdit) {
        await updateUser.mutateAsync({ id: user.id, input: { fullName: values.fullName, role: values.role } })
        toast.show('Usuário atualizado.')
      } else {
        const result = await createUser.mutateAsync(values)
        toast.show(
          result.inviteSent
            ? `Usuário criado. Convite enviado para ${values.email}.`
            : 'Usuário criado, mas o e-mail de convite falhou. Use "Reenviar convite".',
          result.inviteSent ? 'success' : 'warning',
        )
      }
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar. Verifique sua conexão.')
    }
  })

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={isEdit ? 'Editar usuário' : 'Novo usuário'}
      description={isEdit ? undefined : 'A pessoa recebe um e-mail para criar a própria senha.'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button type="submit" form="user-form" loading={isSubmitting}>
            {isEdit ? 'Salvar' : 'Criar e enviar convite'}
          </Button>
        </>
      }
    >
      <form id="user-form" onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        {error && <Alert tone="error">{error}</Alert>}
        <TextField label="Nome completo" autoComplete="off" error={errors.fullName?.message} {...register('fullName')} />
        <TextField
          label="E-mail corporativo"
          type="email"
          autoComplete="off"
          disabled={isEdit}
          placeholder={`nome${CORPORATE_DOMAIN}`}
          hint={isEdit ? 'O e-mail não pode ser alterado.' : `Somente e-mails ${CORPORATE_DOMAIN}.`}
          error={errors.email?.message}
          {...register('email')}
        />
        <SelectField
          label="Perfil de acesso"
          options={roleOptions}
          hint={roleOptions.find((r) => r.value === selectedRole)?.description}
          error={errors.role?.message}
          {...register('role')}
        />
        {isEdit && selectedRole !== user.role && (
          <Alert tone="warning">A troca de perfil encerra as sessões abertas desse usuário.</Alert>
        )}
      </form>
    </Modal>
  )
}
