import { useMutation } from '@tanstack/react-query'
import { MessageSquare, Send } from 'lucide-react'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { PageHeader } from '@/components/layout/AppShell'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { TextField } from '@/components/ui/Field'
import { Table, Td, Th } from '@/components/ui/Table'
import { ApiError, apiRequest } from '@/lib/api'
import { cn } from '@/lib/cn'

type AnswerTable = { columns: string[]; rows: string[][] }
type AssistantAnswer = { text: string; understood: string | null; table: AnswerTable | null; links: { label: string; href: string }[]; suggestions: string[] }

type Message = { id: number; from: 'user'; text: string } | { id: number; from: 'assistant'; answer: AssistantAnswer } | { id: number; from: 'error'; text: string }

const examples = [
  'Resumo da rede',
  'Rupturas no Buriti',
  'O que transferir para Taubaté?',
  'Negativos da Matriz',
  'Onde tem estalo de salão?',
  'Produtos parados em Pindamonhangaba',
  'O que comprar da Mattel?',
  'Mais vendidos da rede',
]

/** Chat de perguntas guiadas (decisão 41): respostas com os dados da última análise, sem IA. Só consulta. */
export function AssistantPage() {
  const [messages, setMessages] = useState<Message[]>([])
  const [question, setQuestion] = useState('')
  const nextId = useRef(1)
  const endRef = useRef<HTMLDivElement>(null)
  const ask = useMutation({ mutationFn: (text: string) => apiRequest<AssistantAnswer>('/assistant', { method: 'POST', body: { question: text } }) })

  // Chaves no corpo: o efeito não pode devolver valor. Navegadores novos fazem scrollIntoView devolver uma Promise,
  // que o React tentaria chamar como função de limpeza ("is not a function").
  useEffect(() => {
    endRef.current?.scrollIntoView?.({ behavior: 'smooth', block: 'end' })
  }, [messages, ask.isPending])

  const send = async (text: string) => {
    const trimmed = text.trim()
    if (!trimmed || ask.isPending) return
    setQuestion('')
    setMessages((m) => [...m, { id: nextId.current++, from: 'user', text: trimmed }])
    try {
      const answer = await ask.mutateAsync(trimmed)
      setMessages((m) => [...m, { id: nextId.current++, from: 'assistant', answer }])
    } catch (e) {
      const text = e instanceof ApiError ? e.message : 'Não foi possível consultar agora. Verifique a conexão e tente de novo.'
      setMessages((m) => [...m, { id: nextId.current++, from: 'error', text }])
    }
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    void send(question)
  }

  return (
    <>
      <PageHeader
        title="Assistente"
        description="Pergunte sobre o estoque em português. As respostas usam os dados da última análise; o assistente só consulta, não altera nada."
      />
      <Card className="flex min-h-[32rem] flex-col">
        <div className="flex flex-1 flex-col gap-4 overflow-y-auto p-4 tablet:p-6" role="log" aria-live="polite" aria-label="Conversa com o assistente">
          {messages.length === 0 && <Welcome onAsk={(text) => void send(text)} />}
          {messages.map((message) =>
            message.from === 'user' ? (
              <div key={message.id} className="self-end rounded-lg bg-primary px-4 py-2 text-body text-primary-fg">
                {message.text}
              </div>
            ) : message.from === 'error' ? (
              <div key={message.id} className="self-start rounded-lg border border-error bg-error-soft px-4 py-2 text-body text-text">
                {message.text}
              </div>
            ) : (
              <AnswerView key={message.id} answer={message.answer} onAsk={(text) => void send(text)} />
            ),
          )}
          {ask.isPending && <div className="self-start rounded-lg bg-surface-muted px-4 py-2 text-body text-text-muted">Consultando…</div>}
          <div ref={endRef} />
        </div>
        <form onSubmit={submit} className="flex items-end gap-2 border-t border-border p-4 tablet:px-6">
          <div className="flex-1">
            <TextField
              label="Sua pergunta"
              value={question}
              maxLength={300}
              autoComplete="off"
              placeholder='Ex.: "O que transferir para o Buriti?"'
              onChange={(e) => setQuestion(e.target.value)}
            />
          </div>
          <Button type="submit" icon={<Send aria-hidden className="size-4" />} loading={ask.isPending} disabled={!question.trim()}>
            Perguntar
          </Button>
        </form>
      </Card>
    </>
  )
}

function Welcome({ onAsk }: { onAsk: (text: string) => void }) {
  return (
    <div className="flex flex-col items-center gap-4 py-8 text-center">
      <MessageSquare aria-hidden className="size-10 text-primary" />
      <div className="flex max-w-xl flex-col gap-1">
        <p className="text-subtitle text-text">Pergunte sobre o estoque da rede</p>
        <p className="text-body text-text-muted">
          Rupturas, negativos, transferências, compras, parados, excesso, onde tem um produto, mais vendidos ou o resumo de uma loja.
          Cite a loja pelo nome, código ou apelido ("Buriti", "09", "SJC").
        </p>
      </div>
      <Suggestions items={examples} onAsk={onAsk} />
    </div>
  )
}

function AnswerView({ answer, onAsk }: { answer: AssistantAnswer; onAsk: (text: string) => void }) {
  return (
    <div className="flex max-w-full flex-col gap-3 self-start rounded-lg border border-border bg-surface-muted p-4 notebook:max-w-[90%]">
      {answer.understood && <p className="text-caption text-text-subtle">{answer.understood}</p>}
      <p className="text-body text-text">{answer.text}</p>
      {answer.table && answer.table.rows.length > 0 && (
        <div className="rounded-md border border-border bg-surface">
          <Table>
            <caption className="sr-only">{answer.text}</caption>
            <thead>
              <tr>
                {answer.table.columns.map((c, i) => (
                  <Th key={c} className={cn(i > 0 && 'text-right')}>
                    {c}
                  </Th>
                ))}
              </tr>
            </thead>
            <tbody>
              {answer.table.rows.map((row, r) => (
                <tr key={r}>
                  {row.map((value, i) => (
                    <Td key={i} className={cn(i > 0 ? 'whitespace-nowrap text-right tabular-nums' : 'min-w-56')}>
                      {value}
                    </Td>
                  ))}
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      )}
      {answer.links.length > 0 && (
        <div className="flex flex-wrap gap-3">
          {answer.links.map((link) => (
            <Link key={link.href} to={link.href} className="text-body font-medium text-primary hover:underline">
              Abrir {link.label} →
            </Link>
          ))}
        </div>
      )}
      <Suggestions items={answer.suggestions} onAsk={onAsk} />
    </div>
  )
}

function Suggestions({ items, onAsk }: { items: string[]; onAsk: (text: string) => void }) {
  if (items.length === 0) return null
  return (
    <div className="flex flex-wrap justify-center gap-2 notebook:justify-start">
      {items.map((item) => (
        <Button key={item} variant="secondary" size="sm" onClick={() => onAsk(item)}>
          {item}
        </Button>
      ))}
    </div>
  )
}
