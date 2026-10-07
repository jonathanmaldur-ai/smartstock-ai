using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Common;
using SmartStock.Application.Reports;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Reports;

/// <summary>
/// Relatórios da Fase 4 (decisão 42), a partir da análise mais recente: listas por situação, agregados por
/// loja/marca/categoria (cobertura e giro) e transferências. Só leitura.
/// </summary>
internal sealed class ReportService(SmartStockDbContext db) : IReportService
{
    private delegate Task<(IReadOnlyList<ReportColumn> Columns, List<object?[]> Rows, int Total)> Builder(
        ReportContext context, int? maxRows, CancellationToken cancellationToken);

    private sealed record Definition(ReportInfo Info, Builder Build);

    private sealed record ReportContext(StockAnalysis Analysis, ReportFilter Filter);

    private IReadOnlyList<Definition>? _definitions;

    /// <summary>Os relatórios da central (inicializados na primeira chamada: os construtores usam métodos da instância).</summary>
    private IReadOnlyList<Definition> Definitions => _definitions ??=
    [
        new(new("ruptura", "Ruptura", "Produtos que venderam em 12 meses e estão sem estoque na loja.", "Situação do estoque"),
            (c, m, ct) => PositionsAsync(c, m, p => p.Situation == StockSituation.Rupture, ct)),
        new(new("criticos", "Produtos críticos", "Ruptura ou cobertura abaixo de 7 dias em produto que vende pelo menos a venda mínima.", "Situação do estoque"),
            (c, m, ct) => PositionsAsync(c, m, p => p.Priority == AlertPriority.Critical || p.Priority == AlertPriority.High, ct)),
        new(new("excesso", "Excesso", "Estoque para mais de 120 dias no ritmo de venda da loja.", "Situação do estoque"),
            (c, m, ct) => PositionsAsync(c, m, p => p.Situation == StockSituation.Excess, ct)),
        new(new("parado", "Estoque parado", "Estoque positivo sem nenhuma venda nos 12 meses.", "Situação do estoque"),
            (c, m, ct) => PositionsAsync(c, m, p => p.Situation == StockSituation.Stagnant, ct)),
        new(new("por-loja", "Cobertura e giro por loja", "Estoque, vendas, cobertura, giro e situação de cada loja.", "Cobertura e giro"),
            (c, m, ct) => AggregateAsync(c, m, Grouping.Store, ct)),
        new(new("por-marca", "Cobertura e giro por marca", "Estoque, vendas, cobertura, giro e situação de cada marca.", "Cobertura e giro"),
            (c, m, ct) => AggregateAsync(c, m, Grouping.Brand, ct)),
        new(new("por-categoria", "Cobertura e giro por categoria", "Estoque, vendas, cobertura, giro e situação de cada categoria.", "Cobertura e giro"),
            (c, m, ct) => AggregateAsync(c, m, Grouping.Category, ct)),
        new(new("transferencias-sugeridas", "Transferências sugeridas", "Sugestões pendentes de aprovação, com origem, destino e quantidade.", "Transferências"),
            (c, m, ct) => SuggestionsAsync(c, m, [SuggestionStatus.Suggested], ct)),
        new(new("transferencias-decisoes", "Transferências aprovadas, rejeitadas e realizadas",
                "Histórico de decisões: quem decidiu, quando e se a transferência apareceu no arquivo do ERP.", "Transferências"),
            (c, m, ct) => SuggestionsAsync(c, m, [SuggestionStatus.Approved, SuggestionStatus.Rejected, SuggestionStatus.Completed], ct)),
    ];

    public async Task<ReportCatalog> CatalogAsync(CancellationToken cancellationToken = default)
    {
        var brands = await db.Brands.AsNoTracking().OrderBy(b => b.Name).Select(b => new ReportOption(b.Id, b.Name)).ToListAsync(cancellationToken);
        return new ReportCatalog(Definitions.Select(d => d.Info).ToList(), brands);
    }

    public async Task<Result<ReportData>> BuildAsync(string key, ReportFilter filter, int? maxRows, CancellationToken cancellationToken = default)
    {
        var definition = Definitions.FirstOrDefault(d => d.Info.Key == key);
        if (definition is null)
            return Error.NotFound("report.not_found", "Relatório não encontrado.");

        var analysis = await db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (analysis is null)
            return Error.Conflict("report.no_analysis", "Gere a análise do estoque antes de emitir relatórios.");

        var (columns, rows, total) = await definition.Build(new ReportContext(analysis, filter), maxRows, cancellationToken);
        return new ReportData(
            definition.Info.Key, definition.Info.Title, definition.Info.Description, await FilterSummaryAsync(filter, cancellationToken),
            analysis.AnalysisDate, analysis.StockDate, columns, rows, total);
    }

    // ---- Listas por situação -------------------------------------------------------------------------------------

    private static readonly ReportColumn[] PositionColumns =
    [
        new("Loja", ReportColumnKind.Text), new("Código", ReportColumnKind.Text), new("Produto", ReportColumnKind.Text),
        new("Referência", ReportColumnKind.Text), new("Marca", ReportColumnKind.Text), new("Categoria", ReportColumnKind.Text),
        new("Estoque", ReportColumnKind.Decimal), new("Venda 12 meses", ReportColumnKind.Decimal), new("VMD", ReportColumnKind.Decimal),
        new("Cobertura (dias)", ReportColumnKind.Decimal), new("Prioridade", ReportColumnKind.Text)
    ];

    private async Task<(IReadOnlyList<ReportColumn>, List<object?[]>, int)> PositionsAsync(
        ReportContext c, int? maxRows, System.Linq.Expressions.Expression<Func<StockPosition, bool>> predicate, CancellationToken cancellationToken)
    {
        var query =
            from p in db.StockPositions.AsNoTracking().Where(predicate)
            join product in db.Products on p.ProductId equals product.Id
            join store in db.Stores on p.StoreId equals store.Id
            where p.AnalysisId == c.Analysis.Id
                  && (c.Filter.StoreId == null || p.StoreId == c.Filter.StoreId)
                  && (c.Filter.BrandId == null || product.BrandId == c.Filter.BrandId)
                  && (c.Filter.CategoryId == null || product.CategoryId == c.Filter.CategoryId)
            orderby p.Priority descending, p.Sold12Months descending, product.Description
            select new
            {
                Store = store.Code + " " + store.Name, product.Code, product.Description, product.Reference,
                Brand = product.Brand.Name, Category = product.Category.Name,
                p.Stock, p.Sold12Months, p.DailyAverage, p.CoverageDays, p.Priority
            };

        var total = await query.CountAsync(cancellationToken);
        var rows = await (maxRows is null ? query : query.Take(maxRows.Value)).ToListAsync(cancellationToken);
        return (PositionColumns, rows.Select(r => new object?[]
        {
            r.Store, r.Code, r.Description, r.Reference, r.Brand, r.Category, r.Stock, r.Sold12Months,
            Math.Round(r.DailyAverage, 2), r.CoverageDays is null ? null : Math.Round(r.CoverageDays.Value, 0), PriorityLabel(r.Priority)
        }).ToList(), total);
    }

    // ---- Agregados: cobertura e giro -----------------------------------------------------------------------------

    private enum Grouping { Store, Brand, Category }

    private static readonly ReportColumn[] AggregateColumnsTail =
    [
        new("Itens", ReportColumnKind.Integer), new("Estoque (un.)", ReportColumnKind.Decimal), new("Venda 12 meses (un.)", ReportColumnKind.Decimal),
        new("Cobertura (dias)", ReportColumnKind.Decimal), new("Giro anual", ReportColumnKind.Decimal),
        new("Ruptura", ReportColumnKind.Integer), new("Ruptura que importa", ReportColumnKind.Integer), new("Abaixo do mínimo", ReportColumnKind.Integer),
        new("Excesso", ReportColumnKind.Integer), new("Parado", ReportColumnKind.Integer), new("Negativos", ReportColumnKind.Integer)
    ];

    private async Task<(IReadOnlyList<ReportColumn>, List<object?[]>, int)> AggregateAsync(
        ReportContext c, int? maxRows, Grouping grouping, CancellationToken cancellationToken)
    {
        var positions =
            from p in db.StockPositions.AsNoTracking()
            join product in db.Products on p.ProductId equals product.Id
            join store in db.Stores on p.StoreId equals store.Id
            where p.AnalysisId == c.Analysis.Id
                  && (c.Filter.StoreId == null || p.StoreId == c.Filter.StoreId)
                  && (c.Filter.BrandId == null || product.BrandId == c.Filter.BrandId)
                  && (c.Filter.CategoryId == null || product.CategoryId == c.Filter.CategoryId)
            select new
            {
                Key = grouping == Grouping.Store ? store.Code + " " + store.Name
                    : grouping == Grouping.Brand ? product.Brand.Name
                    : product.Category.Name,
                p
            };

        var groups = await positions
            .GroupBy(x => x.Key)
            .Select(g => new
            {
                g.Key,
                Items = g.Count(),
                Stock = g.Where(x => x.p.Stock > 0).Sum(x => x.p.Stock),
                Sold = g.Sum(x => x.p.Sold12Months),
                Rupture = g.Count(x => x.p.Situation == StockSituation.Rupture),
                Relevant = g.Count(x => x.p.Situation == StockSituation.Rupture && x.p.Priority == AlertPriority.Critical),
                Below = g.Count(x => x.p.Situation == StockSituation.BelowMinimum || x.p.Situation == StockSituation.CriticalCoverage),
                Excess = g.Count(x => x.p.Situation == StockSituation.Excess),
                Stagnant = g.Count(x => x.p.Situation == StockSituation.Stagnant),
                Negatives = g.Count(x => x.p.Stock < 0)
            })
            .ToListAsync(cancellationToken);

        var ordered = groups.OrderByDescending(g => grouping == Grouping.Store ? 0 : g.Sold).ThenBy(g => g.Key).ToList();
        var rows = (maxRows is null ? ordered : ordered.Take(maxRows.Value))
            .Select(g =>
            {
                var coverage = StockMetrics.CoverageDays(g.Stock, StockMetrics.DailyAverage(g.Sold));
                return new object?[]
                {
                    g.Key, g.Items, g.Stock, g.Sold, coverage is null ? null : Math.Round(coverage.Value, 0),
                    g.Stock > 0 ? Math.Round(g.Sold / g.Stock, 2) : null,
                    g.Rupture, g.Relevant, g.Below, g.Excess, g.Stagnant, g.Negatives
                };
            })
            .ToList();

        var first = new ReportColumn(grouping switch { Grouping.Store => "Loja", Grouping.Brand => "Marca", _ => "Categoria" }, ReportColumnKind.Text);
        return ([first, .. AggregateColumnsTail], rows, ordered.Count);
    }

    // ---- Transferências ------------------------------------------------------------------------------------------

    private static readonly ReportColumn[] SuggestionColumns =
    [
        new("Status", ReportColumnKind.Text), new("Prioridade", ReportColumnKind.Text), new("Origem", ReportColumnKind.Text),
        new("Destino", ReportColumnKind.Text), new("Código", ReportColumnKind.Text), new("Produto", ReportColumnKind.Text),
        new("Marca", ReportColumnKind.Text), new("Quantidade", ReportColumnKind.Decimal), new("Análise de", ReportColumnKind.Date),
        new("Decidido por", ReportColumnKind.Text), new("Decidido em", ReportColumnKind.Date), new("Observação", ReportColumnKind.Text),
        new("Realizada em", ReportColumnKind.Date), new("Qtd. realizada", ReportColumnKind.Decimal), new("Dias até realizar", ReportColumnKind.Integer)
    ];

    private async Task<(IReadOnlyList<ReportColumn>, List<object?[]>, int)> SuggestionsAsync(
        ReportContext c, int? maxRows, SuggestionStatus[] statuses, CancellationToken cancellationToken)
    {
        var query =
            from s in db.TransferSuggestions.AsNoTracking()
            join product in db.Products on s.ProductId equals product.Id
            join origin in db.Stores on s.OriginStoreId equals origin.Id
            join destination in db.Stores on s.DestinationStoreId equals destination.Id
            join analysis in db.StockAnalyses on s.AnalysisId equals analysis.Id
            where statuses.Contains(s.Status)
                  && (c.Filter.StoreId == null || s.OriginStoreId == c.Filter.StoreId || s.DestinationStoreId == c.Filter.StoreId)
                  && (c.Filter.BrandId == null || product.BrandId == c.Filter.BrandId)
                  && (c.Filter.CategoryId == null || product.CategoryId == c.Filter.CategoryId)
            orderby s.DecidedAt descending, s.Priority descending, s.Quantity descending
            select new
            {
                s.Status, s.Priority, Origin = origin.Code + " " + origin.Name, Destination = destination.Code + " " + destination.Name,
                product.Code, product.Description, Brand = product.Brand.Name, s.Quantity, analysis.AnalysisDate,
                s.DecidedByEmail, s.DecidedAt, s.DecisionNote, s.CompletedOn, s.CompletedQuantity
            };

        var total = await query.CountAsync(cancellationToken);
        var rows = await (maxRows is null ? query : query.Take(maxRows.Value)).ToListAsync(cancellationToken);
        return (SuggestionColumns, rows.Select(r =>
        {
            var decidedDay = r.DecidedAt is null ? (DateOnly?)null : DateOnly.FromDateTime(r.DecidedAt.Value.ToLocalTime().DateTime);
            return new object?[]
            {
                StatusLabel(r.Status), PriorityLabel(r.Priority), r.Origin, r.Destination, r.Code, r.Description, r.Brand, r.Quantity,
                r.AnalysisDate, r.DecidedByEmail, decidedDay, r.DecisionNote, r.CompletedOn, r.CompletedQuantity,
                r.CompletedOn is null || decidedDay is null ? null : r.CompletedOn.Value.DayNumber - decidedDay.Value.DayNumber
            };
        }).ToList(), total);
    }

    // ---- Apoio ---------------------------------------------------------------------------------------------------

    private async Task<string> FilterSummaryAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var parts = new List<string>();
        if (filter.StoreId is not null)
            parts.Add("Loja " + await db.Stores.Where(s => s.Id == filter.StoreId).Select(s => s.Code + " " + s.Name).FirstOrDefaultAsync(cancellationToken));
        if (filter.BrandId is not null)
            parts.Add("Marca " + await db.Brands.Where(b => b.Id == filter.BrandId).Select(b => b.Name).FirstOrDefaultAsync(cancellationToken));
        if (filter.CategoryId is not null)
            parts.Add("Categoria " + await db.Categories.Where(x => x.Id == filter.CategoryId).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken));
        return parts.Count == 0 ? "Rede toda" : string.Join(" · ", parts);
    }

    private static string PriorityLabel(AlertPriority priority) => priority switch
    {
        AlertPriority.Critical => "Crítica",
        AlertPriority.High => "Alta",
        AlertPriority.Medium => "Média",
        AlertPriority.Low => "Baixa",
        _ => "—"
    };

    private static string StatusLabel(SuggestionStatus status) => status switch
    {
        SuggestionStatus.Suggested => "Pendente",
        SuggestionStatus.Approved => "Aprovada",
        SuggestionStatus.Rejected => "Rejeitada",
        SuggestionStatus.Completed => "Realizada",
        SuggestionStatus.Superseded => "Substituída",
        _ => status.ToString()
    };
}
