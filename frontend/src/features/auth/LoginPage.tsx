import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Button } from '@/components/ui/Button'
import { Alert } from '@/components/ui/Feedback'
import { PasswordField, TextField } from '@/components/ui/Field'
import { ApiError } from '@/lib/api'
import { AuthLayout } from './AuthLayout'
import { useAuth } from './useAuth'

const schema = z.object({
  email: z.string().trim().min(1, 'Informe o e-mail.').email('E-mail inválido.'),
  password: z.string().min(1, 'Informe a senha.'),
})

type LoginForm = z.infer<typeof schema>

export function LoginPage() {
  const { login } = useAuth()
  const [error, setError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginForm>({ resolver: zodResolver(schema) })

  const onSubmit = handleSubmit(async ({ email, password }) => {
    setError(null)
    try {
      await login(email, password)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível entrar. Verifique sua conexão.')
    }
  })

  return (
    <AuthLayout title="Entrar" description="Use seu e-mail corporativo.">
      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-4">
        {error && <Alert tone="error">{error}</Alert>}
        <TextField
          label="E-mail"
          type="email"
          autoComplete="username"
          placeholder="nome@example.com"
          error={errors.email?.message}
          {...register('email')}
        />
        <PasswordField
          label="Senha"
          autoComplete="current-password"
          error={errors.password?.message}
          {...register('password')}
        />
        <Button type="submit" loading={isSubmitting} className="mt-2 w-full">
          Entrar
        </Button>
        <Link to="/esqueci-senha" className="text-center text-body text-primary hover:underline">
          Esqueci minha senha
        </Link>
      </form>
    </AuthLayout>
  )
}
