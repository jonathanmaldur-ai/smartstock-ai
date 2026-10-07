import { Card } from '@/components/ui/Card'
import { SelectField, TextField } from '@/components/ui/Field'
import { useCategories, useStores } from '@/features/catalog/catalogApi'

type FilterField =
  | { kind: 'store'; label: string; value?: number; onChange: (value: number | undefined) => void }
  | { kind: 'category'; label: string; value?: number; onChange: (value: number | undefined) => void }
  | { kind: 'options'; label: string; value?: string; options: readonly { value: string; label: string }[]; onChange: (value: string | undefined) => void }

/** Busca + filtros das telas de análise (loja, categoria e listas próprias de cada tela). */
export function AnalysisFilters({ search, onSearch, fields }: { search: string; onSearch: (value: string) => void; fields: readonly FilterField[] }) {
  const stores = useStores()
  const categories = useCategories()
  const storeOptions = (stores.data ?? [])
    .filter((s) => s.status === 'Active')
    .map((s) => ({ value: String(s.id), label: `${s.code} ${s.name}` }))
  const categoryOptions = (categories.data ?? [])
    .filter((c) => !c.excludedFromAnalysis)
    .toSorted((a, b) => a.name.localeCompare(b.name, 'pt-BR'))
    .map((c) => ({ value: String(c.id), label: c.name }))

  const toNumber = (value: string) => (value ? Number(value) : undefined)

  return (
    <Card className="mb-6 grid gap-4 p-4 tablet:grid-cols-2 tablet:p-6 notebook:grid-cols-4">
      <TextField label="Buscar" type="search" placeholder="Código, descrição ou referência" value={search} onChange={(e) => onSearch(e.target.value)} />
      {fields.map((field) => {
        const all = { value: '', label: 'Todas' }
        if (field.kind === 'store')
          return (
            <SelectField
              key={field.label}
              label={field.label}
              value={field.value ?? ''}
              options={[all, ...storeOptions]}
              onChange={(e) => field.onChange(toNumber(e.target.value))}
            />
          )
        if (field.kind === 'category')
          return (
            <SelectField
              key={field.label}
              label={field.label}
              value={field.value ?? ''}
              options={[all, ...categoryOptions]}
              onChange={(e) => field.onChange(toNumber(e.target.value))}
            />
          )
        return (
          <SelectField
            key={field.label}
            label={field.label}
            value={field.value ?? ''}
            options={[all, ...field.options]}
            onChange={(e) => field.onChange(e.target.value || undefined)}
          />
        )
      })}
    </Card>
  )
}
