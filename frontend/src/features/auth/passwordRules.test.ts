import { describe, expect, it } from 'vitest'
import { newPasswordSchema } from './passwordRules'

describe('regras de senha', () => {
  it.each([
    ['curta', false],
    ['semnumeroMaiuscula', false],
    ['semmaiuscula123', false],
    ['SEMMINUSCULA123', false],
    ['SenhaValida123', true],
  ])('"%s" válida = %s', (password, valid) => {
    expect(newPasswordSchema.safeParse({ password, confirmation: password }).success).toBe(valid)
  })

  it('exige que a confirmação seja igual', () => {
    const result = newPasswordSchema.safeParse({ password: 'SenhaValida123', confirmation: 'SenhaValida124' })
    expect(result.success).toBe(false)
  })
})
