export type CsvColumn<T> = { header: string; value: (row: T) => string | number | null }

/** Valor seguro para CSV: separador ";" (Excel em português) e fórmulas neutralizadas (=, +, -, @). */
function cell(value: string | number | null) {
  if (value === null) return ''
  if (typeof value === 'number') return value.toLocaleString('pt-BR', { maximumFractionDigits: 4, useGrouping: false })
  const text = '=+-@'.includes(value[0] ?? '') ? `'${value}` : value
  return /[;"\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

/** CSV em UTF-8 com BOM, para abrir direto no Excel com acentos. */
export function toCsv<T>(columns: readonly CsvColumn<T>[], rows: readonly T[]) {
  const lines = [columns.map((c) => cell(c.header)).join(';'), ...rows.map((row) => columns.map((c) => cell(c.value(row))).join(';'))]
  return '﻿' + lines.join('\r\n')
}

export function downloadCsv<T>(fileName: string, columns: readonly CsvColumn<T>[], rows: readonly T[]) {
  const url = URL.createObjectURL(new Blob([toCsv(columns, rows)], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
