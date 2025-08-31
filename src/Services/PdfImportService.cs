// /src/Services/PdfImportService.cs
using System.Text;
using UglyToad.PdfPig;
using PdfPage = UglyToad.PdfPig.Content.Page;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using static RecipeOptimizer.Services.RecipeTextSplitter;

namespace RecipeOptimizer.Services
{
    public class PdfImportService : IPdfImportService
    {
        private readonly IDebugDumpService _dump;
        
        public PdfImportService(IDebugDumpService dump)
        {
            _dump = dump;
        }
        
        public async Task<PdfImportResult> ImportRecipeFromPdfAsync(Stream pdfStream, CancellationToken ct = default)
        {
            try
            {
                // PdfPig needs a seekable stream; copy if necessary.
                Stream working = pdfStream;
                if (!pdfStream.CanSeek)
                {
                    var ms = new MemoryStream();
                    await pdfStream.CopyToAsync(ms, ct);
                    ms.Position = 0;
                    working = ms;
                }

                var raw = ExtractAllText(working);
                if (string.IsNullOrWhiteSpace(raw))
                    return new PdfImportResult(false, null, null, null, null, null, "No text found in PDF.");

                var (title, servings, ingredients, steps, equipment) = SplitRecipeText(raw);
                var dump = new StringBuilder()
                    .AppendLine("RAW TEXT:")
                    .AppendLine(raw)
                    .AppendLine()
                    .AppendLine("== SPLIT RESULT ==")
                    .AppendLine($"Title: {title}")
                    .AppendLine($"Servings: {servings}")
                    .AppendLine()
                    .AppendLine("[EQUIPMENT]")
                    .AppendLine(equipment ?? "")
                    .AppendLine()   
                    .AppendLine("[INGREDIENTS]")
                    .AppendLine(ingredients ?? "")
                    .AppendLine()
                    .AppendLine("[STEPS]")
                    .AppendLine(steps ?? "")
                    .ToString();

#if DEBUG

                await _dump.AppendAsync("PDF Import", dump, ct);

#endif
                
                
                return new PdfImportResult(true, title, servings, ingredients, steps, equipment, null);
            }
            catch (Exception ex)
            {
                return new PdfImportResult(false, null, null, null, null,  null, $"PDF import failed: {ex.Message}");
            }
        }

        private static string ExtractAllText(Stream s)
        {
            s.Position = 0;
            var sb = new StringBuilder();
            using var doc = PdfDocument.Open(s);
            foreach (PdfPage page in doc.GetPages())
            {
                var pageText = ExtractPageText(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine(pageText.TrimEnd());
                    sb.AppendLine(); // page break
                }
            }
            return NormalizeWhitespace(sb.ToString());
        }

        private static string ExtractPageText(PdfPage page)
        {
            // 1) Best effort: PdfPig’s layout-aware extractor (includes line breaks)
            string? text = null;
            try
            {
                text = ContentOrderTextExtractor.GetText(page);
            }
            catch { /* fall back */ }

            if (!string.IsNullOrWhiteSpace(text))
                return text;

            // 2) Fallback: group words into lines by Y position
            var words = page.GetWords().ToList();
            if (words.Count == 0) return page.Text; // last resort

            // Tolerance for line “row” grouping; adjust if needed
            const double yTolerance = 2.0;
            var rows = new List<(double y, List<string> items)>();

            
            foreach (var w in words)
            {
                var y = w.BoundingBox.Bottom; // or w.Baseline.YStart
                int idx = rows.FindIndex(r => Math.Abs(r.y - y) <= yTolerance);
                if (idx < 0)
                    rows.Add((y, new List<string> { w.Text }));
                else
                    rows[idx].items.Add(w.Text);
            }

            // Sort rows from top to bottom (PDF Y increases up); PdfPig pages typically have higher Y at top
            rows.Sort((a, b) => b.y.CompareTo(a.y));

            var sb = new StringBuilder();
            foreach (var row in rows)
                sb.AppendLine(string.Join(" ", row.items));

            return sb.ToString();
        }

        

        

        
    }
}
