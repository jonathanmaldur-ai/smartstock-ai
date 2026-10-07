import { zodResolver } from '@hookform/resolvers/zod'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Button } from '@/components/ui/Button'
import { Alert } from '@/components/ui/Feedback'
import { TextField } from '@/components/ui/Field'
import { ApiError, apiRequest } from '@/lib/api'
import { AuthLayout } from './AuthLayout'

const schema = z.object({
  email: z.string().trim().min(1, 'Informe o e-mail.').email('E-mail inválido.'),
})

type ForgotForm = z.infer<typeof schema>

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<ForgotForm>({ resolver: zodResolver(schema) })

  const onSubmit = handleSubmit(async ({ email }) => {
    setError(null)
    try {
      await apiRequest('/auth/forgot-password', { method: 'POST', body: { email }, skipAuthRetry: true })
      setSent(true)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar. Verifique sua conexão.')
    }
  })

  return (
    <AuthLayout title="Esqueci minha senha" description="Enviaremos um link para você criar uma nova senha.">
      {sent ? (
        <div className="flex flex-col gap-4">
          <Alert tone="success" title="Pedido recebido">
            Se o e-mail estiver cadastrado, você vai receber o link em instantes. Confira também a caixa de spam.
          </Alert>
          <Link to="/login" className="text-center text-body text-primary hover:underline">
            Voltar para o login
          </Link>
        </div>
      ) : (
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
          <Button type="submit" loading={isSubmitting} className="mt-2 w-full">
            Enviar link
          </Button>
          <Link to="/login" className="text-center text-body text-primary hover:underline">
            Voltar para o login
          </Link>
        </form>
      )}
    </AuthLayout>
  )
}
