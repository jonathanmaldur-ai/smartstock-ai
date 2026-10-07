import { z } from 'zod'

/** Mesmas regras configuradas no backend (ASP.NET Identity). */
export const passwordRules = [
  { id: 'length', label: 'Pelo menos 10 caracteres', test: (value: string) => value.length >= 10 },
  { id: 'upper', label: 'Uma letra maiúscula', test: (value: string) => /[A-Z]/.test(value) },
  { id: 'lower', label: 'Uma letra minúscula', test: (value: string) => /[a-z]/.test(value) },
  { id: 'digit', label: 'Um número', test: (value: string) => /\d/.test(value) },
] as const

export const newPasswordSchema = z
  .object({
    password: z.string().refine((value) => passwordRules.every((rule) => rule.test(value)), {
      message: 'A senha não atende a todos os requisitos.',
    }),
    confirmation: z.string().min(1, 'Repita a senha.'),
  })
  .refine((data) => data.password === data.confirmation, {
    message: 'As senhas não são iguais.',
    path: ['confirmation'],
  })

export type NewPasswordForm = z.infer<typeof newPasswordSchema>
