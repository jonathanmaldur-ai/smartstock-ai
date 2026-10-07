import { FolderTree, Merge } from 'lucide-react'
import { useMemo, useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Alert, Badge, EmptyState } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Switch } from '@/components/ui/Switch'
import { Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/api'
import { TableError, TableLoading } from './CatalogStates'
import { useCategories, useMergeCategory, useSetCategoryExcluded, type Category } from './catalogApi'

export function CategoriesPage() {
  const { user } = useAuth()
  const isAdmin = hasRole(user?.role, ['Administrador'])
  const toast = useToast()
  const { data, isPending, isError, refetch } = useCategories()
  const setExcluded = useSetCategoryExcluded()
  const [search, setSearch] = useState('')
  const [merging, setMerging] = useState<Category | null>(null)

  const filtered = useMemo(() => {
    const term = search.trim().toLocaleUpperCase('pt-BR')
    return (data ?? []).filter((c) => !term || c.name.includes(term) || c.aliases.some((a) => a.includes(term)))
  }, [data, search])

  const toggleExcluded = async (category: Category, excluded: boolean) => {
    try {
      await setExcluded.mutateAsync({ id: category.id, excluded })
      toast.show(excluded ? `${category.name} ficará fora das análises.` : `${category.name} volta a entrar nas análises.`)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível alterar.', 'error')
    }
  }

  return (
    <>
      <PageHeader
        title="Categorias"
        description="Categorias oficiais, as grafias que o sistema corrige automaticamente e o que fica fora das análises."
      />

      {isAdmin && (
        <div className="mb-6">
          <Alert tone="info" title="Como revisar">
            Se duas categorias forem a mesma coisa escrita de jeitos diferentes, use <strong>Unificar</strong>: os produtos passam para a
            categoria certa e a grafia errada é corrigida sozinha nas próximas importações. Use a chave <strong>Nas análises</strong> para
            tirar itens que não são mercadoria.
          </Alert>
        </div>
      )}

      <Card className="mb-6 p-4 tablet:p-6">
        <div className="max-w-md">
          <TextField label="Buscar" type="search" placeholder="Nome ou grafia" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
      </Card>

      <Card>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="as categorias" onRetry={() => void refetch()} />
        ) : filtered.length === 0 ? (
          <EmptyState icon={FolderTree} title="Nenhuma categoria encontrada" />
        ) : (
          <Table>
            <caption className="sr-only">Categorias</caption>
            <thead>
              <tr>
                <Th>Categoria</Th>
                <Th className="text-right">Produtos</Th>
                <Th>Grafias corrigidas automaticamente</Th>
                <Th>Nas análises</Th>
                {isAdmin && <Th className="text-right">Ações</Th>}
              </tr>
            </thead>
            <tbody>
              {filtered.map((category) => (
                <tr key={category.id}>
                  <Td className="font-medium">{category.name}</Td>
                  <Td className="text-right tabular-nums">{category.productCount.toLocaleString('pt-BR')}</Td>
                  <Td>
                    <span className="flex max-w-md flex-wrap gap-1">
                      {category.aliases.length === 0 ? '—' : category.aliases.map((alias) => <Badge key={alias}>{alias}</Badge>)}
                    </span>
                  </Td>
                  <Td>
                    {isAdmin ? (
                      <Switch
                        checked={!category.excludedFromAnalysis}
                        onChange={(included) => void toggleExcluded(category, !included)}
                        label={`${category.name} nas análises`}
                        disabled={setExcluded.isPending}
                      />
                    ) : category.excludedFromAnalysis ? (
                      <Badge>Fora</Badge>
                    ) : (
                      <Badge tone="success">Sim</Badge>
                    )}
                  </Td>
                  {isAdmin && (
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" icon={<Merge aria-hidden className="size-4" />} onClick={() => setMerging(category)}>
                        Unificar
                      </Button>
                    </Td>
                  )}
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Card>

      <MergeModal source={merging} categories={data ?? []} onClose={() => setMerging(null)} />
    </>
  )
}

function MergeModal({ source, categories, onClose }: { source: Category | null; categories: Category[]; onClose: () => void }) {
  const toast = useToast()
  const merge = useMergeCategory()
  const [targetId, setTargetId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const target = categories.find((c) => String(c.id) === targetId)

  const close = () => {
    setTargetId('')
    setError(null)
    onClose()
  }

  const confirm = async () => {
    if (!source || !target) return
    setError(null)
    try {
      await merge.mutateAsync({ sourceId: source.id, targetId: target.id })
      toast.show(`${source.name} foi unificada com ${target.name}.`)
      close()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível unificar.')
    }
  }

  return (
    <Modal
      open={source !== null}
      onClose={close}
      title={`Unificar "${source?.name ?? ''}"`}
      description="Escolha a categoria correta. Esta categoria deixa de existir e passa a ser corrigida automaticamente."
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            Cancelar
          </Button>
          <Button disabled={!target} loading={merge.isPending} onClick={() => void confirm()}>
            Unificar
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {error && <Alert tone="error">{error}</Alert>}
        <SelectField
          label="Unificar com"
          value={targetId}
          onChange={(e) => setTargetId(e.target.value)}
          options={[
            { value: '', label: 'Selecione…' },
            ...categories
              .filter((c) => c.id !== source?.id)
              .toSorted((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
              .map((c) => ({ value: String(c.id), label: `${c.name} (${c.productCount.toLocaleString('pt-BR')})` })),
          ]}
        />
        {source && target && (
          <p className="text-body text-text">
            Os <strong>{source.productCount.toLocaleString('pt-BR')}</strong> produtos de {source.name} passarão para{' '}
            <strong>{target.name}</strong>.
          </p>
        )}
      </div>
    </Modal>
  )
}
