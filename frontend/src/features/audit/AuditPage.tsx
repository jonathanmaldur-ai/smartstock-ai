import { ScrollText } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Alert, Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { formatDateTime } from '@/lib/format'
import { actionLabel, useAuditActions, useAuditLogs, type AuditFilters, type AuditLog, type AuditResult } from './auditApi'

const emptyFilters: AuditFilters = { page: 1 }

export function AuditPage() {
  const [draft, setDraft] = useState<AuditFilters>(emptyFilters)
  const [filters, setFilters] = useState<AuditFilters>(emptyFilters)
  const [selected, setSelected] = useState<AuditLog | null>(null)
  const { data, isPending, isError, isFetching, refetch } = useAuditLogs(filters)
  const actions = useAuditActions()

  const apply = (event: React.FormEvent) => {
    event.preventDefault()
    setFilters({ ...draft, page: 1 })
  }

  const clear = () => {
    setDraft(emptyFilters)
    setFilters(emptyFilters)
  }

  return (
    <>
      <PageHeader title="Auditoria" description="Registro de todas as ações críticas: quem fez, o quê, quando e o resultado." />

      <Card className="mb-6 p-4 tablet:p-6">
        <form onSubmit={apply} className="grid grid-cols-1 gap-4 tablet:grid-cols-2 notebook:grid-cols-6">
          <div className="notebook:col-span-2">
            <SelectField
              label="Ação"
              value={draft.action ?? ''}
              onChange={(e) => setDraft({ ...draft, action: e.target.value || undefined })}
              options={[
                { value: '', label: 'Todas' },
                ...(actions.data ?? []).map((a) => ({ value: a, label: actionLabel(a) })),
              ]}
            />
          </div>
          <div className="notebook:col-span-2">
            <TextField
              label="Usuário (e-mail)"
              value={draft.userEmail ?? ''}
              onChange={(e) => setDraft({ ...draft, userEmail: e.target.value || undefined })}
              placeholder="Parte do e-mail"
            />
          </div>
          <div className="notebook:col-span-2">
            <SelectField
              label="Resultado"
              value={draft.result ?? ''}
              onChange={(e) => setDraft({ ...draft, result: (e.target.value || undefined) as AuditResult | undefined })}
              options={[
                { value: '', label: 'Todos' },
                { value: 'Success', label: 'Sucesso' },
                { value: 'Failure', label: 'Falha' },
              ]}
            />
          </div>
          <div className="notebook:col-span-2">
            <TextField label="De" type="date" value={draft.from ?? ''} onChange={(e) => setDraft({ ...draft, from: e.target.value || undefined })} />
          </div>
          <div className="notebook:col-span-2">
            <TextField label="Até" type="date" value={draft.to ?? ''} onChange={(e) => setDraft({ ...draft, to: e.target.value || undefined })} />
          </div>
          <div className="flex items-end gap-2 notebook:col-span-2">
            <Button type="submit" loading={isFetching && !isPending}>
              Filtrar
            </Button>
            <Button variant="secondary" onClick={clear}>
              Limpar
            </Button>
          </div>
        </form>
      </Card>

      <Card>
        {isPending ? (
          <div className="flex flex-col gap-3 p-6">
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} className="h-10" />
            ))}
          </div>
        ) : isError ? (
          <div className="p-6">
            <Alert tone="error" title="Não foi possível carregar a auditoria">
              <button type="button" className="underline" onClick={() => void refetch()}>
                Tentar novamente
              </button>
            </Alert>
          </div>
        ) : data.items.length === 0 ? (
          <EmptyState icon={ScrollText} title="Nenhum registro encontrado" description="Ajuste os filtros para ver outros períodos." />
        ) : (
          <>
            <Table>
              <caption className="sr-only">Registros de auditoria</caption>
              <thead>
                <tr>
                  <Th>Data e hora</Th>
                  <Th>Usuário</Th>
                  <Th>Ação</Th>
                  <Th>Resultado</Th>
                  <Th>IP</Th>
                  <Th className="text-right">Detalhes</Th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((log) => (
                  <tr key={log.id}>
                    <Td className="whitespace-nowrap">{formatDateTime(log.occurredAt)}</Td>
                    <Td className="max-w-64 truncate">{log.userEmail ?? 'Anônimo'}</Td>
                    <Td>{actionLabel(log.action)}</Td>
                    <Td>
                      <ResultBadge result={log.result} />
                    </Td>
                    <Td className="text-text-muted">{log.ipAddress ?? '—'}</Td>
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => setSelected(log)}>
                        Ver
                      </Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={(page) => setFilters({ ...filters, page })}
            />
          </>
        )}
      </Card>

      <AuditDetailsModal log={selected} onClose={() => setSelected(null)} />
    </>
  )
}

function ResultBadge({ result }: { result: AuditResult }) {
  return result === 'Success' ? <Badge tone="success">Sucesso</Badge> : <Badge tone="error">Falha</Badge>
}

function AuditDetailsModal({ log, onClose }: { log: AuditLog | null; onClose: () => void }) {
  return (
    <Modal open={log !== null} onClose={onClose} title={log ? actionLabel(log.action) : ''}>
      {log && (
        <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-body">
          <dt className="text-text-muted">Data e hora</dt>
          <dd>{formatDateTime(log.occurredAt)}</dd>
          <dt className="text-text-muted">Usuário</dt>
          <dd className="break-all">{log.userEmail ?? 'Anônimo'}</dd>
          <dt className="text-text-muted">Resultado</dt>
          <dd>
            <ResultBadge result={log.result} />
          </dd>
          <dt className="text-text-muted">Código</dt>
          <dd className="font-mono text-caption">{log.action}</dd>
          <dt className="text-text-muted">Registro afetado</dt>
          <dd className="break-all">{log.entityType ? `${log.entityType} ${log.entityId ?? ''}` : '—'}</dd>
          <dt className="text-text-muted">IP</dt>
          <dd>{log.ipAddress ?? '—'}</dd>
          <dt className="col-span-2 text-text-muted">Dados</dt>
          <dd className="col-span-2">
            <pre className="overflow-x-auto rounded-md bg-surface-muted p-3 font-mono text-caption">
              {log.details ? JSON.stringify(JSON.parse(log.details), null, 2) : 'Sem dados adicionais.'}
            </pre>
          </dd>
        </dl>
      )}
    </Modal>
  )
}
