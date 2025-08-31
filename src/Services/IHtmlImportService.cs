namespace RecipeOptimizer.Services;

public interface IHtmlImportService
{
    Task<PdfImportResult?> ImportFromUrlAsync(string url, CancellationToken ct = default);
}