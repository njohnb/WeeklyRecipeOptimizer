// /src/Services/HtmlImportService.cs
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using static RecipeOptimizer.Services.RecipeTextSplitter;

namespace RecipeOptimizer.Services;

public sealed class HtmlImportService : IHtmlImportService
{
    private readonly IWebPageFetcher _fetcher;
    private readonly IDebugDumpService _dump;

    public HtmlImportService(IWebPageFetcher fetcher, IDebugDumpService dump)
    {
        _fetcher = fetcher;
        _dump = dump;
    }

    public async Task<PdfImportResult> ImportFromUrlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var html = await _fetcher.GetAsync(url, ct);
            if (string.IsNullOrWhiteSpace(html))
                return new PdfImportResult(false, null, null, null, null, null, "Empty response");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Try DOM-structured extraction first
            var domOk =  TryExtractFromDom(doc, out var title, out var servings, out var ingredients, out var steps, out var equipment);

            // Fallback: plain-text + splitter
            if (!domOk)
            {
                var text = NormalizeWhitespace(doc.DocumentNode.InnerText ?? "");
                if (string.IsNullOrWhiteSpace(text))
                    return new PdfImportResult(false, null, null, null, null, null, "No text found in HTML.");

                (title, servings, ingredients, steps, equipment) = SplitRecipeText(text);
            }

            var raw = ExtractAllText(html); // for the log

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
            await _dump.AppendAsync("HTML Import", dump, ct);
#endif

            return new PdfImportResult(true, title, servings, ingredients, steps, equipment, null);
        }
        catch (Exception ex)
        {
            return new PdfImportResult(false, null, null, null, null, null, $"HTML import failed: {ex.Message}");
        }
    }

    // ---------- DOM-first extraction ----------
    private static bool TryExtractFromDom(HtmlDocument doc,
        out string title, out string servings, out string ingredients, out string steps, out string equipment)
    {
        title = ExtractTitle(doc);
        servings = ExtractServings(doc);

        // Ingredients via <li> under ingredients containers, or itemprop
        var ingredientLines = SelectIngredientLines(doc);
        // Steps via <li> under instructions containers, or itemprop
        var stepLines = SelectInstructionLines(doc);
        // Equipment via nearby header "Equipment/Tools" then following list
        var equipmentLines = SelectEquipmentLines(doc);

        // As a rescue: if ingredients are in a single blob under an "Ingredients" container, try to split by qty/unit
        if (ingredientLines.Count == 0)
        {
            var ingrBlob = SelectSingleBlobUnder(doc, "ingredients");
            if (!string.IsNullOrWhiteSpace(ingrBlob))
            {
                ingredientLines = SplitInlineIngredients(ingrBlob);
            }
        }

        ingredients = JoinLines(ingredientLines);
        steps = JoinLines(stepLines);
        equipment = JoinLines(equipmentLines);

        // Consider it a success if we got at least one of the main sections (ingredients or steps)
        // Title alone is common, but we want to fall back if nothing else parsed.
        return !(string.IsNullOrWhiteSpace(ingredients) && string.IsNullOrWhiteSpace(steps));
    }

    private static string ExtractTitle(HtmlDocument doc)
    {
        // 1) h1 with recipe-like classes
        var h1 = doc.DocumentNode.SelectSingleNode(
            "//h1[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'title') or contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'recipe') or contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'post')]")
                 ?? doc.DocumentNode.SelectSingleNode("//h1");
        if (h1 != null)
            return CleanText(h1.InnerText);

        // 2) schema.org
        var itemName = doc.DocumentNode.SelectSingleNode("//*[@itemprop='name']");
        if (itemName != null)
            return CleanText(itemName.InnerText);

        // 3) document title
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
            return CleanText(titleNode.InnerText);

        // Fallback
        return "Imported Recipe";
    }

    private static string ExtractServings(HtmlDocument doc)
    {
        // Common schema.org
        var yieldNode = doc.DocumentNode.SelectSingleNode("//*[@itemprop='recipeYield']");
        if (yieldNode != null)
            return ExtractFirstInteger(CleanText(yieldNode.InnerText));

        // Class hints
        var sv = doc.DocumentNode.SelectSingleNode(
            "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'serving') or contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'yield')]");
        if (sv != null)
        {
            var txt = CleanText(sv.InnerText);
            // Examples: "Servings 6", "Yield: 4 servings"
            var m = Regex.Match(txt, @"(?i)\b(serves?|servings?|yield)\D+(\d+)");
            if (m.Success) return m.Groups[2].Value;
            return ExtractFirstInteger(txt);
        }

        // Label/value pattern
        var label = doc.DocumentNode.SelectSingleNode("//*[contains(., 'Servings') or contains(., 'Yield')]");
        if (label != null)
        {
            var txt = CleanText(label.InnerText);
            var m = Regex.Match(txt, @"(?i)\b(serves?|servings?|yield)\D+(\d+)");
            if (m.Success) return m.Groups[2].Value;
        }

        return string.Empty;
    }

    private static List<string> SelectIngredientLines(HtmlDocument doc)
    {
        // schema.org recipeIngredient
        var list = doc.DocumentNode.SelectNodes("//*[@itemprop='recipeIngredient']");
        if (list != null && list.Count > 0)
            return list.Select(n => CleanText(n.InnerText)).Where(s => s.Length > 0).ToList();

        // UL/OL lists under containers with "ingredient" in class/id
        var ulLis = doc.DocumentNode.SelectNodes(
            "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'ingredient') or contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'ingredient')]//li");
        if (ulLis != null && ulLis.Count > 0)
            return ulLis.Select(n => CleanText(n.InnerText)).Where(s => s.Length > 0).ToList();

        return new List<string>();
    }

    private static List<string> SelectInstructionLines(HtmlDocument doc)
    {
        // schema.org recipeInstructions can be text, HowToStep, or nodes
        var steps = new List<string>();
        var instNodes = doc.DocumentNode.SelectNodes(
            "//*[@itemprop='recipeInstructions']//*[@itemprop='text'] | //*[@itemprop='recipeInstructions']//li | //*[@itemprop='recipeInstructions' and not(descendant::li)]");
        if (instNodes != null)
        {
            foreach (var n in instNodes)
            {
                // If it's a container without <li>, treat its inner text as one step chunk
                if (n.Name.ToLower() != "li" && n.SelectNodes(".//li") == null)
                {
                    var txt = CleanText(n.InnerText);
                    if (!string.IsNullOrWhiteSpace(txt)) steps.AddRange(SplitIntoStepLines(txt));
                }
                else
                {
                    var txt = CleanText(n.InnerText);
                    if (!string.IsNullOrWhiteSpace(txt)) steps.Add(txt);
                }
            }
            if (steps.Count > 0)
                return steps;
        }

        // UL/OL lists under containers with instructions/directions/method/steps
        var ulLis = doc.DocumentNode.SelectNodes(
            "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'instruction') or " +
            "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'direction') or " +
            "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'method') or " +
            "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'step') or " +
            "contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'instruction') or " +
            "contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'direction') or " +
            "contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'method') or " +
            "contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'step')]" +
            "//li");
        if (ulLis != null && ulLis.Count > 0)
            return ulLis.Select(n => CleanText(n.InnerText)).Where(s => s.Length > 0).ToList();

        return new List<string>();
    }

    private static List<string> SelectEquipmentLines(HtmlDocument doc)
    {
        // Look for a header that says Equipment/Tools, then grab the next list's <li>
        var hdr = doc.DocumentNode.SelectSingleNode(
            "//h2|//h3|//h4|//p|//strong[normalize-space(translate(.,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'))='equipment' or " +
            "contains(translate(.,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'equipment') or " +
            "contains(translate(.,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'tools')]");
        if (hdr != null)
        {
            var list = hdr.SelectSingleNode("./following::ul[1]") ?? hdr.SelectSingleNode("./following::ol[1]");
            if (list != null)
            {
                var lis = list.SelectNodes("./li");
                if (lis != null)
                    return lis.Select(n => CleanText(n.InnerText)).Where(s => s.Length > 0 && s.Length <= 64).Take(6).ToList();
            }
        }

        // Some sites put equipment under a container with “equipment/tools” class
        var eqLis = doc.DocumentNode.SelectNodes(
            "//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'equipment') or " +
            "contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'tools') or " +
            "contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'equipment') or " +
            "contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'tools')]" +
            "//li");
        if (eqLis != null && eqLis.Count > 0)
            return eqLis.Select(n => CleanText(n.InnerText)).Where(s => s.Length > 0 && s.Length <= 64).Take(6).ToList();

        return new List<string>();
    }

    private static string SelectSingleBlobUnder(HtmlDocument doc, string keyword)
    {
        var node = doc.DocumentNode.SelectSingleNode(
            $"//*[contains(translate(@class,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'{keyword}') or " +
            $"contains(translate(@id,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'{keyword}')]");
        if (node == null) return string.Empty;

        // If there are LIs, the list extractor should have caught it already; only take when it's a blob
        if (node.SelectNodes(".//li") != null) return string.Empty;

        var txt = CleanText(node.InnerText);
        return txt;
    }

    private static List<string> SplitInlineIngredients(string blob)
    {
        // Split on obvious quantity starts to simulate line breaks
        // e.g., "3 chicken breasts ... 1/2 cup flour ... 3 eggs, whisked ..."
        var lines = new List<string>();
        var rx = new Regex(@"(?=(?:^|\s)(?:\d+(?:\s+\d+/\d+)?|\d+/\d+|[¼½¾⅓⅔⅛⅜⅝⅞]))");
        var parts = rx.Split(blob).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        foreach (var p in parts)
            lines.Add(p);
        // If splitting produced only one line, don't return bogus results
        return lines.Count >= 2 ? lines : new List<string>();
    }

    private static IEnumerable<string> SplitIntoStepLines(string text)
    {
        // Split by sentences or forced breaks when there are clear markers (digits/bullets)
        var byLine = text.Replace("\r\n", "\n").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (byLine.Count > 1) return byLine;

        // Fallback: split on numbered/period patterns
        var rx = new Regex(@"(?=(?:^|\s)(?:\d+[.)]\s))");
        var parts = rx.Split(text).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return parts.Count > 1 ? parts : new[] { text.Trim() };
    }

    private static string JoinLines(List<string> lines) =>
        string.Join("\n", lines ?? new List<string>());

    private static string CleanText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var raw = HtmlEntity.DeEntitize(s);
        // Normalize whitespace similar to your splitter
        var t = raw.Replace("\0", "").Replace("\r\n", "\n").Replace("\r", "\n");
        t = Regex.Replace(t, @"[ \t]+\n", "\n");
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim();
    }

    private static string ExtractFirstInteger(string s)
    {
        var m = Regex.Match(s, @"\d+");
        return m.Success ? m.Value : string.Empty;
    }

    // ---------- Existing text-dump helper ----------
    private static string ExtractAllText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();

        var raw = doc.DocumentNode.InnerText ?? "";
        var s = raw.Replace("\0", "")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
        s = Regex.Replace(s, @"[ \t]+\n", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }
}
