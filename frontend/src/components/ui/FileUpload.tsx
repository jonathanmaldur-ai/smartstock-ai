import { FileSpreadsheet, UploadCloud, X } from 'lucide-react'
import { useId, useRef, useState, type DragEvent } from 'react'
import { cn } from '@/lib/cn'

type FileUploadProps = {
  label: string
  accept: string
  hint?: string
  files: File[]
  onChange: (files: File[]) => void
  /** Permite escolher vários arquivos de uma vez (ex.: vendas de todas as lojas). */
  multiple?: boolean
  disabled?: boolean
}

/** Seleção de arquivo(s) por clique ou arrastando. Acessível por teclado (o input real fica visível ao foco). */
export function FileUpload({ label, accept, hint, files, onChange, multiple = false, disabled }: FileUploadProps) {
  const inputId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  const select = (list: FileList | null) => {
    const selected = Array.from(list ?? [])
    onChange(multiple ? selected : selected.slice(0, 1))
  }

  const remove = (file: File) => {
    onChange(files.filter((f) => f !== file))
    if (inputRef.current) inputRef.current.value = ''
  }

  const onDrop = (event: DragEvent) => {
    event.preventDefault()
    setDragging(false)
    if (!disabled) select(event.dataTransfer.files)
  }

  return (
    <div className="flex flex-col gap-2">
      <span className="text-label text-text">{label}</span>
      {files.length > 0 ? (
        <ul className="flex flex-col gap-2">
          {files.map((file) => (
            <li key={`${file.name}-${file.size}`} className="flex items-center gap-3 rounded-md border border-border-strong bg-surface-muted p-3">
              <FileSpreadsheet aria-hidden className="size-5 shrink-0 text-success" />
              <div className="min-w-0 flex-1">
                <p className="truncate text-body font-medium text-text">{file.name}</p>
                <p className="text-caption text-text-subtle">
                  {(file.size / 1024 / 1024).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} MB
                </p>
              </div>
              <button
                type="button"
                disabled={disabled}
                onClick={() => remove(file)}
                aria-label={`Remover ${file.name}`}
                className="rounded-md p-2 text-text-muted hover:bg-surface hover:text-text disabled:opacity-50"
              >
                <X aria-hidden className="size-4" />
              </button>
            </li>
          ))}
        </ul>
      ) : (
        <label
          htmlFor={inputId}
          onDragOver={(e) => {
            e.preventDefault()
            setDragging(true)
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          className={cn(
            'flex cursor-pointer flex-col items-center gap-2 rounded-md border-2 border-dashed p-6 text-center transition-colors',
            'has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-focus',
            dragging ? 'border-primary bg-primary-soft' : 'border-border-strong hover:border-primary hover:bg-surface-muted',
            disabled && 'pointer-events-none opacity-50',
          )}
        >
          <UploadCloud aria-hidden className="size-8 text-primary" />
          <span className="text-body text-text">
            <span className="font-medium text-primary">Clique para escolher</span> ou arraste {multiple ? 'os arquivos' : 'o arquivo'} aqui
          </span>
          {hint && <span className="text-caption text-text-subtle">{hint}</span>}
        </label>
      )}
      <input
        ref={inputRef}
        id={inputId}
        type="file"
        accept={accept}
        multiple={multiple}
        disabled={disabled}
        className="sr-only"
        onChange={(e) => select(e.target.files)}
      />
    </div>
  )
}
