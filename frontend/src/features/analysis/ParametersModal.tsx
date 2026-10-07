import { useEffect, useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Alert, Skeleton } from '@/components/ui/Feedback'
import { TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Switch } from '@/components/ui/Switch'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/api'
import { formatDateTime } from '@/lib/format'
import { useParameters, useUpdateParameters, type ParametersInput } from './analysisApi'

const fields: readonly { key: Exclude<keyof ParametersInput, 'sendAlertEmail'>; label: string; hint: string }[] = [
  { key: 'criticalCoverageDays', label: 'Cobertura crítica (dias)', hint: 'Abaixo disso: prioridade alta' },
  { key: 'minimumDays', label: 'Estoque mínimo (dias)', hint: 'Abaixo disso a loja recebe sugestão' },
  { key: 'idealDays', label: 'Estoque ideal (dias)', hint: 'A sugestão leva o destino até aqui; a origem nunca fica abaixo' },
  { key: 'maximumDays', label: 'Estoque máximo (dias)', hint: 'Referência de estoque alto' },
  { key: 'excessDays', label: 'Excesso (dias)', hint: 'Acima disso: excesso' },
  { key: 'minimumAnnualSales', label: 'Venda mínima no destino (un. em 12 meses)', hint: 'Abaixo disso não há sugestão (decisão 35)' },
]

/** Parâmetros da seção 2 das decisões. Todos veem; só o Administrador altera. */
export function ParametersModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { user } = useAuth()
  const toast = useToast()
  const { data, isPending } = useParameters()
  const update = useUpdateParameters()
  const [values, setValues] = useState<ParametersInput | null>(null)
  const [error, setError] = useState<string | null>(null)
  const canEdit = hasRole(user?.role, ['Administrador'])

  useEffect(() => {
    if (open && data) {
      const { updatedAt: _updatedAt, updatedByEmail: _updatedBy, ...input } = data
      setValues(input)
      setError(null)
    }
  }, [open, data])

  const save = async () => {
    if (!values) return
    setError(null)
    try {
      await update.mutateAsync(values)
      toast.show('Parâmetros salvos. Gere uma nova análise para aplicá-los.')
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar.')
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Parâmetros de estoque"
      description="Faixas usadas para classificar o estoque e calcular as sugestões (seção 2 das decisões do projeto)."
      footer={
        canEdit ? (
          <>
            <Button variant="secondary" onClick={onClose}>
              Cancelar
            </Button>
            <Button loading={update.isPending} onClick={() => void save()}>
              Salvar
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            Fechar
          </Button>
        )
      }
    >
      {isPending || !values ? (
        <Skeleton className="h-64" />
      ) : (
        <div className="flex flex-col gap-4">
          <div className="grid gap-4 tablet:grid-cols-2">
            {fields.map((field) => (
              <TextField
                key={field.key}
                label={field.label}
                hint={field.hint}
                type="number"
                min={0}
                value={values[field.key]}
                disabled={!canEdit}
                onChange={(e) => setValues({ ...values, [field.key]: Number(e.target.value) })}
              />
            ))}
          </div>
          <div className="flex items-start gap-3 rounded-md border border-border p-3">
            <Switch
              checked={values.sendAlertEmail}
              disabled={!canEdit}
              label="Enviar resumo dos alertas por e-mail"
              onChange={(sendAlertEmail) => setValues({ ...values, sendAlertEmail })}
            />
            <span className="flex flex-col">
              <span className="text-body text-text">Enviar resumo dos alertas por e-mail</span>
              <span className="text-caption text-text-muted">Após cada análise, para os Administradores e Gerentes ativos. Só os alertas ainda não vistos.</span>
            </span>
          </div>
          {error && <Alert tone="error">{error}</Alert>}
          {data && (
            <p className="text-caption text-text-subtle">
              Última alteração em {formatDateTime(data.updatedAt)} por {data.updatedByEmail ?? '—'}.
            </p>
          )}
        </div>
      )}
    </Modal>
  )
}
