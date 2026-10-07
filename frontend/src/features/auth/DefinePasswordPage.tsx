import { zodResolver } from '@hookform/resolvers/zod'
import { Check, Circle } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useSearchParams } from 'react-router'
import { Button } from '@/components/ui/Button'
import { Alert } from '@/components/ui/Feedback'
import { PasswordField } from '@/components/ui/Field'
import { ApiError, apiRequest } from '@/lib/api'
import { cn } from '@/lib/cn'
import { AuthLayout } from './AuthLayout'
import { newPasswordSchema, passwordRules, type NewPasswordForm } from './passwordRules'

type LinkType = 'convite' | 'recuperacao'

/** Tela aberta pelo link do e-mail: primeiro acesso (convite) ou recuperação de senha. */
export function DefinePasswordPage() {
  const [params] = useSearchParams()
  const type = params.get('tipo') as LinkType | null
  const userId = params.get('uid')
  const token = params.get('token')
  const validLink = (type === 'convite' || type === 'recuperacao') && !!userId && !!token

  const [done, setDone] = useState(false)
  const [error, setError] = useState<{ message: string; invalidLink: boolean } | null>(null)
  const {
    register,
    handleSubmit,
    watch,
    formState: { errors, isSubmitting },
  } = useForm<NewPasswordForm>({ resolver: zodResolver(newPasswordSchema), defaultValues: { password: '', confirmation: '' } })
  const password = watch('password')

  const isInvite = type === 'convite'
  const title = isInvite ? 'Crie sua senha' : 'Redefinir senha'

  const onSubmit = handleSubmit(async (form) => {
    setError(null)
    try {
      await apiRequest('/auth/define-password', {
        method: 'POST',
        body: { userId, token, newPassword: form.password, type },
        skipAuthRetry: true,
      })
      setDone(true)
    } catch (e) {
      setError(
        e instanceof ApiError
          ? { message: e.message, invalidLink: e.code === 'auth.invalid_link' }
          : { message: 'Não foi possível salvar. Verifique sua conexão.', invalidLink: false },
      )
    }
  })

  if (!validLink) {
    return (
      <AuthLayout title="Link inválido">
        <div className="flex flex-col gap-4">
          <Alert tone="error">Este link está incompleto ou foi copiado errado. Solicite um novo.</Alert>
          <Link to="/esqueci-senha" className="text-center text-body text-primary hover:underline">
            Solicitar novo link
          </Link>
        </div>
      </AuthLayout>
    )
  }

  if (done) {
    return (
      <AuthLayout title={isInvite ? 'Acesso ativado' : 'Senha alterada'}>
        <div className="flex flex-col gap-4">
          <Alert tone="success">
            {isInvite ? 'Sua senha foi criada.' : 'Sua senha foi alterada e as sessões abertas foram encerradas.'} Agora é
            só entrar.
          </Alert>
          <Link
            to="/login"
            className="inline-flex h-10 items-center justify-center rounded-md bg-primary px-4 font-medium text-primary-fg hover:bg-primary-hover"
          >
            Ir para o login
          </Link>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout
      title={title}
      description={isInvite ? 'Bem-vindo(a) ao SmartStock AI. Crie a senha do seu acesso.' : 'Crie uma nova senha para o seu acesso.'}
    >
      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        {error && (
          <Alert tone="error">
            {error.message}{' '}
            {error.invalidLink && (
              <Link to="/esqueci-senha" className="underline">
                Solicitar novo link
              </Link>
            )}
          </Alert>
        )}
        <PasswordField label="Nova senha" autoComplete="new-password" error={errors.password?.message} {...register('password')} />
        <ul aria-label="Requisitos da senha" className="flex flex-col gap-1">
          {passwordRules.map((rule) => {
            const ok = rule.test(password)
            return (
              <li key={rule.id} className={cn('flex items-center gap-2 text-caption', ok ? 'text-success' : 'text-text-subtle')}>
                {ok ? <Check aria-hidden className="size-3.5" /> : <Circle aria-hidden className="size-3.5" />}
                {rule.label}
                <span className="sr-only">{ok ? '(atendido)' : '(pendente)'}</span>
              </li>
            )
          })}
        </ul>
        <PasswordField
          label="Repita a nova senha"
          autoComplete="new-password"
          error={errors.confirmation?.message}
          {...register('confirmation')}
        />
        <Button type="submit" loading={isSubmitting} className="mt-2 w-full">
          Salvar senha
        </Button>
      </form>
    </AuthLayout>
  )
}
