import { Pencil } from 'lucide-react'
import { useEffect, useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button, IconButton } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Alert, Badge } from '@/components/ui/Feedback'
import { TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/api'
import { TableError, TableLoading } from './CatalogStates'
import { storeTypeLabels, useStores, useUpdateStore, type Store } from './catalogApi'

export function StoresPage() {
  const { user } = useAuth()
  const isAdmin = hasRole(user?.role, ['Administrador'])
  const { data, isPending, isError, refetch } = useStores()
  const [editing, setEditing] = useState<Store | null>(null)

  return (
    <>
      <PageHeader
        title="Lojas"
        description="Unidades da rede. O código é o que identifica a loja nas planilhas e não pode ser alterado."
      />
      <Card>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="as lojas" onRetry={() => void refetch()} />
        ) : (
          <Table>
            <caption className="sr-only">Lojas</caption>
            <thead>
              <tr>
                <Th>Código</Th>
                <Th>Nome</Th>
                <Th>Cidade</Th>
                <Th>Tipo</Th>
                <Th>Situação</Th>
                {isAdmin && <Th className="text-right">Editar</Th>}
              </tr>
            </thead>
            <tbody>
              {data.map((store) => (
                <tr key={store.id} className={store.status === 'Closed' ? 'opacity-60' : undefined}>
                  <Td className="font-mono">{store.code}</Td>
                  <Td className="font-medium">{store.name}</Td>
                  <Td>{store.city ?? '—'}</Td>
                  <Td>{storeTypeLabels[store.type]}</Td>
                  <Td>
                    {store.status === 'Active' ? <Badge tone="success">Ativa</Badge> : <Badge>Fechada: ignorada nas importações</Badge>}
                  </Td>
                  {isAdmin && (
                    <Td className="text-right">
                      <IconButton label={`Editar ${store.name}`} onClick={() => setEditing(store)}>
                        <Pencil aria-hidden className="size-4" />
                      </IconButton>
                    </Td>
                  )}
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>
      <EditStoreModal store={editing} onClose={() => setEditing(null)} />
    </>
  )
}

function EditStoreModal({ store, onClose }: { store: Store | null; onClose: () => void }) {
  const toast = useToast()
  const update = useUpdateStore()
  const [name, setName] = useState('')
  const [city, setCity] = useState('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setName(store?.name ?? '')
    setCity(store?.city ?? '')
    setError(null)
  }, [store])

  const save = async () => {
    if (!store) return
    if (!name.trim()) {
      setError('Informe o nome da unidade.')
      return
    }
    try {
      await update.mutateAsync({ id: store.id, name: name.trim(), city: city.trim() || null })
      toast.show('Unidade atualizada.')
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar.')
    }
  }

  return (
    <Modal
      open={store !== null}
      onClose={onClose}
      title={`Editar loja ${store?.code ?? ''}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button loading={update.isPending} onClick={() => void save()}>
            Salvar
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error && <Alert tone="error">{error}</Alert>}
        <TextField label="Nome" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} />
        <TextField label="Cidade" value={city} maxLength={100} onChange={(e) => setCity(e.target.value)} />
      </div>
    </Modal>
  )
}
