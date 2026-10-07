const dateTime = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

export function formatDateTime(iso: string) {
  return dateTime.format(new Date(iso))
}

/** Data sem hora vinda da API ("2026-09-23"), sem conversão de fuso: "23/09/2026". */
export function formatDate(isoDate: string) {
  const [year, month, day] = isoDate.split('-')
  return `${day}/${month}/${year}`
}

/** Hoje no fuso do navegador, no formato do campo de data ("2026-09-23"). */
export function todayIsoDate() {
  const now = new Date()
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Quantidades: separador brasileiro e até 2 casas (estoque pode ser fracionado). */
export function formatNumber(value: number, maximumFractionDigits = 2) {
  return value.toLocaleString('pt-BR', { maximumFractionDigits })
}

/** Valores em reais: "R$ 1.234,56". */
export function formatMoney(value: number, maximumFractionDigits = 2) {
  return value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', minimumFractionDigits: maximumFractionDigits, maximumFractionDigits })
}
