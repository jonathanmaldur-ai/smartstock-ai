import { Package } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Badge, EmptyState } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { TableError, TableLoading } from './CatalogStates'
import { useCategories, useProducts, type Product } from './catalogApi'
import { ProductOverviewView } from './ProductOverviewView'

export function ProductsPage() {
  const [search, setSearch] = useState('')
  const [categoryId, setCategoryId] = useState<number | undefined>()
  const [isActive, setIsActive] = useState<boolean | undefined>()
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Product | null>(null)
  const debouncedSearch = useDebouncedValue(search)
  const categories = useCategories()
  const { data, isPending, isError, refetch } = useProducts({ search: debouncedSearch, categoryId, isActive, page })

  const resetPage = <T,>(setter: (value: T) => void) => (value: T) => {
    setter(value)
    setPage(1)
  }

  return (
    <>
      <PageHeader title="Produtos" description="Catálogo importado do ERP. Os dados são atualizados a cada importação." />

      <Card className="mb-6 grid gap-4 p-4 tablet:grid-cols-2 tablet:p-6 notebook:grid-cols-4">
        <div className="notebook:col-span-2">
          <TextField
            label="Buscar"
            type="search"
            placeholder="Código, descrição ou referência"
            value={search}
            onChange={(e) => resetPage(setSearch)(e.target.value)}
          />
        </div>
        <SelectField
          label="Categoria"
          value={categoryId ?? ''}
          onChange={(e) => resetPage(setCategoryId)(e.target.value ? Number(e.target.value) : undefined)}
          options={[
            { value: '', label: 'Todas' },
            ...(categories.data ?? [])
              .toSorted((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
              .map((c) => ({ value: String(c.id), label: c.name })),
          ]}
        />
        <SelectField
          label="Situação"
          value={isActive === undefined ? '' : String(isActive)}
          onChange={(e) => resetPage(setIsActive)(e.target.value === '' ? undefined : e.target.value === 'true')}
          options={[
            { value: '', label: 'Todas' },
            { value: 'true', label: 'Ativos' },
            { value: 'false', label: 'Inativos' },
          ]}
        />
      </Card>

      <Card>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="os produtos" onRetry={() => void refetch()} />
        ) : data.totalCount === 0 ? (
          <EmptyState
            icon={Package}
            title={debouncedSearch || categoryId || isActive !== undefined ? 'Nenhum produto encontrado' : 'Nenhum produto cadastrado'}
            description={
              debouncedSearch || categoryId || isActive !== undefined
                ? 'Ajuste a busca ou os filtros.'
                : 'Importe os arquivos de marcas e produtos para começar.'
            }
            action={
              <Link to="/importacoes" className="text-body text-primary hover:underline">
                Ir para Importações
              </Link>
            }
          />
        ) : (
          <>
            <Table>
              <caption className="sr-only">Produtos</caption>
              <thead>
                <tr>
                  <Th>Código</Th>
                  <Th>Descrição</Th>
                  <Th>Marca</Th>
                  <Th>Categoria</Th>
                  <Th>Unid.</Th>
                  <Th>Situação</Th>
                  <Th className="text-right">Ficha</Th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((product) => (
                  <tr key={product.id}>
                    <Td className="whitespace-nowrap font-mono text-caption">{product.code}</Td>
                    <Td className="min-w-72">
                      <span className="flex flex-col">
                        <span>{product.description}</span>
                        {product.reference && <span className="text-caption text-text-subtle">Ref. {product.reference}</span>}
                      </span>
                    </Td>
                    <Td className="whitespace-nowrap">{product.brandName}</Td>
                    <Td>
                      <span className="flex flex-col items-start gap-1">
                        <span className="whitespace-nowrap">{product.categoryName}</span>
                        {product.subcategoryName && <span className="text-caption text-text-subtle">{product.subcategoryName}</span>}
                        {product.categoryExcluded && <Badge>Fora das análises</Badge>}
                      </span>
                    </Td>
                    <Td>{product.unit ?? '—'}</Td>
                    <Td>{product.isActive ? <Badge tone="success">Ativo</Badge> : <Badge>Inativo</Badge>}</Td>
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => setSelected(product)} aria-label={`Ver ficha de ${product.description}`}>
                        Ver ficha
                      </Button>
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
          </>
        )}
      </Card>

      <Modal open={selected !== null} onClose={() => setSelected(null)} title="Ficha do produto" size="lg">
        {selected && <ProductOverviewView productId={selected.id} />}
      </Modal>
    </>
  )
}
