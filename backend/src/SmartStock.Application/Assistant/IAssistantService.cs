namespace SmartStock.Application.Assistant;

/// <summary>Chat de perguntas guiadas, sem IA (decisão 41): só consulta, nada é alterado.</summary>
public interface IAssistantService
{
    Task<AssistantAnswer> AskAsync(string question, CancellationToken cancellationToken = default);
}

/// <param name="Understood">O que o assistente reconheceu na pergunta (assunto, loja, marca, produto), para o usuário conferir.</param>
/// <param name="Suggestions">Perguntas para continuar a conversa.</param>
public sealed record AssistantAnswer(
    string Text,
    string? Understood,
    AnswerTable? Table,
    IReadOnlyList<AnswerLink> Links,
    IReadOnlyList<string> Suggestions);

public sealed record AnswerTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record AnswerLink(string Label, string Href);
