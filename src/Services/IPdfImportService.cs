// /src/Services/IPdfImportService.cs
using System.Threading.Tasks;

namespace RecipeOptimizer.Services
{
    public sealed record PdfImportResult(
        bool Success,
        string? Title,
        string? Servings,
        string? IngredientsText,
        string? Steps,
        string? Equipment,
        string? Error);
    
    public interface IPdfImportService
    {
        Task<PdfImportResult> ImportRecipeFromPdfAsync(Stream pdfStream, CancellationToken ct = default);
    }
}