import { Eye, EyeOff } from 'lucide-react'
import { useId, useState, type InputHTMLAttributes, type ReactNode, type Ref, type SelectHTMLAttributes } from 'react'
import { cn } from '@/lib/cn'

type FieldShellProps = {
  id: string
  label: string
  hint?: string
  error?: string
  children: ReactNode
}

function FieldShell({ id, label, hint, error, children }: FieldShellProps) {
  return (
    <div className="flex flex-col gap-2">
      <label htmlFor={id} className="text-label text-text">
        {label}
      </label>
      {children}
      {error ? (
        <p id={`${id}-error`} role="alert" className="text-caption text-error">
          {error}
        </p>
      ) : hint ? (
        <p id={`${id}-hint`} className="text-caption text-text-subtle">
          {hint}
        </p>
      ) : null}
    </div>
  )
}

const controlClass = (invalid: boolean) =>
  cn(
    'h-10 w-full rounded-md border bg-surface px-3 text-body text-text placeholder:text-text-subtle',
    'transition-colors focus:border-primary focus:outline-none focus-visible:outline-2 focus-visible:outline-offset-0',
    'disabled:cursor-not-allowed disabled:bg-surface-muted disabled:text-text-subtle',
    invalid ? 'border-error' : 'border-border-strong',
  )

function describedBy(id: string, error?: string, hint?: string) {
  if (error) return `${id}-error`
  if (hint) return `${id}-hint`
  return undefined
}

type TextFieldProps = InputHTMLAttributes<HTMLInputElement> & {
  label: string
  hint?: string
  error?: string
  ref?: Ref<HTMLInputElement>
}

export function TextField({ label, hint, error, id, className, ref, ...props }: TextFieldProps) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  return (
    <FieldShell id={fieldId} label={label} hint={hint} error={error}>
      <input
        ref={ref}
        id={fieldId}
        aria-invalid={!!error || undefined}
        aria-describedby={describedBy(fieldId, error, hint)}
        className={cn(controlClass(!!error), className)}
        {...props}
      />
    </FieldShell>
  )
}

export function PasswordField({ label, hint, error, id, className, ref, ...props }: TextFieldProps) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  const [visible, setVisible] = useState(false)
  return (
    <FieldShell id={fieldId} label={label} hint={hint} error={error}>
      <div className="relative">
        <input
          ref={ref}
          id={fieldId}
          type={visible ? 'text' : 'password'}
          aria-invalid={!!error || undefined}
          aria-describedby={describedBy(fieldId, error, hint)}
          className={cn(controlClass(!!error), 'pr-10', className)}
          {...props}
        />
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? 'Ocultar senha' : 'Mostrar senha'}
          className="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-text-muted hover:text-text"
        >
          {visible ? <EyeOff className="size-4" aria-hidden /> : <Eye className="size-4" aria-hidden />}
        </button>
      </div>
    </FieldShell>
  )
}

type SelectFieldProps = SelectHTMLAttributes<HTMLSelectElement> & {
  label: string
  hint?: string
  error?: string
  options: readonly { value: string; label: string }[]
  ref?: Ref<HTMLSelectElement>
}

export function SelectField({ label, hint, error, id, options, className, ref, ...props }: SelectFieldProps) {
  const generatedId = useId()
  const fieldId = id ?? generatedId
  return (
    <FieldShell id={fieldId} label={label} hint={hint} error={error}>
      <select
        ref={ref}
        id={fieldId}
        aria-invalid={!!error || undefined}
        aria-describedby={describedBy(fieldId, error, hint)}
        className={cn(controlClass(!!error), className)}
        {...props}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldShell>
  )
}
