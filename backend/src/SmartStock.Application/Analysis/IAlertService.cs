using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;

namespace SmartStock.Application.Analysis;

/// <summary>Central de alertas (Módulo 3.1, decisão 47): os alertas só avisam; o usuário decide o que fazer.</summary>
public interface IAlertService
{
    Task<AlertSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<AlertDto>> ListAsync(AlertQuery query, CancellationToken cancellationToken = default);

    /// <summary>Marca (ou desmarca) como visto. Devolve quantos foram alterados.</summary>
    Task<Result<int>> MarkSeenAsync(IReadOnlyList<long> ids, bool seen, CancellationToken cancellationToken = default);
}

/// <param name="PreviousStockDate">Foto usada na comparação; null quando ainda não havia análise anterior.</param>
public sealed record AlertSummary(Guid? AnalysisId, DateOnly? StockDate, DateOnly? PreviousStockDate, IReadOnlyList<AlertTypeCount> Types);

public sealed record AlertTypeCount(AlertType Type, string Label, AlertPriority Priority, int Total, int Unseen);

public sealed record AlertQuery(
    AlertType? Type, AlertPriority? Priority, int? StoreId, int? BrandId, string? Search, bool IncludeSeen = false, int Page = 1, int PageSize = 25);

public sealed record AlertDto(
    long Id,
    AlertType Type,
    string TypeLabel,
    AlertPriority Priority,
    int? ProductId,
    string? ProductCode,
    string? ProductDescription,
    string? StoreCode,
    string? StoreName,
    string? BrandName,
    string Message,
    DateTimeOffset? SeenAt,
    string? SeenByEmail);
