// /src/Services/PdfImportService.cs
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using PdfPage = UglyToad.PdfPig.Content.Page;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

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

        private static string NormalizeWhitespace(string input)
        {
            // Keep line breaks; trim excess spaces/newlines but don’t collapse lines into one.
            var s = input.Replace("\0","")
                .Replace("\r\n", "\n").Replace("\r", "\n");
            // remove trailing spaces per line
            s = Regex.Replace(s, @"[ \t]+\n", "\n");
            // collapse >2 blank lines to exactly 2
            s = Regex.Replace(s, @"\n{3,}", "\n\n");
            return s.Trim();
        }

        // Super-light heuristic splitter. Phase 2 will do real parsing.
        private static (string Title, string Servings, string Ingredients, string Steps, string Equipment) SplitRecipeText(string t)
        {
            // Normalize → split → trim
            var allLines = t.Replace("\r\n", "\n").Replace("\r", "\n")
                .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            // Title is first non-empty
            var title = allLines.FirstOrDefault() ?? "Imported Recipe";

            // Extract meta line (if present)
            var metaLine = allLines.FirstOrDefault(IsMetaLine) ?? "";
            string servings = "";
            
            
            int? prepMin = null, cookMin = null, totalMin = null;

            if (!string.IsNullOrEmpty(metaLine))
            {
                // Servings: "yield: 6 SERVINGS", "serves 4", etc.
                var sMatch = Regex.Match(metaLine, @"(?i)(serves|servings?|yield)\D+(\d+)");
                if (sMatch.Success) servings = sMatch.Groups[2].Value;

                // Times
                var prep = Regex.Match(metaLine, @"(?i)(prep|preparation)\s*:\s*([^\s].*?)(?=\s+\w+:|$)");
                var cook = Regex.Match(metaLine, @"(?i)(cook)\s*:\s*([^\s].*?)(?=\s+\w+:|$)");
                var total = Regex.Match(metaLine, @"(?i)(total)\s*:\s*([^\s].*?)(?=\s+\w+:|$)");
                if (prep.Success)  prepMin  = ParseDurationMinutes(prep.Groups[1].Value);
                if (cook.Success)  cookMin  = ParseDurationMinutes(cook.Groups[1].Value);
                if (total.Success) totalMin = ParseDurationMinutes(total.Groups[1].Value);
                // If you later add Recipe.TotalTimeMin, you can pass totalMin upstream.
            }

            // Build the working list of lines for sectioning, skipping meta and junk
            var lines = allLines
                .Where(l => !IsMetaLine(l))                   // <-- skip meta row
                .Where(l => !IsJunkLine(l))                   // <-- from a previous snippet
                .ToList();
            
            RemoveHeaderBanner(lines);
            
            // Heading indices (maybe “banner” headings not tied to the content position)
            int idxIngredientsHdr = FindIndex(lines, "ingredients?");
            int idxDirectionsHdr  = FindIndex(lines, "(directions?|instructions?|method?|steps|preparation|cooking|cooking steps)");
            int idxEquipmentHdr   = FindIndex(lines, "(equipment|tools?)");

            
            // 1) INGREDIENTS: pick the longest contiguous ingredient-like block in the whole doc
            var bestIngr = FindMaxIngredientRun(lines, 0, lines.Count);
            
            string ingredients = "";
            string steps = "";
            string equipment = "";
                    
            if (bestIngr.start >= 0 && bestIngr.len >= 2)
                ingredients = string.Join("\n", lines.Skip(bestIngr.start).Take(bestIngr.len));

            // 2) STEPS: first step-like line AFTER ingredients block; else slice after a directions header (not in banner)
            int idxFirstStep = -1;
            if (bestIngr.start >= 0)
            {
                int searchFrom = bestIngr.start + bestIngr.len;
                for (int i = searchFrom; i < lines.Count; i++)
                {
                    if (IsStepLine(lines[i])) { idxFirstStep = i; break; }
                }
            }

            if (idxFirstStep >= 0)
            {
                var stepBlock = TrimAfterNotes(lines.Skip(idxFirstStep));
                steps = string.Join("\n", stepBlock);
            }
            else if (idxDirectionsHdr >= 0)
            {
                // Slice from directions header to the next header, then trim at NOTES/URLs
                string SliceSection(List<string> _lines, int startIndex, params int[] otherHeadings)
                {
                    if (startIndex < 0) return "";
                    var next = otherHeadings.Where(i => i >= 0 && i > startIndex).DefaultIfEmpty(_lines.Count).Min();
                    var start = startIndex + 1;
                    var end = Math.Max(start - 1, next - 1);
                    return (end >= start) ? string.Join("\n", _lines.Skip(start).Take(end - start + 1)) : "";
                }
                var rawSteps = SliceSection(lines, idxDirectionsHdr, idxIngredientsHdr, idxEquipmentHdr);
                var stepBlock = TrimAfterNotes(rawSteps.Split('\n'));
                steps = string.Join("\n", stepBlock);
            }

            // 3) EQUIPMENT:
            //    Preferred: short lines BETWEEN ingredients and steps that look like tools.
            if (!string.IsNullOrWhiteSpace(ingredients) && !string.IsNullOrWhiteSpace(steps))
            {
                int ingrEnd = bestIngr.start + bestIngr.len - 1;
                int gapEndExclusive = (idxFirstStep >= 0 ? idxFirstStep : lines.Count);
                if (gapEndExclusive > ingrEnd + 1)
                {
                    var gap = lines.Skip(ingrEnd + 1).Take(gapEndExclusive - (ingrEnd + 1))
                                   .Where(IsLikelyEquipmentLine).Take(4).ToList();
                    if (gap.Count is >= 1 and <= 4)
                        equipment = string.Join("\n", gap);
                }
            }

            // Fallback: if still empty, try a tight slice after EQUIPMENT header (but not banner),
            // stop at next header or when ingredient/step-like lines start appearing.
            if (string.IsNullOrWhiteSpace(equipment) && idxEquipmentHdr >= 0)
            {
                var list = new List<string>();
                for (int i = idxEquipmentHdr + 1; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (IsHeadingLine(l)) break;
                    if (IsIngredientLine(l) || IsStepLine(l)) break;
                    if (IsLikelyEquipmentLine(l)) list.Add(l);
                    else break; // keep it tight
                    if (list.Count >= 4) break;
                }
                if (list.Count > 0) equipment = string.Join("\n", list);
            }

            // Final sanity cleanup: if ingredients still include steps, prune them out
            if (!string.IsNullOrWhiteSpace(ingredients))
            {
                var pruned = ingredients.Split('\n').Where(l => !IsStepLine(l)).ToList();
                if (pruned.Count >= 2) ingredients = string.Join("\n", pruned);
            }

            return (title, servings, ingredients, steps, equipment);
        }

        private static void RemoveHeaderBanner(List<string> lines)
        {
            // Remove a top “banner” made of consecutive pure heading lines (e.g., INGREDIENTS / EQUIPMENT / INSTRUCTIONS)
            // Only consider the very first ~8 lines.
            int i = 0; int count = 0;
            while (i < Math.Min(lines.Count, 8) && IsHeadingLine(lines[i]))
            {
                count++; i++;
            }
            // Require at least 2 consecutive headings to consider it a banner
            if (count >= 2)
                lines.RemoveRange(0, count);
        }
        
        private static (int start, int len) FindMaxIngredientRun(List<string> lines, int startIndexInclusive, int endIndexExclusive)
        {
            var best = (start: -1, len: 0);
            int runStart = -1, runLen = 0;

            for (int i = startIndexInclusive; i < endIndexExclusive; i++)
            {
                var l = lines[i];
                if (IsHeadingLine(l) || IsJunkLine(l))
                {
                    if (runLen > 0 && runLen > best.len) best = (runStart, runLen);
                    runStart = -1; runLen = 0;
                    continue;
                }

                if (IsIngredientLine(l))
                {
                    if (runStart < 0) runStart = i;
                    runLen++;
                }
                else
                {
                    if (runLen > 0 && runLen > best.len) best = (runStart, runLen);
                    runStart = -1; runLen = 0;
                }
            }
            if (runLen > 0 && runLen > best.len) best = (runStart, runLen);
            return best;
        }
        
        // Short, non-ingredient, non-step lines (likely cookware/tools)
        private static bool IsLikelyEquipmentLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (IsHeadingLine(line) || IsJunkLine(line)) return false;
            if (IsIngredientLine(line) || IsStepLine(line)) return false;

            // Prefer short-ish lines without punctuation noise
            if (line.Length > 64) return false;

            // Mild cookware lexicon helps but keep it permissive
            var cookRx = new Regex(@"(?i)\b(pan|skillet|pot|sheet|grill|griddle|baking|tray|dish|bowl|whisk|spatula|tongs|knife|foil|paper|rack|oven|cast iron)\b");
            return cookRx.IsMatch(line) || !Regex.IsMatch(line, @"[.!?]$");
        }
        
        // Stop step capture at NOTES, URLs, or social lines
        private static List<string> TrimAfterNotes(IEnumerable<string> input)
        {
            var stopRx = new Regex(@"(?i)^(notes\b|this .* recipe .*|https?://)", RegexOptions.IgnoreCase);
            var list = new List<string>();
            foreach (var l in input)
            {
                if (stopRx.IsMatch(l)) break;
                list.Add(l);
            }
            return list;
        }
        
        // Heuristics
        private static bool IsHeadingLine(string line) =>
            Regex.IsMatch(line, @"(?i)^\s*(ingredients?|equipment|tools?|instructions?|directions?|method|steps|notes|yield|serves|servings?)\s*:?\s*$");

        private static bool IsJunkLine(string line)
        {
            // Typical garbage separators or stray bullets from OCR/extractor
            if (Regex.IsMatch(line, @"^[\uFFFD�•\-\–\—\*\. ]{1,}$")) return true; // U+FFFD replacement or just bullets/dashes
            if (Regex.IsMatch(line, @"^\uFEFF$")) return true; // BOM
            return false;
        }

        // members for stepline detection
        private static readonly Regex RxNumberedPrefix = new(@"^\s*\d+\s*(?:[.)]\s*)?", RegexOptions.Compiled);
        // “Looks like a quantity start”: 1, 1 1/2, ½, 2–3, etc.
        private static readonly Regex RxQtyStart = new(
            @"^\s*(?:\d+(?:\s+\d+\/\d+)?|\d+\/\d+|[¼½¾⅓⅔⅛⅜⅝⅞])\b",
            RegexOptions.Compiled);
        // Common cooking units at the **start** of a token
        private static readonly Regex RxUnitStart = new(
            @"(?i)^(?:cup|cups|tbsp|tablespoons?|tsp|teaspoons?|g|grams?|kg|kilograms?|ml|milli?liters?|l|liters?|oz|ounces?|lb|lbs|pounds?|clove|cloves|slice|slices|can|cans|package|packages|stick|sticks|pinch|dash|sprig|bunch)\b",
            RegexOptions.Compiled);
        // Require a verb for bullet steps
        private static readonly Regex RxBulletVerb = new(
            @"^\s*[•\-\–\—]\s+(?:Heat|Add|Stir|Bake|Combine|Serve|Wrap|Slice|Preheat|Whisk|Cook|Mix|Saute|Sauté|Simmer|Bring|Reduce|Pour|Spread|Season|Fold|Arrange)\b",
            RegexOptions.Compiled);
        // Imperative (non-numbered) step start
        private static readonly Regex RxImperativeStart = new(
            @"^(?:Heat|Add|Stir|Bake|Combine|Serve|Wrap|Slice|Preheat|Whisk|Cook|Mix|Saute|Sauté|Simmer|Bring|Reduce|Pour|Spread|Season|Fold|Arrange)\b",
            RegexOptions.Compiled);
        // Capitalized-word fallback for numbered steps like "1 Wrap ..." (not a unit/qty)
        private static readonly Regex RxCapitalWord = new(@"^[A-Z][a-z]", RegexOptions.Compiled);
        private static bool IsStepLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            // Never call it a step if it looks like a heading or ingredient.
            if (IsHeadingLine(line) || IsIngredientLine(line)) return false;

            // --- Numbered steps ---
            var m = RxNumberedPrefix.Match(line);
            if (m.Success)
            {
                var rest = line[m.Length..].TrimStart();

                // If the remainder *starts* like a quantity/fraction/unit, it's an ingredient, not a step.
                if (RxQtyStart.IsMatch(rest) || RxUnitStart.IsMatch(rest))
                    return false;

                // Good numbered step if it starts with an imperative verb or at least a capitalized word (e.g., "1 Wrap ...")
                if (RxImperativeStart.IsMatch(rest) || RxCapitalWord.IsMatch(rest))
                    return true;

                // Otherwise, not a step.
                return false;
            }

            // --- Bulleted steps: require a verb after the bullet ---
            if (RxBulletVerb.IsMatch(line))
                return true;

            // --- Imperative (non-numbered, non-bulleted) steps ---
            if (RxImperativeStart.IsMatch(line))
                return true;

            return false;
        }
        // Keep the public IsIngredientLine wrapper that calls the internal with guards.
        private static bool IsIngredientLine(string line)
        {
            // Reject lines that clearly look like numbered steps:
            // e.g., "1 Wrap ...", "2 Heat ...", "3 Add ..."
            if (Regex.IsMatch(line, @"^\s*\d+\s+[A-Z][a-z]")) return false;

            return IsIngredientLine_Internal(line);
        }
        private static bool IsIngredientLine_Internal(string line)
        {
            // Bullets
            if (line.StartsWith("- ") || line.StartsWith("• ")) return true;

            // Accept:
            //  - "1 1/2 cups ..."
            //  - "½ cup ..."        
            //  - "1 ½ pounds ..."
            //  - "2–3 tbsp ..."
            //  - "1 cup ..." (qty + optional unit + name)
            var asciiFrac   = @"\d+\s+\d+/\d+";                 // 1 1/2
            var unicodeOnly = @"[¼½¾⅓⅔⅛⅜⅝⅞]";                   // ½
            var digitUni    = $@"\d+\s*{unicodeOnly}";          // 1 ½
            var range       = @"\d+\s*[–-]\s*\d+";              // 2–3
            var plainNum    = @"\d+(?:[./]\d+)?";               // 1 or 1.5

            var qty    = $"(?:{asciiFrac}|{digitUni}|{unicodeOnly}|{range}|{plainNum})";
            
            // Known unit-ish words (loose but excludes "Wrap/Heat/Serve").
            var unit = @"(?:
                cups?|cup|tablespoons?|tbsp|teaspoons?|tsp|
                pounds?|lbs?|oz|ounces?|
                grams?|g|kilograms?|kg|
                milliliters?|ml|liters?|l|
                cloves?|heads?|cans?|packages?|packets?|sticks?|sprigs?|bunch(?:es)?|
                pears?|onions?|seeds?|oil|sauce|ginger|garlic|gochujang|pear|steak
            )";
            
            if (Regex.IsMatch(line, $@"^\s*{qty}\s+(?:{unit}\b|[a-z].+)",
                    RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace))
                return true;
            
            // "salt, to taste", "optional"
            if (Regex.IsMatch(line, @"(?i)\b(to taste|optional)\b") && !IsHeadingLine(line))
                return true;

            return false;
        }


        private static bool IsMetaLine(string line) =>
            Regex.IsMatch(line, @"(?i)\b(yield|serves?|servings?)\b.*\b(prep|cook|total)\b")
            || Regex.IsMatch(line, @"(?i)\b(prep|cook|total)\b.*\b(yield|serves?|servings?)\b");

        // Optional: parse "2 HOURS 45 MINUTES" or "15 MINUTES" → minutes
        private static int ParseDurationMinutes(string text)
        {
            int minutes = 0;
            var h = Regex.Match(text, @"(?i)(\d+)\s*hour");
            var m = Regex.Match(text, @"(?i)(\d+)\s*min");
            if (h.Success) minutes += int.Parse(h.Groups[1].Value) * 60;
            if (m.Success) minutes += int.Parse(m.Groups[1].Value);
            return minutes;
        }
        
        private static int FindIndex(List<string> lines, string pattern)
        {
            var rx = new Regex(@"(?i)^\s*" + pattern + @"\s*:?\s*$");
            for (int i = 0; i < lines.Count; i++)
                if (rx.IsMatch(lines[i])) return i;
            return -1;
        }
    }
}
