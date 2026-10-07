import { RefreshCw, SlidersHorizontal } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Alert, Skeleton } from '@/components/ui/Feedback'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/api'
import { formatDate, formatDateTime } from '@/lib/format'
import { analyzeRoles, useAnalysisSummary, useRunAnalysis } from './analysisApi'
import { ParametersModal } from './ParametersModal'

/** Qual análise está na tela, se ela está desatualizada, e as ações de gerar outra e ver os parâmetros. */
export function AnalysisBanner() {
  const { user } = useAuth()
  const toast = useToast()
  const { data, isPending, isError, refetch } = useAnalysisSummary()
  const run = useRunAnalysis()
  const [showParameters, setShowParameters] = useState(false)
  const canRun = hasRole(user?.role, analyzeRoles)

  const generate = async () => {
    try {
      const summary = await run.mutateAsync(undefined)
      toast.show(`Análise gerada: ${summary.suggestions.pending.toLocaleString('pt-BR')} sugestões de transferência.`)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível gerar a análise.', 'error')
    }
  }

  if (isPending) return <Skeleton className="mb-6 h-20" />
  if (isError)
    return (
      <div className="mb-6">
        <Alert tone="error" title="Não foi possível carregar a análise">
          <button type="button" className="underline" onClick={() => void refetch()}>
            Tentar novamente
          </button>
        </Alert>
      </div>
    )

  return (
    <div className="mb-6 flex flex-col gap-3">
      <Card className="flex flex-col gap-3 p-4 tablet:flex-row tablet:items-center tablet:justify-between tablet:p-6">
        <div className="flex flex-col gap-1">
          {data.analysisDate && data.stockDate ? (
            <>
              <p className="text-body font-medium text-text">
                Análise de {formatDate(data.analysisDate)} com o estoque de {formatDate(data.stockDate)}
              </p>
              <p className="text-caption text-text-muted">
                Gerada em {formatDateTime(data.createdAt!)} por {data.createdByEmail}. O estoque é projetado até a data da análise
                (estoque − venda média × dias desde a importação).
              </p>
            </>
          ) : (
            <p className="text-body text-text">Nenhuma análise gerada ainda.</p>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" icon={<SlidersHorizontal aria-hidden className="size-4" />} onClick={() => setShowParameters(true)}>
            Parâmetros
          </Button>
          {canRun && (
            <Button icon={<RefreshCw aria-hidden className="size-4" />} loading={run.isPending} onClick={() => void generate()}>
              {run.isPending ? 'Calculando…' : 'Gerar nova análise'}
            </Button>
          )}
        </div>
      </Card>

      {data.isOutdated && (
        <Alert tone="warning" title={data.analysisId ? 'Análise desatualizada' : 'Dados prontos para análise'}>
          {data.analysisId
            ? 'Há estoque ou vendas importados, ou parâmetros alterados, depois desta análise. Gere uma nova análise para atualizar as sugestões.'
            : 'O estoque já foi importado. Gere a primeira análise para ver a situação e as sugestões.'}
        </Alert>
      )}

      <ParametersModal open={showParameters} onClose={() => setShowParameters(false)} />
    </div>
  )
}
