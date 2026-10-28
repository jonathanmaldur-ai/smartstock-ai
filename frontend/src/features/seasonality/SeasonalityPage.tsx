import { CalendarRange, Check, Pencil, Plus, X } from 'lucide-react'
import { useState } from 'react'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card, CardHeader } from '@/components/ui/Card'
import { Alert, Badge, EmptyState, Skeleton } from '@/components/ui/Feedback'
import { SelectField, TextField } from '@/components/ui/Field'
import { Modal } from '@/components/ui/Modal'
import { Switch } from '@/components/ui/Switch'
import { Pagination, Table, Td, Th } from '@/components/ui/Table'
import { useToast } from '@/components/ui/Toast'
import { hasRole, useAuth } from '@/features/auth/useAuth'
import { TableError, TableLoading } from '@/features/catalog/CatalogStates'
import { ApiError } from '@/lib/api'
import { cn } from '@/lib/cn'
import { formatNumber } from '@/lib/format'
import {
  kindLabels,
  monthNames,
  periodLabel,
  useDecideSeasonal,
  useSaveSeason,
  useSeasonalProducts,
  useSeasonalSuggestions,
  useSeasons,
  useSetProductSeason,
  type Season,
  type SeasonFilter,
  type SeasonInput,
  type SeasonalProduct,
} from './seasonalityApi'

type View = 'suggestions' | 'products' | 'seasons'

const views: { value: View; label: string }[] = [
  { value: 'suggestions', label: 'Sugestões' },
  { value: 'products', label: 'Produtos sazonais' },
  { value: 'seasons', label: 'Épocas' },
]

/**
 * Sazonalidade (decisão 65): a análise sugere os produtos de época (palavra-chave ou venda que parou) e o
 * Administrador confirma. Fora da época, o produto vira "Sobra de época" e sai das sugestões de transferência e compra.
 */
export function SeasonalityPage() {
  const { user } = useAuth()
  const isAdmin = hasRole(user?.role, ['Administrador'])
  const seasons = useSeasons()
  const [view, setView] = useState<View>('suggestions')
  const [filter, setFilter] = useState<SeasonFilter>({})

  const select = (next: SeasonFilter) =>
    setFilter((current) => (current.seasonId === next.seasonId && current.withoutSeason === next.withoutSeason ? {} : next))

  return (
    <>
      <PageHeader
        title="Sazonalidade"
        description="Produtos que só vendem numa época (Natal, Páscoa, Copa...). Fora da época, eles saem das sugestões de transferência e compra."
      />

      {isAdmin && (
        <div className="mb-6">
          <Alert tone="info" title="Como funciona">
            A cada análise, o sistema sugere produtos de época pela categoria ou descrição e pelos que pararam de vender. Nada muda até você{' '}
            <strong>confirmar</strong>. Na época, o produto usa a venda dos últimos 30 dias para não faltar no pico; fora dela, vira{' '}
            <strong>Sobra de época</strong>: candidato a promoção, sem sugestão e sem alarme de ruptura.
          </Alert>
        </div>
      )}

      <SeasonCards seasons={seasons.data} loading={seasons.isPending} filter={filter} onSelect={select} />

      <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Visão">
        {views.map((v) => (
          <Button key={v.value} variant={view === v.value ? 'primary' : 'secondary'} size="sm" aria-pressed={view === v.value} onClick={() => setView(v.value)}>
            {v.label}
          </Button>
        ))}
      </div>

      {view === 'suggestions' ? (
        <SuggestionsCard filter={filter} seasons={seasons.data ?? []} isAdmin={isAdmin} />
      ) : view === 'products' ? (
        <ProductsCard filter={filter} seasons={seasons.data ?? []} isAdmin={isAdmin} />
      ) : (
        <SeasonsCard seasons={seasons.data} isAdmin={isAdmin} />
      )}
    </>
  )
}

function SeasonCards({
  seasons,
  loading,
  filter,
  onSelect,
}: {
  seasons: Season[] | undefined
  loading: boolean
  filter: SeasonFilter
  onSelect: (filter: SeasonFilter) => void
}) {
  if (loading)
    return (
      <div className="mb-6 grid gap-3 tablet:grid-cols-2 notebook:grid-cols-4">
        {Array.from({ length: 4 }, (_, i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
    )

  const cardClass = (selected: boolean) =>
    cn(
      'flex flex-col gap-2 rounded-lg border border-border bg-surface p-4 text-left shadow-card transition-colors hover:bg-surface-muted',
      selected && 'border-primary bg-primary-soft',
    )

  return (
    <section className="mb-6 flex flex-col gap-3" aria-label="Épocas">
      <p className="text-caption text-text-muted">Clique numa época para filtrar as sugestões e os produtos.</p>
      <div className="grid gap-3 tablet:grid-cols-2 notebook:grid-cols-4">
        {(seasons ?? [])
          .filter((s) => s.isActive)
          .map((s) => (
            <button key={s.id} type="button" aria-pressed={filter.seasonId === s.id} onClick={() => onSelect({ seasonId: s.id })} className={cardClass(filter.seasonId === s.id)}>
              <span className="flex items-start justify-between gap-2">
                <span className="text-label text-text">{s.name}</span>
                {s.inSeasonNow ? <Badge tone="success">Em época</Badge> : <Badge>Fora de época</Badge>}
              </span>
              <span className="text-caption text-text-muted">{periodLabel(s)}</span>
              <span className="text-caption text-text-muted">
                {s.productCount.toLocaleString('pt-BR')} confirmados · {s.pendingSuggestions.toLocaleString('pt-BR')} sugestões
              </span>
            </button>
          ))}
        <button type="button" aria-pressed={!!filter.withoutSeason} onClick={() => onSelect({ withoutSeason: true })} className={cardClass(!!filter.withoutSeason)}>
          <span className="text-label text-text">Sem época definida</span>
          <span className="text-caption text-text-muted">Pararam de vender, sem palavra-chave de época. Ao confirmar, ficam fora de época até você desmarcar.</span>
        </button>
      </div>
    </section>
  )
}

type DecisionTarget = { ids: number[]; confirm: boolean; count: number }

function SuggestionsCard({ filter, seasons, isAdmin }: { filter: SeasonFilter; seasons: Season[]; isAdmin: boolean }) {
  const toast = useToast()
  const decide = useDecideSeasonal()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [pending, setPending] = useState<DecisionTarget | null>(null)
  const { data, isPending, isError, refetch, isFetching } = useSeasonalSuggestions({ ...filter, search: search || undefined, page })

  const items = data?.items ?? []
  const allSelected = items.length > 0 && items.every((s) => selected.has(s.id))
  const scope = filter.withoutSeason ? 'sem época definida' : seasons.find((s) => s.id === filter.seasonId)?.name
  const toggle = (id: number) =>
    setSelected((current) => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const confirm = async () => {
    if (!pending) return
    try {
      const result = await decide.mutateAsync({ ...filter, ids: pending.ids, confirm: pending.confirm })
      toast.show(`${result.decided.toLocaleString('pt-BR')} sugest${result.decided === 1 ? 'ão' : 'ões'} ${pending.confirm ? 'confirmada' : 'rejeitada'}${result.decided === 1 ? '' : 's'}.`)
      setSelected(new Set())
      setPending(null)
    } catch (e) {
      toast.show(e instanceof ApiError ? e.message : 'Não foi possível registrar a decisão.', 'error')
    }
  }

  return (
    <Card>
      <CardHeader
        title={scope ? `Sugestões: ${scope}` : 'Sugestões'}
        description="Produtos que parecem ser de época. A análise atualiza esta lista; rejeitadas não voltam para a mesma época."
        actions={
          isAdmin &&
          scope &&
          (data?.totalCount ?? 0) > 0 && (
            <>
              <Button variant="secondary" icon={<X aria-hidden className="size-4" />} onClick={() => setPending({ ids: [], confirm: false, count: data!.totalCount })}>
                Rejeitar todas
              </Button>
              <Button icon={<Check aria-hidden className="size-4" />} onClick={() => setPending({ ids: [], confirm: true, count: data!.totalCount })}>
                Confirmar todas
              </Button>
            </>
          )
        }
      />
      <div className="border-b border-border p-4 tablet:px-6">
        <div className="max-w-md">
          <TextField
            label="Buscar"
            type="search"
            placeholder="Código ou descrição"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
        </div>
      </div>

      {isAdmin && selected.size > 0 && (
        <div className="flex flex-col gap-3 border-b border-border bg-surface-muted p-4 tablet:flex-row tablet:items-center tablet:justify-between">
          <p className="text-body text-text">
            {selected.size} marcado{selected.size > 1 ? 's' : ''}
          </p>
          <div className="flex gap-2">
            <Button variant="secondary" size="sm" onClick={() => setPending({ ids: [...selected], confirm: false, count: selected.size })}>
              Rejeitar marcados
            </Button>
            <Button size="sm" onClick={() => setPending({ ids: [...selected], confirm: true, count: selected.size })}>
              Confirmar marcados
            </Button>
          </div>
        </div>
      )}

      {isPending ? (
        <TableLoading />
      ) : isError ? (
        <TableError what="as sugestões" onRetry={() => void refetch()} />
      ) : data.totalCount === 0 ? (
        <EmptyState icon={CalendarRange} title="Nenhuma sugestão pendente" description="As sugestões aparecem depois de cada análise." />
      ) : (
        <div className={cn(isFetching && 'opacity-60')}>
          <Table>
            <caption className="sr-only">Sugestões de produtos sazonais</caption>
            <thead>
              <tr>
                {isAdmin && (
                  <Th className="w-10">
                    <input
                      type="checkbox"
                      className="size-4 accent-primary"
                      aria-label="Marcar todas desta página"
                      checked={allSelected}
                      onChange={() => setSelected(allSelected ? new Set() : new Set(items.map((s) => s.id)))}
                    />
                  </Th>
                )}
                <Th>Produto</Th>
                <Th>Época sugerida</Th>
                <Th>Motivo</Th>
                <Th className="text-right">Estoque na rede</Th>
                <Th className="text-right">Venda 12 meses</Th>
              </tr>
            </thead>
            <tbody>
              {items.map((s) => (
                <tr key={s.id}>
                  {isAdmin && (
                    <Td>
                      <input
                        type="checkbox"
                        className="size-4 accent-primary"
                        aria-label={`Marcar ${s.description}`}
                        checked={selected.has(s.id)}
                        onChange={() => toggle(s.id)}
                      />
                    </Td>
                  )}
                  <Td className="min-w-64">
                    <span className="flex flex-col">
                      <span>{s.description}</span>
                      <span className="text-caption text-text-subtle">
                        <span className="font-mono">{s.code}</span> · {s.brand} · {s.category}
                      </span>
                    </span>
                  </Td>
                  <Td>{s.seasonName ?? <span className="text-text-muted">Sem época</span>}</Td>
                  <Td className="min-w-56">
                    <span className="flex flex-col gap-1">
                      <Badge tone={s.kind === 'SalesStopped' ? 'warning' : 'info'}>{kindLabels[s.kind]}</Badge>
                      <span className="text-caption text-text-muted">{s.evidence}</span>
                    </span>
                  </Td>
                  <Td className="text-right tabular-nums">{formatNumber(s.stock, 0)}</Td>
                  <Td className="text-right tabular-nums">{formatNumber(s.sold12Months, 0)}</Td>
                </tr>
              ))}
            </tbody>
          </Table>
          <Pagination
            page={data.page}
            totalPages={data.totalPages}
            totalCount={data.totalCount}
            onPageChange={(p) => {
              setPage(p)
              setSelected(new Set())
            }}
          />
        </div>
      )}

      <Modal
        open={pending !== null}
        onClose={() => setPending(null)}
        title={pending?.confirm ? 'Confirmar produtos sazonais' : 'Rejeitar sugestões'}
        footer={
          <>
            <Button variant="secondary" onClick={() => setPending(null)}>
              Cancelar
            </Button>
            <Button variant={pending?.confirm ? 'primary' : 'secondary'} loading={decide.isPending} onClick={() => void confirm()}>
              {pending?.confirm ? 'Confirmar' : 'Rejeitar'}
            </Button>
          </>
        }
      >
        {pending && (
          <p className="text-body text-text">
            {pending.confirm ? (
              <>
                <strong>{pending.count.toLocaleString('pt-BR')}</strong> produto{pending.count === 1 ? '' : 's'} passa{pending.count === 1 ? '' : 'm'} a ser
                sazona{pending.count === 1 ? 'l' : 'is'}
                {scope && pending.ids.length === 0 ? ` (${scope})` : ''}. Fora da época, sa{pending.count === 1 ? 'i' : 'em'} das sugestões de transferência e
                compra a partir da próxima análise.
              </>
            ) : (
              <>
                <strong>{pending.count.toLocaleString('pt-BR')}</strong> sugest{pending.count === 1 ? 'ão' : 'ões'} rejeitada{pending.count === 1 ? '' : 's'}: o
                produto continua comum e a sugestão não volta para a mesma época.
              </>
            )}
          </p>
        )}
      </Modal>
    </Card>
  )
}

const stateBadge = (p: SeasonalProduct) =>
  p.state === 'InSeason' ? (
    <Badge tone="success">Em época</Badge>
  ) : p.markedOutOfSeason ? (
    <Badge tone="warning">Fora de época (manual)</Badge>
  ) : (
    <Badge tone="warning">Fora de época</Badge>
  )

function ProductsCard({ filter, seasons, isAdmin }: { filter: SeasonFilter; seasons: Season[]; isAdmin: boolean }) {
  const toast = useToast()
  const setSeason = useSetProductSeason()
  const [search, setSearch] = useState('')
  const [outOfSeasonOnly, setOutOfSeasonOnly] = useState(false)
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<SeasonalProduct | null>(null)
  const { data, isPending, isError, refetch, isFetching } = useSeasonalProducts({
    seasonId: filter.withoutSeason ? undefined : filter.seasonId,
    outOfSeasonOnly: outOfSeasonOnly || undefined,
    search: search || undefined,
    page,
  })

  return (
    <Card>
      <CardHeader title="Produtos sazonais" description="Produtos com época confirmada ou marcados como fora de época." />
      <div className="flex flex-col gap-4 border-b border-border p-4 tablet:flex-row tablet:items-end tablet:px-6">
        <div className="max-w-md flex-1">
          <TextField
            label="Buscar"
            type="search"
            placeholder="Código ou descrição"
            value={search}
            onChange={(e) => {
              setSearch(e.target.value)
              setPage(1)
            }}
          />
        </div>
        <div className="flex h-10 items-center gap-2">
          <Switch
            checked={outOfSeasonOnly}
            onChange={(checked) => {
              setOutOfSeasonOnly(checked)
              setPage(1)
            }}
            label="Só os fora de época"
          />
          <span className="text-body text-text">Só os fora de época (sobras para promoção)</span>
        </div>
      </div>

      {isPending ? (
        <TableLoading />
      ) : isError ? (
        <TableError what="os produtos sazonais" onRetry={() => void refetch()} />
      ) : data.totalCount === 0 ? (
        <EmptyState icon={CalendarRange} title="Nenhum produto sazonal" description="Confirme as sugestões para os produtos aparecerem aqui." />
      ) : (
        <div className={cn(isFetching && 'opacity-60')}>
          <Table>
            <caption className="sr-only">Produtos sazonais</caption>
            <thead>
              <tr>
                <Th>Produto</Th>
                <Th>Época</Th>
                <Th>Hoje</Th>
                <Th className="text-right">Estoque na rede</Th>
                <Th className="text-right">Venda 12 meses</Th>
                {isAdmin && <Th className="text-right">Ações</Th>}
              </tr>
            </thead>
            <tbody>
              {data.items.map((p) => (
                <tr key={p.productId}>
                  <Td className="min-w-64">
                    <span className="flex flex-col">
                      <span>{p.description}</span>
                      <span className="text-caption text-text-subtle">
                        <span className="font-mono">{p.code}</span> · {p.brand} · {p.category}
                      </span>
                    </span>
                  </Td>
                  <Td>{p.seasonName ?? '—'}</Td>
                  <Td>{stateBadge(p)}</Td>
                  <Td className="text-right tabular-nums">{formatNumber(p.stock, 0)}</Td>
                  <Td className="text-right tabular-nums">{formatNumber(p.sold12Months, 0)}</Td>
                  {isAdmin && (
                    <Td className="text-right">
                      <Button variant="ghost" size="sm" icon={<Pencil aria-hidden className="size-4" />} onClick={() => setEditing(p)}>
                        Alterar
                      </Button>
                    </Td>
                  )}
                </tr>
              ))}
            </tbody>
          </Table>
          <Pagination page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onPageChange={setPage} />
        </div>
      )}

      <ProductSeasonModal
        product={editing}
        seasons={seasons}
        saving={setSeason.isPending}
        onClose={() => setEditing(null)}
        onSave={async (seasonId, outOfSeason) => {
          if (!editing) return
          try {
            await setSeason.mutateAsync({ productId: editing.productId, seasonId, outOfSeason })
            toast.show(seasonId === null && !outOfSeason ? `${editing.description} deixou de ser sazonal.` : 'Época do produto alterada.')
            setEditing(null)
          } catch (e) {
            toast.show(e instanceof ApiError ? e.message : 'Não foi possível alterar.', 'error')
          }
        }}
      />
    </Card>
  )
}

function ProductSeasonModal({
  product,
  seasons,
  saving,
  onClose,
  onSave,
}: {
  product: SeasonalProduct | null
  seasons: Season[]
  saving: boolean
  onClose: () => void
  onSave: (seasonId: number | null, outOfSeason: boolean) => Promise<void>
}) {
  // A chave do formulário é o produto: abre sempre com os valores dele.
  return (
    <Modal open={product !== null} onClose={onClose} title={`Época de "${product?.description ?? ''}"`}>
      {product && <ProductSeasonForm key={product.productId} product={product} seasons={seasons} saving={saving} onClose={onClose} onSave={onSave} />}
    </Modal>
  )
}

function ProductSeasonForm({
  product,
  seasons,
  saving,
  onClose,
  onSave,
}: {
  product: SeasonalProduct
  seasons: Season[]
  saving: boolean
  onClose: () => void
  onSave: (seasonId: number | null, outOfSeason: boolean) => Promise<void>
}) {
  const [seasonId, setSeasonId] = useState(product.seasonId ? String(product.seasonId) : '')
  const [outOfSeason, setOutOfSeason] = useState(product.markedOutOfSeason)

  return (
    <div className="flex flex-col gap-4">
      <SelectField
        label="Época"
        value={seasonId}
        onChange={(e) => setSeasonId(e.target.value)}
        options={[{ value: '', label: 'Nenhuma' }, ...seasons.map((s) => ({ value: String(s.id), label: `${s.name} (${periodLabel(s)})` }))]}
      />
      <div className="flex items-center gap-2">
        <Switch checked={outOfSeason} onChange={setOutOfSeason} label="Fora de época até desmarcar" />
        <span className="text-body text-text">Fora de época até desmarcar (sem sugestões, qualquer que seja o mês)</span>
      </div>
      <div className="flex flex-wrap justify-between gap-2">
        <Button variant="ghost" onClick={() => void onSave(null, false)} loading={saving}>
          Não é sazonal
        </Button>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={onClose}>
            Cancelar
          </Button>
          <Button loading={saving} onClick={() => void onSave(seasonId ? Number(seasonId) : null, outOfSeason)}>
            Salvar
          </Button>
        </div>
      </div>
    </div>
  )
}

function SeasonsCard({ seasons, isAdmin }: { seasons: Season[] | undefined; isAdmin: boolean }) {
  const [editing, setEditing] = useState<Season | 'new' | null>(null)

  return (
    <Card>
      <CardHeader
        title="Épocas"
        description="Período de venda e palavras-chave que apontam os produtos de cada época."
        actions={
          isAdmin && (
            <Button icon={<Plus aria-hidden className="size-4" />} onClick={() => setEditing('new')}>
              Nova época
            </Button>
          )
        }
      />
      {!seasons ? (
        <TableLoading />
      ) : (
        <Table>
          <caption className="sr-only">Épocas</caption>
          <thead>
            <tr>
              <Th>Época</Th>
              <Th>Período</Th>
              <Th>Palavras-chave</Th>
              <Th>Situação</Th>
              {isAdmin && <Th className="text-right">Ações</Th>}
            </tr>
          </thead>
          <tbody>
            {seasons.map((s) => (
              <tr key={s.id}>
                <Td className="font-medium">{s.name}</Td>
                <Td>{periodLabel(s)}</Td>
                <Td>
                  <span className="flex max-w-md flex-col gap-1">
                    <span className="flex flex-wrap gap-1">
                      {s.keywords.map((k) => (
                        <Badge key={k}>{k}</Badge>
                      ))}
                    </span>
                    <span className="text-caption text-text-subtle">
                      {s.matchDescription ? 'Categoria, subcategoria e descrição' : 'Só categoria e subcategoria'}
                    </span>
                  </span>
                </Td>
                <Td>{!s.isActive ? <Badge>Desativada</Badge> : s.inSeasonNow ? <Badge tone="success">Em época</Badge> : <Badge>Fora de época</Badge>}</Td>
                {isAdmin && (
                  <Td className="text-right">
                    <Button variant="ghost" size="sm" icon={<Pencil aria-hidden className="size-4" />} onClick={() => setEditing(s)}>
                      Editar
                    </Button>
                  </Td>
                )}
              </tr>
            ))}
          </tbody>
        </Table>
      )}
      <Modal open={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'Nova época' : `Editar "${editing?.name ?? ''}"`}>
        {editing && <SeasonForm key={editing === 'new' ? 'new' : editing.id} season={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
      </Modal>
    </Card>
  )
}

const monthOptions = monthNames.map((name, i) => ({ value: String(i + 1), label: name }))

function SeasonForm({ season, onClose }: { season: Season | null; onClose: () => void }) {
  const toast = useToast()
  const save = useSaveSeason()
  const [form, setForm] = useState<SeasonInput>(
    season
      ? {
          name: season.name,
          startMonth: season.startMonth,
          endMonth: season.endMonth,
          worldCupOnly: season.worldCupOnly,
          keywords: season.keywords,
          matchDescription: season.matchDescription,
          isActive: season.isActive,
        }
      : { name: '', startMonth: 1, endMonth: 1, worldCupOnly: false, keywords: [], matchDescription: true, isActive: true },
  )
  const [keywords, setKeywords] = useState(form.keywords.join(', '))
  const [error, setError] = useState<string | null>(null)

  const submit = async () => {
    setError(null)
    try {
      const input = { ...form, keywords: keywords.split(',').map((k) => k.trim()).filter(Boolean) }
      await save.mutateAsync(season ? { ...input, id: season.id } : input)
      toast.show(season ? `${form.name} atualizada.` : `${form.name} criada.`)
      onClose()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Não foi possível salvar.')
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {error && <Alert tone="error">{error}</Alert>}
      <TextField label="Nome" value={form.name} maxLength={60} onChange={(e) => setForm({ ...form, name: e.target.value })} />
      <div className="grid gap-4 tablet:grid-cols-2">
        <SelectField label="De" value={String(form.startMonth)} options={monthOptions} onChange={(e) => setForm({ ...form, startMonth: Number(e.target.value) })} />
        <SelectField label="Até" value={String(form.endMonth)} options={monthOptions} onChange={(e) => setForm({ ...form, endMonth: Number(e.target.value) })} />
      </div>
      <TextField
        label="Palavras-chave"
        hint="Separadas por vírgula, como palavra inteira (ex.: NATAL, PAPAI NOEL). Valem na categoria e na subcategoria; na descrição, só com a chave abaixo ligada."
        value={keywords}
        onChange={(e) => setKeywords(e.target.value)}
      />
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <Switch checked={form.matchDescription} onChange={(matchDescription) => setForm({ ...form, matchDescription })} label="Procurar também na descrição" />
          <span className="text-body text-text">Procurar também na descrição do produto</span>
        </div>
        <div className="flex items-center gap-2">
          <Switch checked={form.worldCupOnly} onChange={(worldCupOnly) => setForm({ ...form, worldCupOnly })} label="Só em ano de Copa do Mundo" />
          <span className="text-body text-text">Só em ano de Copa do Mundo</span>
        </div>
        <div className="flex items-center gap-2">
          <Switch checked={form.isActive} onChange={(isActive) => setForm({ ...form, isActive })} label="Época ativa" />
          <span className="text-body text-text">Ativa</span>
        </div>
      </div>
      <div className="flex justify-end gap-2">
        <Button variant="secondary" onClick={onClose}>
          Cancelar
        </Button>
        <Button loading={save.isPending} disabled={!form.name.trim()} onClick={() => void submit()}>
          Salvar
        </Button>
      </div>
    </div>
  )
}
