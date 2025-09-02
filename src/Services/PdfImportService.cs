// /src/Services/PdfImportService.cs
using System.Text;
//using Android.Icu.Text;
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
            int pageNo = 0;
            foreach (PdfPage page in doc.GetPages())
            {
                pageNo++;
                var pageText = ExtractPageText(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine($"=== PAGE {pageNo} START ===");
                    sb.AppendLine(pageText.TrimEnd());
                    sb.AppendLine($"=== PAGE {pageNo} END ===");
                    sb.AppendLine(); // page break
                }
            }
            return NormalizeWhitespace(sb.ToString());
        }

        private static string ExtractPageText(PdfPage page)
        {
            // 1) try layout-aware first
            try
            {
                var t = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrWhiteSpace(t))
                    return CleanHeadersFooters(t);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            // 2) Reconstruct: words -> (maybe) columns -> rows
            var words = page.GetWords().ToList();
            if (words.Count == 0)
                return CleanHeadersFooters(page.Text ?? string.Empty);

            // Build a simple X histogram to detect 2 clusters (two columns)
            var xs = words.Select(w => w.BoundingBox.Left + (w.BoundingBox.Width / 2)).OrderBy(v => v).ToArray();
            double median = xs[xs.Length / 2];
            // split into left/right buckets by median gap
            var left = new List<UglyToad.PdfPig.Content.Word>();
            var right = new List<UglyToad.PdfPig.Content.Word>();
            // if the layout is actually 1 column, the "right" will be nearly empty; we handle that below
            foreach (var w in words)
            {
                var midX = w.BoundingBox.Left + (w.BoundingBox.Width / 2);
                if (midX < median)
                    left.Add(w);
                else
                    right.Add(w);
            }

            string RebuildColumn(List<UglyToad.PdfPig.Content.Word> bucket)
            {
                if (bucket.Count == 0) return string.Empty;
                
                // group into rows by Y (bottom) with tolerance, then sort each row by X
                const double yTol = 2.0;
                var rows = new List<(double y, List<UglyToad.PdfPig.Content.Word> items)>();
                foreach (var w in bucket)
                {
                    var y = w.BoundingBox.Bottom;
                    int idx = rows.FindIndex(r => Math.Abs(r.y - y) <= yTol);
                    if(idx < 0) rows.Add((y, new List<UglyToad.PdfPig.Content.Word> { w }));
                    else rows[idx].items.Add(w);
                }
                // top->bottom (PdfPig Y increases upward
                rows.Sort((a, b) => b.y.CompareTo(a.y));
                var sb = new StringBuilder();
                foreach (var row in rows)
                {
                    var line = row.items.OrderBy(i => i.BoundingBox.Left).Select(i => i.Text).ToArray();
                    sb.AppendLine(string.Join(" ", line));
                }
                return sb.ToString();
            }

            var leftText = RebuildColumn(left);
            var rightText = RebuildColumn(right);
            
            // if "right" is basically empty, treat as single column (use all words)
            if (string.IsNullOrWhiteSpace(rightText))
            {
                var all = new List<UglyToad.PdfPig.Content.Word>(words);
                all.Sort((a, b) =>
                {
                    // sort by Y desc, then X asc
                    int byY = -a.BoundingBox.Bottom.CompareTo(b.BoundingBox.Bottom);
                    return (byY != 0) ? byY : a.BoundingBox.Left.CompareTo(b.BoundingBox.Left);
                });
                
                var sb = new StringBuilder();
                double? curY = null;
                const double rowTol = 2.0;
                var row = new List<string>();
                foreach (var w in all)
                {
                    if (curY is null || Math.Abs(curY.Value - w.BoundingBox.Bottom) <= rowTol)
                    {
                        curY = curY ?? w.BoundingBox.Bottom;
                        row.Add(w.Text);
                    }
                    else
                    {
                        sb.AppendLine(string.Join(" ", row));
                        row.Clear();
                        row.Add(w.Text);
                        curY = w.BoundingBox.Bottom;
                    }
                }
                
                if (row.Count > 0) sb.AppendLine(string.Join(" ", row));
                return CleanHeadersFooters(sb.ToString());
            }
            
            // two-column: left then right
            var combined = (leftText + "\n" + rightText).Trim();
            return CleanHeadersFooters(combined);
        }

        private static string CleanHeadersFooters(string text)
        {
            // Remove common print headers/footers: site URLs, “print”, page x/y, timestamps
            var lines = text.Replace("\r\n","\n").Replace("\r","\n").Split('\n')
                .Select(l => l.TrimEnd()).ToList();

            bool IsJunk(string l) =>
                string.IsNullOrWhiteSpace(l)
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"https?://")
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"(?i)\bprint\b")
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*\d+\s*/\s*\d+\s*$") // "1/2"
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"^\s*\d{1,2}/\d{1,2}/\d{2,4}") // date
                || System.Text.RegularExpressions.Regex.IsMatch(l, @"(?i)\ballrecipes\.com\b");

            var filtered = lines.Where(l => !IsJunk(l)).ToList();
            // Collapse stray duplicate blank-lines; final trim
            return string.Join("\n", filtered).Trim();
        }





    }
}
