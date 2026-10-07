using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SmartStock.Application.Assistant;

namespace SmartStock.Api.Controllers;

/// <summary>Chat de perguntas guiadas (decisão 41): consulta para todos os perfis, nada é alterado.</summary>
[ApiController]
[Route("api/assistant")]
public sealed class AssistantController(IAssistantService assistant) : ControllerBase
{
    [HttpPost]
    public Task<AssistantAnswer> Ask(QuestionBody body, CancellationToken cancellationToken) =>
        assistant.AskAsync(body.Question, cancellationToken);
}

public sealed record QuestionBody([Required(ErrorMessage = "Escreva uma pergunta."), MaxLength(300)] string Question);
