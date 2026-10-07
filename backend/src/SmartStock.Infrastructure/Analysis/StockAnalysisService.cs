using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NpgsqlTypes;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Analysis;
using SmartStock.Application.Common;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Catalog;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Inventory;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Analysis;

internal sealed class StockAnalysisService(
    SmartStockDbContext db,
    IAuditLogger audit,
    ICurrentUser currentUser,
    TimeProvider clock,
    NegativeStockCalculator negativeCalculator,
    CompletedTransferMatcher completedMatcher,
    AlertCalculator alertCalculator,
    AlertNotifier alertNotifier,
    ILogger<StockAnalysisService> logger) : IStockAnalysisService
{
    public async Task<Result<AnalysisSummary>> RunAsync(CancellationToken cancellationToken = default)
    {
        var stockBatch = await CurrentImports.StockBatchAsync(db, cancellationToken);
        if (stockBatch?.ReferenceDate is null)
            return Error.Conflict("analysis.no_stock", "Importe o estoque antes de gerar a análise.");

        // Antes do cálculo: aprovadas que já aparecem no arquivo de transferências deixam de contar como "a caminho".
        var completed = await completedMatcher.MarkCompletedAsync(cancellationToken);

        var parameters = await db.StockParameters.AsNoTracking().SingleAsync(cancellationToken);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var input = await LoadInputAsync(stockBatch.Id, cancellationToken);
        var result = Calculate(input, parameters, today.DayNumber - stockBatch.ReferenceDate.Value.DayNumber);
        var analysisId = Guid.NewGuid();
        var negatives = await negativeCalculator.CalculateAsync(
            analysisId,
            result.Positions.Select(p => new NegativeStockCalculator.StockEntry(
                p.ProductId, p.StoreId, p.Metrics.Situation == StockSituation.Warehouse, p.Stock, p.Sold)).ToList(),
            cancellationToken);
        var alerts = await alertCalculator.CalculateAsync(
            analysisId,
            stockBatch.ReferenceDate.Value,
            parameters,
            result.Positions.Select(p => new AlertCalculator.Entry(
                p.ProductId, p.StoreId, p.Metrics.Situation == StockSituation.Warehouse, p.Stock, p.Sold,
                p.Metrics.DailyAverage, p.Metrics.CoverageDays, p.Metrics.Situation)).ToList(),
            cancellationToken);

        var analysis = new StockAnalysis
        {
            Id = analysisId,
            StockImportId = stockBatch.Id,
            StockDate = stockBatch.ReferenceDate.Value,
            AnalysisDate = today,
            CreatedAt = clock.GetUtcNow(),
            CreatedByEmail = currentUser.Email ?? "desconhecido",
            Parameters = JsonSerializer.Serialize(ToDto(parameters)),
            PositionCount = result.Positions.Count,
            SuggestionCount = result.Transfers.Count,
            SuggestedUnits = result.Transfers.Sum(t => t.Plan.Quantity),
            PurchaseCount = result.Purchases.Count
        };

        var superseded = await SaveAsync(analysis, result, negatives, alerts, cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.AnalysisGenerated, AuditResult.Success, "StockAnalysis", analysis.Id.ToString(),
            Details: new
            {
                analysis.StockDate, analysis.AnalysisDate, analysis.PositionCount, analysis.SuggestionCount,
                analysis.SuggestedUnits, analysis.PurchaseCount, alerts = alerts.Count, supersededSuggestions = superseded, completedSuggestions = completed
            }),
            cancellationToken);
        logger.LogInformation("Análise {AnalysisId}: {Positions} posições, {Suggestions} sugestões.",
            analysis.Id, analysis.PositionCount, analysis.SuggestionCount);

        if (parameters.SendAlertEmail)
            await alertNotifier.NotifyAsync(analysis, alerts, cancellationToken);

        return await GetSummaryAsync(cancellationToken);
    }

    public async Task<AnalysisSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var latest = await db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var approved = await db.TransferSuggestions.AsNoTracking()
            .Where(s => s.Status == SuggestionStatus.Approved)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Units = g.Sum(s => s.Quantity) })
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
            return new AnalysisSummary(null, null, null, null, null,
                await db.ImportBatches.AnyAsync(b => b.Type == ImportType.Stock && b.Status == ImportStatus.Confirmed, cancellationToken),
                [], 0, new SuggestionTotals(0, 0, approved?.Count ?? 0, approved?.Units ?? 0, 0, 0), 0, 0);

        var situations = await db.StockPositions.AsNoTracking()
            .Where(p => p.AnalysisId == latest.Id)
            .GroupBy(p => p.Situation)
            .Select(g => new SituationCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
        var relevantRuptures = await db.StockPositions.AsNoTracking()
            .CountAsync(p => p.AnalysisId == latest.Id && p.Situation == StockSituation.Rupture && p.Priority == AlertPriority.Critical,
                cancellationToken);

        var current = await db.TransferSuggestions.AsNoTracking()
            .Where(s => s.AnalysisId == latest.Id)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Units = g.Sum(s => s.Quantity) })
            .ToListAsync(cancellationToken);
        var pending = current.SingleOrDefault(c => c.Status == SuggestionStatus.Suggested);
        var rejected = current.SingleOrDefault(c => c.Status == SuggestionStatus.Rejected);
        var completedCount = await db.TransferSuggestions.CountAsync(s => s.Status == SuggestionStatus.Completed, cancellationToken);

        var purchases = await db.PurchaseSuggestions.AsNoTracking()
            .Where(p => p.AnalysisId == latest.Id)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Units = g.Sum(p => p.Quantity) })
            .FirstOrDefaultAsync(cancellationToken);

        return new AnalysisSummary(
            latest.Id, latest.StockDate, latest.AnalysisDate, latest.CreatedAt, latest.CreatedByEmail,
            await IsOutdatedAsync(latest, cancellationToken),
            situations,
            relevantRuptures,
            new SuggestionTotals(pending?.Count ?? 0, pending?.Units ?? 0, approved?.Count ?? 0, approved?.Units ?? 0, rejected?.Count ?? 0, completedCount),
            purchases?.Count ?? 0, purchases?.Units ?? 0);
    }

    public async Task<StockParametersDto> GetParametersAsync(CancellationToken cancellationToken = default) =>
        ToDto(await db.StockParameters.AsNoTracking().SingleAsync(cancellationToken));

    public async Task<Result<StockParametersDto>> UpdateParametersAsync(StockParametersInput input, CancellationToken cancellationToken = default)
    {
        var parameters = await db.StockParameters.SingleAsync(cancellationToken);
        var before = ToDto(parameters);
        parameters.CriticalCoverageDays = input.CriticalCoverageDays;
        parameters.MinimumDays = input.MinimumDays;
        parameters.IdealDays = input.IdealDays;
        parameters.MaximumDays = input.MaximumDays;
        parameters.ExcessDays = input.ExcessDays;
        parameters.MinimumAnnualSales = input.MinimumAnnualSales;
        parameters.SendAlertEmail = input.SendAlertEmail;

        var problem = parameters.Validate();
        if (problem is not null)
            return Error.Validation("analysis.invalid_parameters", problem);

        parameters.UpdatedAt = clock.GetUtcNow();
        parameters.UpdatedByEmail = currentUser.Email;
        await db.SaveChangesAsync(cancellationToken);

        var after = ToDto(parameters);
        await audit.LogAsync(new AuditEntry(
            AuditActions.AnalysisParametersUpdated, AuditResult.Success, "StockParameters", parameters.Id.ToString(),
            Details: new { before, after }), cancellationToken);
        return after;
    }

    public async Task<Result<int>> DecideAsync(SuggestionDecision decision, CancellationToken cancellationToken = default)
    {
        var ids = decision.Ids.Distinct().ToList();
        if (ids.Count == 0 && decision.Route is null)
            return Error.Validation("suggestion.none_selected", "Selecione pelo menos uma sugestão.");

        var pending = db.TransferSuggestions.Where(s => s.Status == SuggestionStatus.Suggested);
        if (ids.Count > 0)
            pending = pending.Where(s => ids.Contains(s.Id));
        else
        {
            // Decisão 36: a rota inteira (a lista de separação de uma carga).
            var route = decision.Route!;
            pending = pending.Where(s => s.OriginStoreId == route.OriginStoreId && s.DestinationStoreId == route.DestinationStoreId);
            if (route.HideNegativeDestination)
                pending = pending.Where(s => !s.DestinationNegative);
        }
        var suggestions = await pending.ToListAsync(cancellationToken);
        if (suggestions.Count == 0)
            return Error.Conflict("suggestion.not_pending", "Nenhuma das sugestões selecionadas está pendente.");

        var note = string.IsNullOrWhiteSpace(decision.Note) ? null : decision.Note.Trim()[..Math.Min(decision.Note.Trim().Length, 500)];
        var now = clock.GetUtcNow();
        foreach (var suggestion in suggestions)
        {
            suggestion.Status = decision.Approve ? SuggestionStatus.Approved : SuggestionStatus.Rejected;
            suggestion.DecidedAt = now;
            suggestion.DecidedByEmail = currentUser.Email;
            suggestion.DecisionNote = note;
        }
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            decision.Approve ? AuditActions.SuggestionsApproved : AuditActions.SuggestionsRejected,
            AuditResult.Success, "TransferSuggestion", suggestions.Count == 1 ? suggestions[0].Id.ToString() : null,
            Details: new { count = suggestions.Count, units = suggestions.Sum(s => s.Quantity), note, ids = suggestions.Select(s => s.Id) }),
            cancellationToken);
        return suggestions.Count;
    }

    private async Task<AnalysisInput> LoadInputAsync(Guid stockImportId, CancellationToken cancellationToken)
    {
        var stores = await db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.Active).ToDictionaryAsync(s => s.Id, cancellationToken);
        var analysedProducts = (await db.Products.AsNoTracking()
            .Where(p => !p.Category.ExcludedFromAnalysis)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        var seasonal = (await db.Products.AsNoTracking()
            .Where(p => p.IsSeasonal)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var salesBatchIds = (await CurrentImports.SalesBatchesAsync(db, cancellationToken)).Values.Select(b => b.Id).ToList();
        var stock = await db.StockLevels.AsNoTracking()
            .Where(s => s.ImportId == stockImportId)
            .Select(s => new { s.ProductId, s.StoreId, s.Quantity })
            .ToListAsync(cancellationToken);
        var sales = await db.SalesTotals.AsNoTracking()
            .Where(s => salesBatchIds.Contains(s.ImportId))
            .Select(s => new { s.ProductId, s.StoreId, s.Quantity })
            .ToListAsync(cancellationToken);
        var inTransit = await db.TransferSuggestions.AsNoTracking()
            .Where(s => s.Status == SuggestionStatus.Approved)
            .Select(s => new { s.ProductId, s.OriginStoreId, s.DestinationStoreId, s.Quantity })
            .ToListAsync(cancellationToken);

        var products = new Dictionary<int, Dictionary<int, (decimal Stock, decimal Sold)>>();
        void Add(int productId, int storeId, decimal stockQuantity, decimal sold)
        {
            if (!analysedProducts.Contains(productId) || !stores.ContainsKey(storeId))
                return;
            var byStore = products.TryGetValue(productId, out var existing) ? existing : products[productId] = [];
            var current = byStore.GetValueOrDefault(storeId);
            byStore[storeId] = (current.Stock + stockQuantity, current.Sold + sold);
        }
        foreach (var s in stock) Add(s.ProductId, s.StoreId, s.Quantity, 0);
        foreach (var s in sales) Add(s.ProductId, s.StoreId, 0, s.Quantity);

        return new AnalysisInput(
            stores,
            products,
            seasonal,
            inTransit.GroupBy(t => t.ProductId).ToDictionary(
                g => g.Key, g => (IReadOnlyList<InTransit>)g.Select(t => new InTransit(t.OriginStoreId, t.DestinationStoreId, t.Quantity)).ToList()));
    }

    private static AnalysisResult Calculate(AnalysisInput input, StockParameters parameters, int daysSinceStock)
    {
        var rules = new StockRules(parameters);
        var planner = new TransferPlanner(rules);
        var result = new AnalysisResult([], [], []);

        foreach (var (productId, byStore) in input.Products)
        {
            var stores = byStore
                .Select(e =>
                {
                    var store = input.Stores[e.Key];
                    return new StoreStock(store.Id, $"{store.Code} {store.Name}", store.Type == StoreType.Warehouse, e.Value.Stock, e.Value.Sold);
                })
                .ToList();

            foreach (var s in stores)
                result.Positions.Add(new PositionRow(productId, s.StoreId, s.Stock, s.Sold12Months,
                    rules.Evaluate(s.IsWarehouse, s.Stock, s.Sold12Months, daysSinceStock)));

            // Decisão 37: produto sazonal continua na situação do estoque, mas sem sugestões.
            if (input.Seasonal.Contains(productId))
                continue;

            var plan = planner.Plan(stores, input.InTransit.GetValueOrDefault(productId) ?? [], daysSinceStock);
            result.Transfers.AddRange(plan.Transfers.Select(t => (productId, t)));
            result.Purchases.AddRange(plan.Purchases.Select(p => (productId, p)));
        }
        return result;
    }

    /// <returns>Quantas sugestões pendentes de análises anteriores foram substituídas.</returns>
    private async Task<int> SaveAsync(
        StockAnalysis analysis, AnalysisResult result, List<NegativeStock> negatives, List<StockAlert> alerts, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var superseded = await db.TransferSuggestions
            .Where(s => s.Status == SuggestionStatus.Suggested)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SuggestionStatus.Superseded), cancellationToken);

        db.StockAnalyses.Add(analysis);
        await db.SaveChangesAsync(cancellationToken);

        await CopyPositionsAsync(analysis.Id, result.Positions, cancellationToken);
        await CopyTransfersAsync(analysis.Id, result.Transfers, cancellationToken);
        await CopyPurchasesAsync(analysis.Id, result.Purchases, cancellationToken);
        await CopyNegativesAsync(negatives, cancellationToken);
        await CopyAlertsAsync(alerts, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return superseded;
    }

    private Task CopyPositionsAsync(Guid analysisId, List<PositionRow> positions, CancellationToken cancellationToken) =>
        PostgresCopy.WriteAsync(db,
            "COPY stock_positions (analysis_id, product_id, store_id, stock, projected_stock, sold12months, daily_average, " +
            "coverage_days, situation, priority) FROM STDIN (FORMAT BINARY)",
            positions,
            async (w, p, ct) =>
            {
                await w.WriteAsync(analysisId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(p.ProductId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(p.StoreId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(p.Stock, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(p.Metrics.ProjectedStock, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(p.Sold, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(p.Metrics.DailyAverage, NpgsqlDbType.Numeric, ct);
                await WriteNullableAsync(w, p.Metrics.CoverageDays, ct);
                await w.WriteAsync(p.Metrics.Situation.ToString(), NpgsqlDbType.Varchar, ct);
                await w.WriteAsync((int)p.Metrics.Priority, NpgsqlDbType.Integer, ct);
            },
            cancellationToken);

    private Task CopyTransfersAsync(Guid analysisId, List<(int ProductId, PlannedTransfer Plan)> transfers, CancellationToken cancellationToken) =>
        PostgresCopy.WriteAsync(db,
            "COPY transfer_suggestions (analysis_id, product_id, origin_store_id, destination_store_id, quantity, priority, status, reason, " +
            "origin_stock, origin_daily_average, origin_coverage_days, destination_stock, destination_daily_average, " +
            "destination_coverage_days, destination_coverage_after, destination_negative) FROM STDIN (FORMAT BINARY)",
            transfers,
            async (w, t, ct) =>
            {
                var (productId, plan) = t;
                await w.WriteAsync(analysisId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(productId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(plan.OriginStoreId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(plan.DestinationStoreId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(plan.Quantity, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync((int)plan.Priority, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(SuggestionStatus.Suggested.ToString(), NpgsqlDbType.Varchar, ct);
                await w.WriteAsync(plan.Reason.Length > 1000 ? plan.Reason[..1000] : plan.Reason, NpgsqlDbType.Varchar, ct);
                await w.WriteAsync(plan.OriginStock, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(plan.Origin.DailyAverage, NpgsqlDbType.Numeric, ct);
                await WriteNullableAsync(w, plan.Origin.CoverageDays, ct);
                await w.WriteAsync(plan.DestinationStock, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(plan.Destination.DailyAverage, NpgsqlDbType.Numeric, ct);
                await WriteNullableAsync(w, plan.Destination.CoverageDays, ct);
                await WriteNullableAsync(w, plan.DestinationCoverageAfter, ct);
                await w.WriteAsync(plan.DestinationNegative, NpgsqlDbType.Boolean, ct);
            },
            cancellationToken);

    private Task CopyPurchasesAsync(Guid analysisId, List<(int ProductId, PlannedPurchase Plan)> purchases, CancellationToken cancellationToken) =>
        PostgresCopy.WriteAsync(db,
            "COPY purchase_suggestions (analysis_id, product_id, store_id, quantity, stock, daily_average, coverage_days) FROM STDIN (FORMAT BINARY)",
            purchases,
            async (w, p, ct) =>
            {
                var (productId, plan) = p;
                await w.WriteAsync(analysisId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(productId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(plan.StoreId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(plan.Quantity, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(plan.Stock, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(plan.Destination.DailyAverage, NpgsqlDbType.Numeric, ct);
                await WriteNullableAsync(w, plan.Destination.CoverageDays, ct);
            },
            cancellationToken);

    private Task CopyNegativesAsync(List<NegativeStock> negatives, CancellationToken cancellationToken) =>
        PostgresCopy.WriteAsync(db,
            "COPY negative_stocks (analysis_id, product_id, store_id, quantity, sold12months, priority, causes, " +
            "pending_transfer_units, duplicate_product_code) FROM STDIN (FORMAT BINARY)",
            negatives,
            async (w, n, ct) =>
            {
                await w.WriteAsync(n.AnalysisId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(n.ProductId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(n.StoreId, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(n.Quantity, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync(n.Sold12Months, NpgsqlDbType.Numeric, ct);
                await w.WriteAsync((int)n.Priority, NpgsqlDbType.Integer, ct);
                await w.WriteAsync((int)n.Causes, NpgsqlDbType.Integer, ct);
                await w.WriteAsync(n.PendingTransferUnits, NpgsqlDbType.Numeric, ct);
                if (n.DuplicateProductCode is null)
                    await w.WriteNullAsync(ct);
                else
                    await w.WriteAsync(n.DuplicateProductCode, NpgsqlDbType.Varchar, ct);
            },
            cancellationToken);

    private Task CopyAlertsAsync(List<StockAlert> alerts, CancellationToken cancellationToken) =>
        PostgresCopy.WriteAsync(db,
            "COPY stock_alerts (analysis_id, type, priority, product_id, store_id, brand_id, message, seen_at, seen_by_email) FROM STDIN (FORMAT BINARY)",
            alerts,
            async (w, a, ct) =>
            {
                await w.WriteAsync(a.AnalysisId, NpgsqlDbType.Uuid, ct);
                await w.WriteAsync(a.Type.ToString(), NpgsqlDbType.Varchar, ct);
                await w.WriteAsync((int)a.Priority, NpgsqlDbType.Integer, ct);
                await WriteNullableIdAsync(w, a.ProductId, ct);
                await WriteNullableIdAsync(w, a.StoreId, ct);
                await WriteNullableIdAsync(w, a.BrandId, ct);
                await w.WriteAsync(a.Message, NpgsqlDbType.Varchar, ct);
                if (a.SeenAt is null)
                    await w.WriteNullAsync(ct);
                else
                    await w.WriteAsync(a.SeenAt.Value, NpgsqlDbType.TimestampTz, ct);
                if (a.SeenByEmail is null)
                    await w.WriteNullAsync(ct);
                else
                    await w.WriteAsync(a.SeenByEmail, NpgsqlDbType.Varchar, ct);
            },
            cancellationToken);

    private static Task WriteNullableIdAsync(Npgsql.NpgsqlBinaryImporter writer, int? value, CancellationToken cancellationToken) =>
        value is null ? writer.WriteNullAsync(cancellationToken) : writer.WriteAsync(value.Value, NpgsqlDbType.Integer, cancellationToken);

    private static Task WriteNullableAsync(Npgsql.NpgsqlBinaryImporter writer, decimal? value, CancellationToken cancellationToken) =>
        value is null ? writer.WriteNullAsync(cancellationToken) : writer.WriteAsync(Math.Round(value.Value, 2), NpgsqlDbType.Numeric, cancellationToken);

    /// <summary>Estoque ou vendas importados, ou parâmetros alterados, depois da análise.</summary>
    private async Task<bool> IsOutdatedAsync(StockAnalysis latest, CancellationToken cancellationToken) =>
        await db.ImportBatches.AnyAsync(b =>
                (b.Type == ImportType.Stock || b.Type == ImportType.Sales) && b.Status == ImportStatus.Confirmed && b.DecidedAt > latest.CreatedAt,
            cancellationToken)
        || await db.StockParameters.AnyAsync(p => p.UpdatedAt > latest.CreatedAt, cancellationToken);

    private static StockParametersDto ToDto(StockParameters p) =>
        new(p.CriticalCoverageDays, p.MinimumDays, p.IdealDays, p.MaximumDays, p.ExcessDays, p.MinimumAnnualSales, p.UpdatedAt, p.UpdatedByEmail, p.SendAlertEmail);

    private sealed record AnalysisInput(
        Dictionary<int, Store> Stores,
        Dictionary<int, Dictionary<int, (decimal Stock, decimal Sold)>> Products,
        HashSet<int> Seasonal,
        Dictionary<int, IReadOnlyList<InTransit>> InTransit);

    private sealed record PositionRow(int ProductId, int StoreId, decimal Stock, decimal Sold, PositionMetrics Metrics);

    private sealed record AnalysisResult(
        List<PositionRow> Positions,
        List<(int ProductId, PlannedTransfer Plan)> Transfers,
        List<(int ProductId, PlannedPurchase Plan)> Purchases);
}
