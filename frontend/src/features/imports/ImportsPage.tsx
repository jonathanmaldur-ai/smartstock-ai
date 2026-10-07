import { History, Upload } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Alert, Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { FileUpload } from '@/components/ui/FileUpload'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { ApiError } from '@/lib/api'
import { formatDateTime, todayIsoDate } from '@/lib/format'
import { ImportReportView, reportScope, statusTone } from './ImportReportView'
import {
  importTypeInfo,
  importTypeLabel,
  importTypes,
  statusLabels,
  useConfirmImport,
  useImportHistory,
  useUploadImport,
  type ImportReport,
  type ImportType,
} from './importsApi'

export function ImportsPage() {
  const [results, setResults] = useState<ImportReport[]>([])
  const [selected, setSelected] = useState<ImportReport | null>(null)

  const replace = (updated: ImportReport) => setResults((list) => list.map((r) => (r.id === updated.id ? updated : r)))

  return (
    <>
      <PageHeader
        title="Importações"
        description="Envie as planilhas exportadas do ERP. Nada é gravado antes de você conferir o relatório e confirmar."
      />

      <div className="flex flex-col gap-6">
        <UploadCard onUploaded={setResults} />

        {results.length > 0 && <ResultsCard results={results} onDecided={replace} />}

        <HistoryCard onOpen={setSelected} />
      </div>

      <Modal open={selected !== null} onClose={() => setSelected(null)} title="Detalhes da importação" size="lg">
        {selected && <ImportReportView report={selected} onDecided={setSelected} />}
      </Modal>
    </>
  )
}

function UploadCard({ onUploaded }: { onUploaded: (reports: ImportReport[]) => void }) {
  const [type, setType] = useState<ImportType>('Brands')
  const [files, setFiles] = useState<File[]>([])
  const [referenceDate, setReferenceDate] = useState(todayIsoDate())
  const [progress, setProgress] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const upload = useUploadImport()
  const info = importTypeInfo(type)
  const busy = progress !== null

  const changeType = (value: ImportType) => {
    setType(value)
    if (!importTypeInfo(value).multiple) setFiles((current) => current.slice(0, 1))
  }

  /** Envia um arquivo por vez: cada um vira uma importação com relatório próprio. */
  const submit = async () => {
    setError(null)
    const reports: ImportReport[] = []
    try {
      for (const [index, file] of files.entries()) {
        setProgress(files.length > 1 ? `Validando ${index + 1} de ${files.length}…` : 'Validando…')
        reports.push(await upload.mutateAsync({ type, file, referenceDate: info.needsDate ? referenceDate : undefined }))
      }
      setFiles([])
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível enviar o arquivo. Verifique sua conexão.')
    } finally {
      setProgress(null)
      if (reports.length > 0) onUploaded(reports)
    }
  }

  return (
    <Card>
      <CardHeader
        title="Nova importação"
        description="Ordem recomendada: Marcas, Produtos e depois Estoque, Vendas e Transferências."
      />
      <div className="grid gap-4 p-4 tablet:p-6 notebook:grid-cols-[16rem_1fr] notebook:items-start">
        <div className="flex flex-col gap-4">
          <SelectField
            label="Tipo de arquivo"
            value={type}
            onChange={(e) => changeType(e.target.value as ImportType)}
            options={importTypes.map((t) => ({ value: t.value, label: `${t.label} (${t.file})` }))}
            disabled={busy}
          />
          {info.needsDate && (
            <TextField
              label="Data dos dados"
              type="date"
              value={referenceDate}
              max={todayIsoDate()}
              onChange={(e) => setReferenceDate(e.target.value)}
              hint="Dia em que o relatório foi tirado do ERP."
              disabled={busy}
            />
          )}
        </div>
        <FileUpload
          label={info.multiple ? 'Arquivos' : 'Arquivo'}
          accept=".xls,.xlsx,.csv"
          hint={
            info.multiple
              ? 'Pode escolher os arquivos de todas as lojas de uma vez. A loja é reconhecida pelo nome da aba (ex.: 04 JAGUARIUNA).'
              : 'Excel (.xls, .xlsx) ou CSV, até 120 MB. Pode enviar o arquivo como sai do ERP, sem tratar.'
          }
          files={files}
          onChange={setFiles}
          multiple={info.multiple}
          disabled={busy}
        />
        <div className="flex flex-col gap-3 notebook:col-span-2">
          {error && <Alert tone="error">{error}</Alert>}
          <div className="flex justify-end">
            <Button
              icon={<Upload aria-hidden className="size-4" />}
              disabled={files.length === 0 || (info.needsDate && !referenceDate)}
              loading={busy}
              onClick={() => void submit()}
            >
              {progress ?? 'Enviar e validar'}
            </Button>
          </div>
        </div>
      </div>
    </Card>
  )
}

function ResultsCard({ results, onDecided }: { results: ImportReport[]; onDecided: (report: ImportReport) => void }) {
  const toast = useToast()
  const confirm = useConfirmImport()
  const [confirmingAll, setConfirmingAll] = useState(false)
  const pending = results.filter((r) => r.status === 'Validated')

  const confirmAll = async () => {
    setConfirmingAll(true)
    let saved = 0
    try {
      for (const report of pending) {
        const updated = await confirm.mutateAsync(report.id)
        onDecided(updated)
        saved += updated.validRows
      }
      toast.show(`${saved.toLocaleString('pt-BR')} registros gravados.`)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível gravar todas.', 'error')
    } finally {
      setConfirmingAll(false)
    }
  }

  return (
    <Card>
      <CardHeader
        title={results.length > 1 ? `Resultado da validação (${results.length} arquivos)` : 'Resultado da validação'}
        actions={
          pending.length > 1 && (
            <Button loading={confirmingAll} onClick={() => void confirmAll()}>
              Gravar todas ({pending.length})
            </Button>
          )
        }
      />
      <div className="flex flex-col divide-y divide-border">
        {results.map((report) => (
          <div key={report.id} className="p-4 tablet:p-6">
            <ImportReportView report={report} onDecided={onDecided} />
          </div>
        ))}
      </div>
    </Card>
  )
}

function HistoryCard({ onOpen }: { onOpen: (report: ImportReport) => void }) {
  const [page, setPage] = useState(1)
  const { data, isPending, isError, refetch } = useImportHistory(page)

  return (
    <Card>
      <CardHeader title="Histórico" description="Todas as importações ficam registradas, com o arquivo original." />
      {isPending ? (
        <div className="flex flex-col gap-3 p-6">
          {Array.from({ length: 3 }, (_, i) => (
            <Skeleton key={i} className="h-10" />
          ))}
        </div>
      ) : isError ? (
        <div className="p-6">
          <Alert tone="error" title="Não foi possível carregar o histórico">
            <button type="button" className="underline" onClick={() => void refetch()}>
              Tentar novamente
            </button>
          </Alert>
        </div>
      ) : data.items.length === 0 ? (
        <EmptyState icon={History} title="Nenhuma importação ainda" description="Comece enviando o arquivo de marcas." />
      ) : (
        <>
          <Table>
            <caption className="sr-only">Histórico de importações</caption>
            <thead>
              <tr>
                <Th>Enviado em</Th>
                <Th>Tipo</Th>
                <Th>Arquivo</Th>
                <Th>Situação</Th>
                <Th className="text-right">Gravadas / linhas</Th>
                <Th className="text-right">Detalhes</Th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((item) => {
                const scope = reportScope(item)
                return (
                  <tr key={item.id}>
                    <Td className="whitespace-nowrap">{formatDateTime(item.uploadedAt)}</Td>
                    <Td>{importTypeLabel(item.type)}</Td>
                    <Td className="max-w-64">
                      <span className="flex flex-col">
                        <span className="truncate">{item.fileName}</span>
                        {scope && <span className="truncate text-caption text-text-subtle">{scope}</span>}
                      </span>
                    </Td>
                    <Td>
                      <Badge tone={statusTone[item.status]}>{statusLabels[item.status]}</Badge>
                    </Td>
                    <Td className="text-right tabular-nums">
                      {item.validRows.toLocaleString('pt-BR')} / {item.totalRows.toLocaleString('pt-BR')}
                    </Td>
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => onOpen(item)}>
                        Ver
                      </Button>
                    </Td>
                  </tr>
                )
              })}
            </tbody>
          </Table>
          <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
        </>
      )}
    </Card>
  )
}
