using SmartStock.Domain.Imports;
using SmartStock.Infrastructure.Persistence;

namespace SmartStock.Infrastructure.Imports.Definitions;

/// <summary>
/// Arquivo de produtos do ERP (Produtos.xlsx). Marca que não está cadastrada vira marca provisória;
/// a categoria passa pela tabela de correção (de-para); marcas provisórias, categorias e subcategorias novas
/// são criadas na confirmação (<see cref="ProductCatalogBuilder"/>).
/// </summary>
internal sealed class ProductImportDefinition(SmartStockDbContext db, TimeProvider clock) : IImportDefinition
{
    private const string CodeColumn = "Codigo do Produto";
    private const string DescriptionColumn = "Descrição do Produto";
    private const string BrandColumn = "promar";
    private const string UnitColumn = "prouni";
    private const string StatusColumn = "Situação";
    private const string CategoryColumn = "Categoria";
    private const string SubcategoryColumn = "Categoria 2";
    private const string ReferenceColumn = "Referencia";
    private const string FiscalColumn = "clf_des";

    public ImportType Type => ImportType.Products;
    public string? CheckLayout(SheetData sheet) =>
        ImportLayout.RequireColumns(sheet, CodeColumn, DescriptionColumn, BrandColumn, CategoryColumn);

    public async Task<ImportValidation> ValidateAsync(ImportContext context, SheetData sheet, CancellationToken cancellationToken)
    {
        var issues = new IssueCollector();
        var catalog = await ProductCatalogBuilder.LoadAsync(db, clock, cancellationToken);

        foreach (var row in sheet.Rows)
        {
            var code = row.Get(CodeColumn);
            var description = row.Get(DescriptionColumn);
            var brandCode = CodeNormalizer.Brand(row.Get(BrandColumn));

            if (code is null)
                issues.Error(row.LineNumber, CodeColumn, "product.code_missing", "Código do produto não informado.");
            else if (code.Length > 30)
                issues.Error(row.LineNumber, CodeColumn, "product.code_too_long", "Código do produto com mais de 30 caracteres.", code);
            else if (catalog.Contains(code))
                issues.Error(row.LineNumber, CodeColumn, "product.duplicated", "Produto repetido no arquivo. Vale a primeira ocorrência.", code);

            if (description is null)
                issues.Error(row.LineNumber, DescriptionColumn, "product.description_missing", "Descrição do produto não informada.");

            if (brandCode is null)
                issues.Error(row.LineNumber, BrandColumn, "product.brand_missing", "Marca do produto não informada.");

            if (issues.HasError(row.LineNumber))
                continue;

            catalog.Add(
                new ProductInput(code!, description!, brandCode!, row.Get(UnitColumn), row.Get(ReferenceColumn), row.Get(FiscalColumn),
                    ParseActive(row, issues), row.Get(CategoryColumn), row.Get(SubcategoryColumn)),
                (BrandColumn, CategoryColumn),
                row.LineNumber,
                issues);
        }

        return new ImportValidation(
            issues,
            catalog.Count,
            async (batchId, ct) =>
            {
                await catalog.SaveAsync(batchId, ct);
                return ApplyResult.None;
            });
    }

    private static bool ParseActive(SheetRow row, IssueCollector issues)
    {
        var status = row.Get(StatusColumn);
        if (status is null or "1")
            return true;
        if (status == "0")
            return false;

        issues.Warning(row.LineNumber, StatusColumn, "product.status_unknown", "Situação desconhecida; o produto foi considerado ativo.", status);
        return true;
    }
}
