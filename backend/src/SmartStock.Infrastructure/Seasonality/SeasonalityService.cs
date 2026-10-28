using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Abstractions;
using SmartStock.Application.Common;
using SmartStock.Application.Seasonality;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Auditing;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Seasonality;

/// <summary>Sazonalidade (decisão 65): épocas, sugestões e produtos sazonais.</summary>
internal sealed class SeasonalityService(SmartStockDbContext db, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock)
    : ISeasonalityService
{
    private const int MaxPageSize = 200;

    /// <summary>Limite de ids gravados na auditoria de uma decisão em lote (o total vai sempre).</summary>
    private const int MaxAuditedIds = 1000;

    private static readonly Error SeasonNotFound = Error.NotFound("season.not_found", "Época não encontrada.");

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // ---- Épocas ----------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<SeasonDto>> ListSeasonsAsync(CancellationToken cancellationToken = default)
    {
        var seasons = await db.Seasons.AsNoTracking().OrderBy(s => s.StartMonth).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        var products = await db.Products.AsNoTracking()
            .Where(p => p.SeasonId != null)
            .GroupBy(p => p.SeasonId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var pending = await db.SeasonalSuggestions.AsNoTracking()
            .Where(s => s.Status == SeasonalSuggestionStatus.Pending && s.SeasonId != null)
            .GroupBy(s => s.SeasonId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return seasons.Select(s => ToDto(s, products.GetValueOrDefault(s.Id), pending.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<Result<SeasonDto>> CreateSeasonAsync(SeasonInput input, CancellationToken cancellationToken = default)
    {
        var problem = await ValidateAsync(input, null, cancellationToken);
        if (problem is not null)
            return problem;

        var season = new Season();
        Apply(season, input);
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.SeasonCreated, AuditResult.Success, "Season", season.Id.ToString(), Details: Snapshot(season)), cancellationToken);
        return ToDto(season, 0, 0);
    }

    public async Task<Result<SeasonDto>> UpdateSeasonAsync(int id, SeasonInput input, CancellationToken cancellationToken = default)
    {
        var season = await db.Seasons.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (season is null)
            return SeasonNotFound;
        var problem = await ValidateAsync(input, id, cancellationToken);
        if (problem is not null)
            return problem;

        var before = Snapshot(season);
        Apply(season, input);
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.SeasonUpdated, AuditResult.Success, "Season", season.Id.ToString(),
            Details: new { before, after = Snapshot(season) }), cancellationToken);
        return (await ListSeasonsAsync(cancellationToken)).Single(s => s.Id == id);
    }

    private async Task<Error?> ValidateAsync(SeasonInput input, int? id, CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 60)
            return Error.Validation("season.invalid_name", "Informe o nome da época (até 60 caracteres).");
        if (input.StartMonth is < 1 or > 12 || input.EndMonth is < 1 or > 12)
            return Error.Validation("season.invalid_months", "Os meses vão de 1 (janeiro) a 12 (dezembro).");
        var keywords = Season.NormalizeKeywords(input.Keywords ?? []);
        if (keywords.Length == 0)
            return Error.Validation("season.no_keywords", "Informe pelo menos uma palavra-chave.");
        if (keywords.Length > 1000)
            return Error.Validation("season.keywords_too_long", "As palavras-chave passam do limite de 1.000 caracteres.");
        if (await db.Seasons.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), cancellationToken))
            return Error.Conflict("season.duplicated", "Já existe uma época com esse nome.");
        return null;
    }

    private void Apply(Season season, SeasonInput input)
    {
        season.Name = input.Name.Trim();
        season.StartMonth = input.StartMonth;
        season.EndMonth = input.EndMonth;
        season.WorldCupOnly = input.WorldCupOnly;
        season.Keywords = Season.NormalizeKeywords(input.Keywords);
        season.MatchDescription = input.MatchDescription;
        season.IsActive = input.IsActive;
        season.UpdatedAt = clock.GetUtcNow();
        season.UpdatedByEmail = currentUser.Email;
    }

    private SeasonDto ToDto(Season s, int productCount, int pending) =>
        new(s.Id, s.Name, s.StartMonth, s.EndMonth, s.WorldCupOnly, s.KeywordList, s.MatchDescription, s.IsActive, s.IsActive && s.IsInSeason(Today), productCount, pending);

    private static object Snapshot(Season s) => new { s.Name, s.StartMonth, s.EndMonth, s.WorldCupOnly, s.Keywords, s.MatchDescription, s.IsActive };

    // ---- Sugestões -------------------------------------------------------------------------------------------------

    public async Task<PagedResult<SeasonalSuggestionDto>> ListSuggestionsAsync(SeasonalSuggestionQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = (Math.Max(1, query.Page), Math.Clamp(query.PageSize, 1, MaxPageSize));
        var latest = await LatestAnalysisIdAsync(cancellationToken);
        var filtered =
            from s in Pending(query.SeasonId, query.WithoutSeason, query.Kind)
            join p in db.Products on s.ProductId equals p.Id
            select new { s, p };
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            filtered = filtered.Where(x => x.p.Code.StartsWith(term) || EF.Functions.ILike(x.p.Description, $"%{term}%"));
        }

        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            .Select(x => new
            {
                x.s.Id, x.s.ProductId, x.p.Code, x.p.Description, Brand = x.p.Brand.Name, Category = x.p.Category.Name, x.s.SeasonId,
                SeasonName = x.s.Season != null ? x.s.Season.Name : null, x.s.Kind, x.s.Evidence, x.s.UpdatedAt,
                Stock = db.StockPositions.Where(sp => sp.AnalysisId == latest && sp.ProductId == x.s.ProductId && sp.Stock > 0).Sum(sp => (decimal?)sp.Stock) ?? 0,
                Sold = db.StockPositions.Where(sp => sp.AnalysisId == latest && sp.ProductId == x.s.ProductId).Sum(sp => (decimal?)sp.Sold12Months) ?? 0
            })
            .OrderBy(x => x.SeasonName == null).ThenBy(x => x.SeasonName).ThenByDescending(x => x.Stock).ThenBy(x => x.Description)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new SeasonalSuggestionDto(
            r.Id, r.ProductId, r.Code, r.Description, r.Brand, r.Category, r.SeasonId, r.SeasonName, r.Kind.ToString(), r.Evidence,
            r.Stock, r.Sold, r.UpdatedAt)).ToList();
        return new PagedResult<SeasonalSuggestionDto>(items, page, pageSize, total);
    }

    public async Task<Result<int>> DecideAsync(SeasonalDecision decision, CancellationToken cancellationToken = default)
    {
        var ids = (decision.Ids ?? []).Distinct().ToList();
        var pending = Pending(decision.SeasonId, decision.WithoutSeason, decision.Kind);
        if (ids.Count > 0)
            pending = pending.Where(s => ids.Contains(s.Id));
        var suggestions = await pending.ToListAsync(cancellationToken);
        if (suggestions.Count == 0)
            return Error.Conflict("seasonal.not_pending", "Nenhuma das sugestões selecionadas está pendente.");

        var now = clock.GetUtcNow();
        var productIds = suggestions.Select(s => s.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        foreach (var suggestion in suggestions)
        {
            suggestion.Status = decision.Confirm ? SeasonalSuggestionStatus.Confirmed : SeasonalSuggestionStatus.Rejected;
            suggestion.DecidedAt = now;
            suggestion.DecidedByEmail = currentUser.Email;
            if (!decision.Confirm)
                continue;
            // Com época: fora dela, o produto sai das sugestões. Sem época (só a venda parou): fica fora de época até desmarcar.
            if (suggestion.SeasonId is not null)
                products[suggestion.ProductId].SeasonId = suggestion.SeasonId;
            else
                products[suggestion.ProductId].IsSeasonal = true;
        }
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            decision.Confirm ? AuditActions.SeasonalSuggestionsConfirmed : AuditActions.SeasonalSuggestionsRejected,
            AuditResult.Success, "SeasonalSuggestion", suggestions.Count == 1 ? suggestions[0].Id.ToString() : null,
            Details: new
            {
                count = suggestions.Count, decision.SeasonId, decision.WithoutSeason, decision.Kind, selected = ids.Count > 0,
                products = suggestions.Take(MaxAuditedIds).Select(s => s.ProductId)
            }),
            cancellationToken);
        return suggestions.Count;
    }

    private IQueryable<SeasonalSuggestion> Pending(int? seasonId, bool withoutSeason, string? kind)
    {
        var query = db.SeasonalSuggestions.Where(s => s.Status == SeasonalSuggestionStatus.Pending);
        if (withoutSeason)
            query = query.Where(s => s.SeasonId == null);
        else if (seasonId is not null)
            query = query.Where(s => s.SeasonId == seasonId);
        if (Enum.TryParse<SeasonalSuggestionKind>(kind, ignoreCase: true, out var parsed))
            query = query.Where(s => s.Kind == parsed);
        return query;
    }

    // ---- Produtos sazonais -----------------------------------------------------------------------------------------

    public async Task<PagedResult<SeasonalProductDto>> ListProductsAsync(SeasonalProductQuery query, CancellationToken cancellationToken = default)
    {
        var (page, pageSize) = (Math.Max(1, query.Page), Math.Clamp(query.PageSize, 1, MaxPageSize));
        var products = db.Products.AsNoTracking().Where(p => p.SeasonId != null || p.IsSeasonal);
        if (query.SeasonId is not null)
            products = products.Where(p => p.SeasonId == query.SeasonId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p => p.Code.StartsWith(term) || EF.Functions.ILike(p.Description, $"%{term}%"));
        }

        // Poucos milhares no máximo: o estado (em época ou não) é calculado aqui, com a data de hoje.
        var all = (await products
                .Select(p => new { p.Id, p.Code, p.Description, Brand = p.Brand.Name, Category = p.Category.Name, p.Season, p.IsSeasonal })
                .ToListAsync(cancellationToken))
            .Select(p => new { p, State = SeasonalRules.StateOf(p.Season, p.IsSeasonal, Today) })
            .Where(x => !query.OutOfSeasonOnly || x.State == SeasonState.OutOfSeason)
            .OrderBy(x => x.p.Season?.Name ?? "~").ThenBy(x => x.p.Description)
            .ToList();
        var pageItems = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var totals = await NetworkTotalsAsync(pageItems.Select(x => x.p.Id).ToList(), cancellationToken);

        var items = pageItems.Select(x => new SeasonalProductDto(
            x.p.Id, x.p.Code, x.p.Description, x.p.Brand, x.p.Category, x.p.Season?.Id, x.p.Season?.Name, x.p.IsSeasonal,
            x.State.ToString(), totals.GetValueOrDefault(x.p.Id).Stock, totals.GetValueOrDefault(x.p.Id).Sold)).ToList();
        return new PagedResult<SeasonalProductDto>(items, page, pageSize, all.Count);
    }

    public async Task<Result<SeasonalProductDto>> SetProductSeasonAsync(int productId, ProductSeasonInput input, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.Include(p => p.Season).SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
            return Error.NotFound("product.not_found", "Produto não encontrado.");
        if (input.SeasonId is not null && !await db.Seasons.AnyAsync(s => s.Id == input.SeasonId, cancellationToken))
            return SeasonNotFound;

        var before = new { product.SeasonId, outOfSeason = product.IsSeasonal };
        product.SeasonId = input.SeasonId;
        product.IsSeasonal = input.OutOfSeason;

        // A decisão manual resolve a sugestão pendente do produto.
        if (input.SeasonId is not null || input.OutOfSeason)
        {
            var now = clock.GetUtcNow();
            foreach (var pending in await db.SeasonalSuggestions
                         .Where(s => s.ProductId == productId && s.Status == SeasonalSuggestionStatus.Pending).ToListAsync(cancellationToken))
                (pending.Status, pending.DecidedAt, pending.DecidedByEmail) = (SeasonalSuggestionStatus.Confirmed, now, currentUser.Email);
        }
        await db.SaveChangesAsync(cancellationToken);

        await audit.LogAsync(new AuditEntry(
            AuditActions.ProductSeasonChanged, AuditResult.Success, "Product", product.Code,
            Details: new { product = product.Code, product.Description, before, after = new { input.SeasonId, input.OutOfSeason } }),
            cancellationToken);

        var season = input.SeasonId is null ? null : await db.Seasons.AsNoTracking().SingleAsync(s => s.Id == input.SeasonId, cancellationToken);
        var totals = await NetworkTotalsAsync([productId], cancellationToken);
        var brand = await db.Brands.Where(b => b.Id == product.BrandId).Select(b => b.Name).SingleAsync(cancellationToken);
        var category = await db.Categories.Where(c => c.Id == product.CategoryId).Select(c => c.Name).SingleAsync(cancellationToken);
        return new SeasonalProductDto(
            product.Id, product.Code, product.Description, brand, category, season?.Id, season?.Name, product.IsSeasonal,
            SeasonalRules.StateOf(season, product.IsSeasonal, Today).ToString(),
            totals.GetValueOrDefault(productId).Stock, totals.GetValueOrDefault(productId).Sold);
    }

    // ---- Apoio -----------------------------------------------------------------------------------------------------

    private Task<Guid?> LatestAnalysisIdAsync(CancellationToken cancellationToken) =>
        db.StockAnalyses.AsNoTracking().OrderByDescending(a => a.CreatedAt).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Estoque positivo e venda de 12 meses na rede, na última análise.</summary>
    private async Task<Dictionary<int, (decimal Stock, decimal Sold)>> NetworkTotalsAsync(List<int> productIds, CancellationToken cancellationToken)
    {
        var latest = await LatestAnalysisIdAsync(cancellationToken);
        if (latest is null || productIds.Count == 0)
            return [];
        return await db.StockPositions.AsNoTracking()
            .Where(p => p.AnalysisId == latest && productIds.Contains(p.ProductId))
            .GroupBy(p => p.ProductId)
            .Select(g => new { g.Key, Stock = g.Sum(p => p.Stock > 0 ? p.Stock : 0), Sold = g.Sum(p => p.Sold12Months) })
            .ToDictionaryAsync(g => g.Key, g => (g.Stock, g.Sold), cancellationToken);
    }
}
