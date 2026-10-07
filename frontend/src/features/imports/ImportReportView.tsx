import { Download } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Alert, Badge, type Tone } from '@/components/ui/Feedback'
import { Stat } from '@/components/ui/Stat'
import { Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { ApiError, apiDownload } from '@/lib/api'
import { formatDate, formatDateTime } from '@/lib/format'
import { importTypeLabel, statusLabels, useConfirmImport, useDiscardImport, type ImportReport, type ImportStatus } from './importsApi'

export const statusTone: Record<ImportStatus, Tone> = {
  Validated: 'warning',
  Confirmed: 'success',
  Discarded: 'neutral',
  Rejected: 'error',
}

/** A que os dados se referem: loja, data e período (estoque, vendas e transferências). */
export function reportScope(report: ImportReport) {
  const parts: string[] = []
  if (report.storeCode) parts.push(`Loja ${report.storeCode} ${report.storeName ?? ''}`.trim())
  if (report.referenceDate) parts.push(`dados de ${formatDate(report.referenceDate)}`)
  if (report.periodStart && report.periodEnd) parts.push(`período ${formatDate(report.periodStart)} a ${formatDate(report.periodEnd)}`)
  return parts.length > 0 ? parts.join(' · ') : null
}

/** Relatório de validação de um lote, com as ações de confirmar ou descartar. */
export function ImportReportView({ report, onDecided }: { report: ImportReport; onDecided: (report: ImportReport) => void }) {
  const toast = useToast()
  const confirm = useConfirmImport()
  const discard = useDiscardImport()
  const [downloading, setDownloading] = useState(false)
  const pending = report.status === 'Validated'
  const scope = reportScope(report)

  const decide = async (action: 'confirm' | 'discard') => {
    try {
      const updated = action === 'confirm' ? await confirm.mutateAsync(report.id) : await discard.mutateAsync(report.id)
      toast.show(action === 'confirm' ? `${updated.validRows.toLocaleString('pt-BR')} registros gravados.` : 'Importação descartada.')
      onDecided(updated)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível concluir.', 'error')
    }
  }

  const download = async () => {
    setDownloading(true)
    try {
      await apiDownload(`/imports/${report.id}/issues.csv`, `ocorrencias-${report.fileName}.csv`)
    } catch {
      toast.show('Não foi possível baixar as ocorrências.', 'error')
    } finally {
      setDownloading(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center gap-3">
        <Badge tone={statusTone[report.status]}>{statusLabels[report.status]}</Badge>
        <span className="text-body text-text-muted">
          {importTypeLabel(report.type)} · {report.fileName} · enviado em {formatDateTime(report.uploadedAt)} por {report.uploadedByEmail}
        </span>
      </div>

      {report.rejectionReason && (
        <Alert tone="error" title="Arquivo recusado">
          {report.rejectionReason}
        </Alert>
      )}

      {scope && <p className="text-body font-medium text-text">{scope}</p>}

      {pending && report.type === 'Transfers' && report.periodStart && report.periodEnd && (
        <Alert tone="info" title="Substitui o período">
          Se já houver transferências importadas entre {formatDate(report.periodStart)} e {formatDate(report.periodEnd)}, elas serão
          substituídas pelas deste arquivo.
        </Alert>
      )}

      {report.replacedRecords > 0 && (
        <Alert tone="info">
          {report.replacedRecords.toLocaleString('pt-BR')} registros de importações anteriores do mesmo período foram substituídos.
        </Alert>
      )}

      {report.duplicateOfBatchId && pending && (
        <Alert tone="warning" title="Arquivo já importado">
          Este mesmo arquivo já foi gravado antes. Confirmar de novo só reescreve os mesmos dados.
        </Alert>
      )}

      {!report.rejectionReason && (
        <div className="grid grid-cols-2 gap-3 notebook:grid-cols-4">
          <Stat label="Linhas no arquivo" value={report.totalRows} />
          <Stat label={pending ? 'Serão gravadas' : 'Gravadas'} value={report.validRows} tone="success" />
          <Stat label="Com erro (não gravadas)" value={report.errorRows} tone={report.errorRows ? 'error' : 'neutral'} />
          <Stat label="Avisos" value={report.warningCount} tone={report.warningCount ? 'warning' : 'neutral'} />
        </div>
      )}

      {report.issueGroups.length > 0 && (
        <div className="flex flex-col gap-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-subtitle">Ocorrências</h3>
            <Button variant="secondary" size="sm" loading={downloading} icon={<Download aria-hidden className="size-4" />} onClick={() => void download()}>
              Baixar lista completa (CSV)
            </Button>
          </div>
          <Table>
            <caption className="sr-only">Ocorrências agrupadas por tipo</caption>
            <thead>
              <tr>
                <Th>Tipo</Th>
                <Th>Ocorrência</Th>
                <Th className="text-right">Linhas</Th>
                <Th>Exemplos</Th>
              </tr>
            </thead>
            <tbody>
              {report.issueGroups.map((group) => (
                <tr key={`${group.severity}-${group.code}`}>
                  <Td>
                    <Badge tone={group.severity === 'Error' ? 'error' : 'warning'}>{group.severity === 'Error' ? 'Erro' : 'Aviso'}</Badge>
                  </Td>
                  <Td className="min-w-64">{group.message}</Td>
                  <Td className="text-right tabular-nums">{group.count.toLocaleString('pt-BR')}</Td>
                  <Td className="text-caption text-text-muted">{group.sampleValues.join(' · ') || '—'}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      )}

      {pending && (
        <div className="flex flex-col gap-3 rounded-md border border-border bg-surface-muted p-4 tablet:flex-row tablet:items-center tablet:justify-between">
          <p className="text-body text-text">
            {report.errorRows > 0
              ? `As ${report.errorRows.toLocaleString('pt-BR')} linhas com erro serão ignoradas. Deseja gravar as ${report.validRows.toLocaleString('pt-BR')} linhas válidas?`
              : `Tudo certo. Deseja gravar as ${report.validRows.toLocaleString('pt-BR')} linhas?`}
          </p>
          <div className="flex gap-2">
            <Button variant="secondary" loading={discard.isPending} disabled={confirm.isPending} onClick={() => void decide('discard')}>
              Descartar
            </Button>
            <Button loading={confirm.isPending} disabled={discard.isPending} onClick={() => void decide('confirm')}>
              Gravar dados
            </Button>
          </div>
        </div>
      )}

      {report.decidedAt && (
        <p className="text-caption text-text-subtle">
          {statusLabels[report.status]} em {formatDateTime(report.decidedAt)} por {report.decidedByEmail}.
        </p>
      )}
    </div>
  )
}
