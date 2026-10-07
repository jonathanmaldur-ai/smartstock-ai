using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartStock.Application.Analysis;
using SmartStock.Application.Assistant;
using SmartStock.Application.Catalog;
using SmartStock.Application.Inventory;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Catalog;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Assistant;

/// <summary>
/// Responde às perguntas do chat (decisão 41) usando as mesmas consultas das telas de análise,
/// para os números baterem com o resto da plataforma. Só leitura.
/// </summary>
internal sealed class AssistantService(
    SmartStockDbContext db,
    IAnalysisQueryService analysis,
    INegativeStockQueryService negatives,
    IDashboardService dashboard,
    ICatalogService catalog,
    IInventoryQueryService inventory) : IAssistantService
{
    private const int RowsShown = 10;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Examples =
    [
        "Resumo da rede",
        "Rupturas no Buriti",
        "O que transferir para Taubaté?",
        "Negativos da Matriz",
        "Onde tem estalo de salão?",
        "Produtos parados em Pindamonhangaba",
        "O que comprar da Mattel?",
        "Mais vendidos da rede"
    ];

    public async Task<AssistantAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var parser = new QuestionParser(await StoresAsync(cancellationToken), await BrandsAsync(cancellationToken));
        var q = parser.Parse(question);

        if (q.Topic != QuestionTopic.Help && !await db.StockAnalyses.AnyAsync(cancellationToken))
            return Answer("Ainda não há análise gerada. Em Situação do estoque, clique em \"Gerar nova análise\" e pergunte de novo.", q,
                links: [new AnswerLink("Situação do estoque", "/estoque")]);

        return q.Topic switch
        {
            QuestionTopic.Summary => await SummaryAsync(q, cancellationToken),
            QuestionTopic.Ruptures => await PositionsAsync(q, StockSituation.Rupture, "em ruptura (vendem e estão sem estoque)", cancellationToken),
            QuestionTopic.Stagnant => await PositionsAsync(q, StockSituation.Stagnant, "parados (têm estoque e não venderam em 12 meses)", cancellationToken),
            QuestionTopic.Excess => await PositionsAsync(q, StockSituation.Excess, "em excesso (estoque para mais de 120 dias)", cancellationToken),
            QuestionTopic.Negatives => await NegativesAsync(q, cancellationToken),
            QuestionTopic.Transfers => await TransfersAsync(q, cancellationToken),
            QuestionTopic.Purchases => await PurchasesAsync(q, cancellationToken),
            QuestionTopic.ProductStock => await ProductStockAsync(q, cancellationToken),
            QuestionTopic.TopSellers => await TopSellersAsync(q, cancellationToken),
            _ => Help()
        };
    }

    private static AssistantAnswer Help() =>
        new("Eu respondo perguntas sobre o estoque da rede com os dados da última análise: rupturas, estoque negativo, " +
            "transferências sugeridas, compras, produtos parados ou em excesso, onde tem um produto, os mais vendidos e o resumo " +
            "de uma loja. Cite a loja (nome, código ou apelido, como \"Buriti\", \"09\" ou \"SJC\"), a marca ou o produto.",
            null, null, [], Examples);

    private async Task<AssistantAnswer> SummaryAsync(ParsedQuestion q, CancellationToken cancellationToken)
    {
        var data = await dashboard.GetAsync(cancellationToken);
        var k = data.Kpis!;
        if (q.Store is null)
        {
            var text = $"Na rede toda (análise de {k.AnalysisDate:dd/MM}, estoque de {k.StockDate:dd/MM}): {N(k.StockUnits)} unidades em estoque, " +
                       $"cobertura de {N(k.NetworkCoverageDays ?? 0)} dias, {N(k.RelevantRuptures)} rupturas que importam, " +
                       $"{N(k.PendingSuggestions)} sugestões de transferência pendentes ({N(k.PendingUnits)} un.) e {N(k.NegativeItems)} itens negativos.";
            return Answer(text, q, links: [new("Dashboard", "/"), new("Sugestões de transferência", "/sugestoes")],
                suggestions: ["Rupturas no Buriti", "O que transferir para Taubaté?", "Negativos da Matriz"]);
        }

        var s = data.Stores.FirstOrDefault(r => r.StoreId == q.Store.Id);
        if (s is null)
            return Answer($"Não encontrei dados da loja {Label(q.Store)} na última análise.", q);
        var pending = await analysis.ListSuggestionsAsync(
            new SuggestionQuery(SuggestionStatus.Suggested, null, s.StoreId, null, null, null, null, 1, 1, HideNegativeDestination: true), cancellationToken);
        var coverage = s.CoverageDays is null ? "" : $", cobertura de {N(s.CoverageDays.Value)} dias";
        var summary = $"{Label(q.Store)}: {N(s.StockUnits)} unidades em estoque, {N(s.Sold12Months)} vendidas em 12 meses{coverage}. " +
                      $"{N(s.Rupture)} itens em ruptura, {N(s.BelowMinimum)} abaixo do mínimo, {N(s.Excess)} em excesso e {N(s.Stagnant)} parados. " +
                      $"{N(pending.TotalCount)} sugestões de transferência para a loja aguardam aprovação.";
        return Answer(summary, q, links: [new("Situação do estoque", "/estoque")],
            suggestions: [$"Rupturas na {q.Store.Name}", $"O que transferir para {q.Store.Name}?", $"Negativos da {q.Store.Name}"]);
    }

    private async Task<AssistantAnswer> PositionsAsync(ParsedQuestion q, StockSituation situation, string description, CancellationToken cancellationToken)
    {
        var result = await analysis.ListPositionsAsync(
            new PositionQuery(situation, q.Store?.Id, q.Brand?.Id, null, q.Search, 1, RowsShown), cancellationToken);
        if (result.TotalCount == 0)
            return Answer($"Nenhum item {description}{Scope(q)}.", q, links: [new("Situação do estoque", "/estoque")]);

        var text = $"{N(result.TotalCount)} itens {description}{Scope(q)}. Os mais importantes:";
        var table = new AnswerTable(
            ["Produto", "Loja", "Estoque", "Vende/ano", "Cobertura", "Prioridade"],
            result.Items.Select(p => (IReadOnlyList<string>)
                [$"{p.ProductDescription} ({p.ProductCode})", $"{p.StoreCode} {p.StoreName}", N(p.Stock), N(p.Sold12Months),
                 p.CoverageDays is null ? "—" : $"{N(p.CoverageDays.Value)} dias", PriorityLabel(p.Priority)]).ToList());
        return Answer(text, q, table, [new("Situação do estoque", "/estoque")],
            situation == StockSituation.Rupture ? [$"O que transferir{(q.Store is null ? "" : $" para {q.Store.Name}")}?"] : []);
    }

    private async Task<AssistantAnswer> NegativesAsync(ParsedQuestion q, CancellationToken cancellationToken)
    {
        var result = await negatives.ListAsync(new NegativeQuery(q.Store?.Id, null, null, q.Brand?.Id, null, q.Search, 1, RowsShown), cancellationToken);
        if (result.TotalCount == 0)
            return Answer($"Nenhum estoque negativo{Scope(q)}.", q, links: [new("Estoque negativo", "/negativos")]);

        var text = $"{N(result.TotalCount)} itens com estoque negativo{Scope(q)}. Os mais urgentes (críticos vendem na loja):";
        var table = new AnswerTable(
            ["Produto", "Loja", "Estoque", "Vende/ano", "Prioridade", "Causa provável"],
            result.Items.Select(n => (IReadOnlyList<string>)
                [$"{n.ProductDescription} ({n.ProductCode})", $"{n.StoreCode} {n.StoreName}", N(n.Quantity), N(n.Sold12Months),
                 PriorityLabel(n.Priority), n.Causes.Count == 0 ? "—" : string.Join("; ", n.Causes.Select(CauseLabel))]).ToList());
        return Answer(text, q, table, [new("Estoque negativo", "/negativos")]);
    }

    private async Task<AssistantAnswer> TransfersAsync(ParsedQuestion q, CancellationToken cancellationToken)
    {
        // Uma loja citada sem "de"/"para" é tratada como destino ("o que mandar para o Buriti").
        var destination = q.Destination ?? (q.Origin is null ? q.Store : null);
        var query = new SuggestionQuery(SuggestionStatus.Suggested, q.Origin?.Id, destination?.Id, null, q.Brand?.Id, null, q.Search, 1, RowsShown,
            HideNegativeDestination: true);
        var result = await analysis.ListSuggestionsAsync(query, cancellationToken);
        var route = (q.Origin is null ? "" : $" saindo de {Label(q.Origin)}") + (destination is null ? "" : $" para {Label(destination)}");
        var brand = q.Brand is null ? "" : $" da marca {q.Brand.Name}";
        var product = q.Search is null ? "" : $" com \"{q.Search.ToLowerInvariant()}\"";
        if (result.TotalCount == 0)
            return Answer($"Nenhuma sugestão de transferência pendente{route}{brand}{product}.", q, links: [new("Sugestões de transferência", "/sugestoes")]);

        var routes = await analysis.ListRoutesAsync(query with { Page = 1, PageSize = 1 }, cancellationToken);
        var text = $"{N(result.TotalCount)} sugestões pendentes{route}{brand}{product}, somando {N(routes.Sum(r => r.Units))} unidades " +
                   $"(sem as lojas com estoque negativo). As mais urgentes:";
        var table = new AnswerTable(
            ["Produto", "De → Para", "Enviar", "No destino hoje", "Prioridade"],
            result.Items.Select(s => (IReadOnlyList<string>)
                [$"{s.ProductDescription} ({s.ProductCode})", $"{s.OriginCode} → {s.DestinationCode}", $"{N(s.Quantity)} un.",
                 $"{N(s.DestinationStock)} un.", PriorityLabel(s.Priority)]).ToList());
        return Answer(text, q, table, [new("Sugestões de transferência", "/sugestoes")]);
    }

    private async Task<AssistantAnswer> PurchasesAsync(ParsedQuestion q, CancellationToken cancellationToken)
    {
        var result = await analysis.ListPurchasesAsync(new PurchaseQuery(q.Store?.Id, q.Brand?.Id, null, q.Search, 1, RowsShown), cancellationToken);
        if (result.TotalCount == 0)
            return Answer($"Nenhuma sugestão de compra{Scope(q)}.", q, links: [new("Sugestões de compra", "/compras")]);

        var text = $"{N(result.TotalCount)} faltas que a rede não consegue cobrir{Scope(q)} (recomendação, não pedido). As de maior venda:";
        var table = new AnswerTable(
            ["Produto", "Loja", "Falta", "Vende/dia"],
            result.Items.Select(p => (IReadOnlyList<string>)
                [$"{p.ProductDescription} ({p.ProductCode})", $"{p.StoreCode} {p.StoreName}", $"{N(p.Quantity)} un.", N(p.DailyAverage, 2)]).ToList());
        return Answer(text, q, table, [new("Sugestões de compra", "/compras")]);
    }

    private async Task<AssistantAnswer> ProductStockAsync(ParsedQuestion q, CancellationToken cancellationToken)
    {
        if (q.Search is null && q.Brand is null)
            return Answer("De qual produto? Escreva o nome, a referência ou o código. Ex.: \"onde tem estalo de salão\".", q);

        var found = await catalog.ListProductsAsync(new ProductQuery(q.Search, q.Brand?.Id, null, null, 1, 5), cancellationToken);
        if (found.TotalCount == 0)
            return Answer($"Não encontrei produto com \"{q.Search?.ToLowerInvariant()}\". Tente outra palavra da descrição, a referência ou o código.", q,
                links: [new("Produtos", "/produtos")]);

        var product = found.Items.FirstOrDefault(p => p.Code == q.Search) ?? found.Items[0];
        var overview = (await inventory.GetProductOverviewAsync(product.Id, cancellationToken)).Value;
        var withStock = overview.Stores.Where(s => s.Stock is not null && s.Stock != 0).ToList();
        var total = overview.Stores.Sum(s => Math.Max(s.Stock ?? 0, 0));
        var text = $"{product.Description} ({product.Code}, {product.BrandName}): {N(total)} unidades na rede" +
                   (overview.StockDate is null ? "." : $" (estoque de {overview.StockDate:dd/MM}).") +
                   (found.TotalCount > 1 ? $" Encontrei {N(found.TotalCount)} produtos parecidos; mostrando o primeiro." : "");
        var table = new AnswerTable(
            ["Loja", "Estoque", "Vende/ano", "Cobertura"],
            overview.Stores.Where(s => (s.Stock ?? 0) != 0 || (s.Sold12Months ?? 0) > 0)
                .Select(s => (IReadOnlyList<string>)
                    [$"{s.StoreCode} {s.StoreName}", N(s.Stock ?? 0), s.Sold12Months is null ? "—" : N(s.Sold12Months.Value),
                     s.CoverageDays is null ? "—" : $"{N(s.CoverageDays.Value)} dias"]).ToList());
        var others = found.Items.Where(p => p.Id != product.Id).Take(3).Select(p => $"Onde tem {p.Code}").ToList();
        return Answer(text, q, withStock.Count == 0 && table.Rows.Count == 0 ? null : table, [new("Produtos (ver ficha)", "/produtos")], others);
    }

    private async Task<AssistantAnswer> TopSellersAsync(ParsedQuestion q, CancellationToken cancellationToken)
    {
        var data = await dashboard.GetAsync(cancellationToken);
        var note = q.Store is null && q.Brand is null ? "" : " Por enquanto o ranking é da rede toda (sem filtro de loja ou marca).";
        var table = new AnswerTable(
            ["Produto", "Vendas 12 meses", "Estoque na rede", "Lojas em ruptura"],
            data.TopProducts.Select(p => (IReadOnlyList<string>)
                [$"{p.Description} ({p.Code})", N(p.Sold12Months), N(p.StockUnits), N(p.RuptureStores)]).ToList());
        return Answer($"Os 10 produtos mais vendidos da rede nos últimos 12 meses.{note}", q, table, [new("Dashboard", "/")]);
    }

    private async Task<IReadOnlyList<KnownStore>> StoresAsync(CancellationToken cancellationToken) =>
        await db.Stores.AsNoTracking().Where(s => s.Status == StoreStatus.Active)
            .Select(s => new KnownStore(s.Id, s.Code, s.Name)).ToListAsync(cancellationToken);

    private async Task<IReadOnlyList<KnownBrand>> BrandsAsync(CancellationToken cancellationToken) =>
        await db.Brands.AsNoTracking().Select(b => new KnownBrand(b.Id, b.Name)).ToListAsync(cancellationToken);

    private static AssistantAnswer Answer(
        string text, ParsedQuestion q, AnswerTable? table = null, IReadOnlyList<AnswerLink>? links = null, IReadOnlyList<string>? suggestions = null) =>
        new(text, Understood(q), table, links ?? [], suggestions ?? []);

    /// <summary>"Entendi: ruptura · loja 09 Buriti Shopping · marca MATTEL · produto "estalo"".</summary>
    private static string Understood(ParsedQuestion q)
    {
        var parts = new List<string> { TopicLabel(q.Topic) };
        if (q.Origin is not null) parts.Add($"de {Label(q.Origin)}");
        if (q.Destination is not null) parts.Add($"para {Label(q.Destination)}");
        if (q.Store is not null && q.Origin is null && q.Destination is null) parts.Add($"loja {Label(q.Store)}");
        if (q.Brand is not null) parts.Add($"marca {q.Brand.Name}");
        if (q.Search is not null) parts.Add($"produto \"{q.Search.ToLowerInvariant()}\"");
        return "Entendi: " + string.Join(" · ", parts);
    }

    private static string Scope(ParsedQuestion q) =>
        (q.Store is null ? "" : $" na {Label(q.Store)}") + (q.Brand is null ? "" : $" da marca {q.Brand.Name}") +
        (q.Search is null ? "" : $" com \"{q.Search.ToLowerInvariant()}\"");

    private static string Label(KnownStore store) => $"{store.Code} {store.Name}";

    private static string N(decimal value, int decimals = 0) => value.ToString("N" + decimals, PtBr);
    private static string N(int value) => value.ToString("N0", PtBr);

    private static string TopicLabel(QuestionTopic topic) => topic switch
    {
        QuestionTopic.Summary => "resumo",
        QuestionTopic.Ruptures => "rupturas",
        QuestionTopic.Negatives => "estoque negativo",
        QuestionTopic.Transfers => "transferências sugeridas",
        QuestionTopic.Purchases => "sugestões de compra",
        QuestionTopic.Stagnant => "produtos parados",
        QuestionTopic.Excess => "excesso de estoque",
        QuestionTopic.ProductStock => "onde tem o produto",
        QuestionTopic.TopSellers => "mais vendidos",
        _ => "ajuda"
    };

    private static string PriorityLabel(AlertPriority priority) => priority switch
    {
        AlertPriority.Critical => "Crítica",
        AlertPriority.High => "Alta",
        AlertPriority.Medium => "Média",
        AlertPriority.Low => "Baixa",
        _ => "—"
    };

    private static string CauseLabel(NegativeCause cause) => cause switch
    {
        NegativeCause.TransferNotReceived => "transferência não recebida",
        NegativeCause.SaleWithoutEntry => "vende sem entrada",
        NegativeCause.ShippedWithoutEntry => "saiu sem entrada de nota",
        NegativeCause.FractionalUnit => "unidade fracionada",
        NegativeCause.PossibleDuplicate => $"possível duplicado",
        _ => cause.ToString()
    };
}
