using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartStock.Domain.Analysis;
using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;
using SmartStock.Tests.Infrastructure;
using static SmartStock.Tests.Infrastructure.ImportTestHelpers;

namespace SmartStock.Tests.Api;

/// <summary>Retenção do detalhe das análises (decisão 56).</summary>
[Collection(ApiCollection.Name)]
public sealed class AnalysisRetentionTests(SmartStockApiFactory factory)
{
    [Fact]
    public async Task Analise_remove_o_detalhe_antigo_e_mantem_a_ultima_de_cada_foto_e_as_decisoes()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var product = UniqueCode("797");
        var brand = UniqueCode("2");
        await admin.ImportAndConfirmAsync("Brands", "Marca.xlsx", Xlsx(BrandHeaders, [brand, $"MARCA {brand}", "1"]));
        await admin.ImportAndConfirmAsync("Products", "Produtos.xlsx",
            Xlsx(ProductHeaders, [product, $"PRODUTO {product}", brand, "PC", "1", "BRINQUEDOS", "OUTROS", "R", ""]));
        await admin.ImportAndConfirmAsync("Stock", "Estoque.xlsx", Xlsx(["Codigo do Produto", "05 DEPOSITO", "06 MOGI MIRIM"], [product, "10", "1"]), today);

        // Foto antiga com duas análises (a primeira perde o detalhe) e outra foto com uma só (mantida).
        var oldDate = new DateOnly(Random.Shared.Next(1990, 2020), 1, 15);
        var (removed, keptSameDate, keptOnly, suggestionId) = await SeedOldAnalysesAsync(product, oldDate);

        (await admin.PostAsync("/api/analysis", null)).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartStockDbContext>();
        var analyses = await db.StockAnalyses.AsNoTracking().Where(a => new[] { removed, keptSameDate, keptOnly }.Contains(a.Id)).ToListAsync();
        Assert.NotNull(analyses.Single(a => a.Id == removed).DetailsRemovedAt);
        Assert.Null(analyses.Single(a => a.Id == keptSameDate).DetailsRemovedAt);
        Assert.Null(analyses.Single(a => a.Id == keptOnly).DetailsRemovedAt);
        Assert.False(await db.StockPositions.AnyAsync(p => p.AnalysisId == removed));
        Assert.True(await db.StockPositions.AnyAsync(p => p.AnalysisId == keptSameDate));
        Assert.True(await db.TransferSuggestions.AnyAsync(s => s.Id == suggestionId)); // decisão preservada
    }

    private async Task<(Guid Removed, Guid KeptSameDate, Guid KeptOnly, long SuggestionId)> SeedOldAnalysesAsync(string productCode, DateOnly oldDate)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartStockDbContext>();
        var productId = await db.Products.Where(p => p.Code == productCode).Select(p => p.Id).SingleAsync();
        var storeId = await db.Stores.Where(s => s.Code == "06").Select(s => s.Id).SingleAsync();
        var batchId = await db.ImportBatches.Where(b => b.Type == ImportType.Stock && b.Status == ImportStatus.Confirmed).Select(b => b.Id).FirstAsync();
        var now = DateTimeOffset.UtcNow;

        StockAnalysis Analysis(DateOnly stockDate, int daysAgo) => new()
        {
            Id = Guid.NewGuid(), StockImportId = batchId, StockDate = stockDate, AnalysisDate = stockDate,
            CreatedAt = now.AddDays(-daysAgo), CreatedByEmail = "teste"
        };
        var removed = Analysis(oldDate, 90);
        var keptSameDate = Analysis(oldDate, 89);
        var keptOnly = Analysis(oldDate.AddDays(-10), 100);
        db.StockAnalyses.AddRange(removed, keptSameDate, keptOnly);
        foreach (var analysis in new[] { removed, keptSameDate, keptOnly })
            db.StockPositions.Add(new StockPosition
            {
                AnalysisId = analysis.Id, ProductId = productId, StoreId = storeId, Stock = 1, ProjectedStock = 1,
                Situation = StockSituation.Normal, Priority = AlertPriority.None
            });
        var suggestion = new TransferSuggestion
        {
            AnalysisId = removed.Id, ProductId = productId, OriginStoreId = storeId, DestinationStoreId = storeId, Quantity = 1,
            Priority = AlertPriority.Low, Status = SuggestionStatus.Rejected, Reason = "teste", DecidedAt = now.AddDays(-90), DecidedByEmail = "teste"
        };
        db.TransferSuggestions.Add(suggestion);
        await db.SaveChangesAsync();
        return (removed.Id, keptSameDate.Id, keptOnly.Id, suggestion.Id);
    }
}
