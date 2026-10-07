import { Alert, Badge, Skeleton } from '@/components/ui/Feedback'
import { Switch } from '@/components/ui/Switch'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { Table, Td, Th } from '@/components/ui/Table'
import { cn } from '@/lib/cn'
import { formatDate, formatNumber } from '@/lib/format'
import { useProductOverview, useSetProductSeasonal, type ProductOverview, type StoreInventory } from './catalogApi'

/** Ficha do produto (decisão 34): serve para conferir a importação com o ERP. */
export function ProductOverviewView({ productId }: { productId: number }) {
  const { data, isPending, isError, refetch } = useProductOverview(productId)

  if (isPending)
    return (
      <div className="flex flex-col gap-3" aria-busy="true">
        {Array.from({ length: 5 }, (_, i) => (
          <Skeleton key={i} className="h-10" />
        ))}
      </div>
    )

  if (isError)
    return (
      <Alert tone="error" title="Não foi possível carregar a ficha do produto">
        <button type="button" className="underline" onClick={() => void refetch()}>
          Tentar novamente
        </button>
      </Alert>
    )

  return (
    <div className="flex flex-col gap-6">
      <ProductHeader overview={data} />
      <StoresTable overview={data} />
      <TransfersTable overview={data} />
    </div>
  )
}

function ProductHeader({ overview }: { overview: ProductOverview }) {
  const { product } = overview
  return (
    <div className="flex flex-col gap-2">
      <p className="text-subtitle text-text">{product.description}</p>
      <p className="text-body text-text-muted">
        <span className="font-mono">{product.code}</span> · {product.brandName} · {product.categoryName}
        {product.reference && ` · Ref. ${product.reference}`}
      </p>
      <div className="flex flex-wrap gap-2">
        {product.isActive ? <Badge tone="success">Ativo</Badge> : <Badge>Inativo</Badge>}
        {product.categoryExcluded && <Badge>Fora das análises</Badge>}
        {product.isSeasonal && <Badge tone="warning">Sazonal: sem sugestões</Badge>}
      </div>
      <SeasonalToggle productId={product.id} seasonal={product.isSeasonal} />
    </div>
  )
}

/** Decisão 37: o Administrador tira das sugestões produtos fora de época (ex.: figurinhas da Copa). */
function SeasonalToggle({ productId, seasonal }: { productId: number; seasonal: boolean }) {
  const { user } = useAuth()
  const toast = useToast()
  const setSeasonal = useSetProductSeasonal()
  if (!hasRole(user?.role, ['Administrador'])) return null

  const change = async (value: boolean) => {
    try {
      await setSeasonal.mutateAsync({ id: productId, seasonal: value })
      toast.show(value ? 'Produto marcado como sazonal. Gere uma nova análise para tirá-lo das sugestões.' : 'Produto volta às sugestões na próxima análise.')
    } catch {
      toast.show('Não foi possível alterar o produto.', 'error')
    }
  }

  return (
    <div className="flex items-center gap-3 rounded-md border border-border bg-surface-muted p-3">
      <Switch checked={seasonal} onChange={(value) => void change(value)} label="Sazonal / fora de época" disabled={setSeasonal.isPending} />
      <span className="flex flex-col">
        <span className="text-body text-text">Sazonal / fora de época</span>
        <span className="text-caption text-text-subtle">Não gera sugestão de transferência nem de compra enquanto estiver marcado.</span>
      </span>
    </div>
  )
}

const quantity = (value: number | null) => (value === null ? '—' : formatNumber(value))

function StoresTable({ overview }: { overview: ProductOverview }) {
  const sum = (pick: (s: StoreInventory) => number | null) =>
    overview.stores.some((s) => pick(s) !== null) ? overview.stores.reduce((total, s) => total + (pick(s) ?? 0), 0) : null

  return (
    <section className="flex flex-col gap-3" aria-labelledby="overview-stores">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 id="overview-stores" className="text-subtitle">
          Estoque e vendas por loja
        </h3>
        <span className="text-caption text-text-muted">
          {overview.stockDate ? `Estoque de ${formatDate(overview.stockDate)}` : 'Nenhum estoque importado ainda'}
        </span>
      </div>
      <Table>
        <caption className="sr-only">Estoque, venda de 12 meses, venda média diária e cobertura por loja</caption>
        <thead>
          <tr>
            <Th>Loja</Th>
            <Th className="text-right">Estoque</Th>
            <Th className="text-right">Venda 12 meses</Th>
            <Th className="text-right">VMD</Th>
            <Th className="text-right">Cobertura</Th>
          </tr>
        </thead>
        <tbody>
          {overview.stores.map((store) => (
            <tr key={store.storeCode}>
              <Td className="whitespace-nowrap">
                {store.storeCode} {store.storeName}
              </Td>
              <Td className="text-right tabular-nums">
                {/* Cor num span próprio: no próprio Td, a cor padrão do texto prevaleceria. */}
                <span className={cn(store.stock !== null && store.stock < 0 && 'font-medium text-error')}>{quantity(store.stock)}</span>
              </Td>
              <Td className="text-right tabular-nums">{quantity(store.sold12Months)}</Td>
              <Td className="text-right tabular-nums">{store.dailyAverage ? formatNumber(store.dailyAverage) : '—'}</Td>
              <Td className="whitespace-nowrap text-right tabular-nums">
                {store.coverageDays === null ? '—' : `${formatNumber(store.coverageDays, 0)} dias`}
              </Td>
            </tr>
          ))}
          <tr className="font-medium">
            <Td>Rede</Td>
            <Td className="text-right tabular-nums">{quantity(sum((s) => s.stock))}</Td>
            <Td className="text-right tabular-nums">{quantity(sum((s) => s.sold12Months))}</Td>
            <Td className="text-right tabular-nums">{quantity(sum((s) => s.dailyAverage))}</Td>
            <Td />
          </tr>
        </tbody>
      </Table>
      <p className="text-caption text-text-subtle">
        VMD = venda dos 12 meses ÷ 365. Cobertura = estoque ÷ VMD (estoque negativo conta como zero). Estoque negativo aparece em
        vermelho.
      </p>
    </section>
  )
}

function TransfersTable({ overview }: { overview: ProductOverview }) {
  return (
    <section className="flex flex-col gap-3" aria-labelledby="overview-transfers">
      <h3 id="overview-transfers" className="text-subtitle">
        Últimas transferências
      </h3>
      <p className="text-caption text-text-subtle">
        Cada transferência aparece em duas linhas: o envio na origem e o recebimento no destino, cada um com sua data e usuário.
      </p>
      {overview.recentTransfers.length === 0 ? (
        <p className="text-body text-text-muted">Nenhuma transferência importada para este produto.</p>
      ) : (
        <Table>
          <caption className="sr-only">Últimas movimentações de transferência do produto</caption>
          <thead>
            <tr>
              <Th>Data</Th>
              <Th>Movimento</Th>
              <Th>De → Para</Th>
              <Th>Lançamento</Th>
              <Th className="text-right">Qtd.</Th>
              <Th>Usuário</Th>
            </tr>
          </thead>
          <tbody>
            {overview.recentTransfers.map((t, index) => (
              <tr key={`${t.date}-${index}`}>
                <Td className="whitespace-nowrap">{formatDate(t.date)}</Td>
                <Td>{t.isCancellation ? <Badge tone="warning">Cancelamento</Badge> : 'Transferência'}</Td>
                <Td className="whitespace-nowrap">
                  {t.originCode} → {t.destinationCode}
                </Td>
                <Td>{t.direction === 'In' ? 'Recebido (entrada)' : 'Enviado (saída)'}</Td>
                <Td className="text-right tabular-nums">{formatNumber(t.quantity)}</Td>
                <Td className="text-caption text-text-muted">{t.userName ?? '—'}</Td>
              </tr>
            ))}
          </tbody>
        </Table>
      )}
    </section>
  )
}
