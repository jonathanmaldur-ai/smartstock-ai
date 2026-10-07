import { Tags } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Card } from '@/components/ui/Card'
import { Badge, EmptyState } from '@/components/ui/Feedback'
import { TextField } from '@/components/ui/Field'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useDebouncedValue } from '@/lib/useDebouncedValue'
import { TableError, TableLoading } from './CatalogStates'
import { useBrands } from './catalogApi'

export function BrandsPage() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const debouncedSearch = useDebouncedValue(search)
  const { data, isPending, isError, refetch } = useBrands(debouncedSearch, page)

  return (
    <>
      <PageHeader title="Marcas" description="Marcas importadas do ERP, com a quantidade de produtos de cada uma." />

      <Card className="mb-6 p-4 tablet:p-6">
        <div className="max-w-md">
          <TextField
            label="Buscar"
            type="search"
            placeholder="Nome ou código da marca"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
        </div>
      </Card>

      <Card>
        {isPending ? (
          <TableLoading />
        ) : isError ? (
          <TableError what="as marcas" onRetry={() => void refetch()} />
        ) : data.totalCount === 0 ? (
          <EmptyState
            icon={Tags}
            title={debouncedSearch ? 'Nenhuma marca encontrada' : 'Nenhuma marca cadastrada'}
            description={debouncedSearch ? 'Tente outro nome ou código.' : 'Importe o arquivo de marcas em Importações.'}
          />
        ) : (
          <>
            <Table>
              <caption className="sr-only">Marcas</caption>
              <thead>
                <tr>
                  <Th>Código</Th>
                  <Th>Marca</Th>
                  <Th className="text-right">Produtos</Th>
                  <Th>Situação</Th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((brand) => (
                  <tr key={brand.id}>
                    <Td className="font-mono text-caption">{brand.code}</Td>
                    <Td>{brand.name}</Td>
                    <Td className="text-right tabular-nums">{brand.productCount.toLocaleString('pt-BR')}</Td>
                    <Td>{brand.isActive ? <Badge tone="success">Ativa</Badge> : <Badge>Inativa</Badge>}</Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
          </>
        )}
      </Card>
    </>
  )
}
