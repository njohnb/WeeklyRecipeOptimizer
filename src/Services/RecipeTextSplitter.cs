using System.Text.RegularExpressions;

namespace RecipeOptimizer.Services;

public class RecipeTextSplitter
{
    public static string NormalizeWhitespace(string input)
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
        public static (string Title, string Servings, string Ingredients, string Steps, string Equipment) SplitRecipeText(string t)
        {
            // Normalize → split → trim
            var allLines = t.Replace("\r\n", "\n").Replace("\r", "\n")
                .Split('\n').Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Where(l => !IsPageMarker(l)).ToList();

            // Title is first non-empty
            var title = allLines.FirstOrDefault(l => !IsHeadingLine(l)) ?? "Imported Recipe";

            // Extract meta line (if present)
            var metaLine = allLines.FirstOrDefault(IsMetaLine) ?? "";
            string servings = "";
            
            
            int? prepMin = null, cookMin = null, totalMin = null;

            if (!string.IsNullOrEmpty(metaLine))
            {
                // Servings: "yield: 6 SERVINGS", "serves 4", etc.
                var sMatch = Regex.Match(metaLine, @"(?i)\b(serves|servings?|yield)\D+(\d+)\b");
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
                .Where(l => !IsHeadingLine(l))                
                .ToList();
            
            RemoveHeaderBanner(lines);
            
            // Heading indices (maybe “banner” headings not tied to the content position)
            int idxIngredientsHdr = FindIndex(lines, "ingredients?");
            int idxDirectionsHdr  = FindIndex(lines, "(directions?|instructions?|method?|steps|preparation|cooking|cooking steps)");
            int idxEquipmentHdr   = FindIndex(lines, "(equipment|tools?)");

            
            // 1) INGREDIENTS: pick the longest contiguous ingredient-like block in the whole doc
            var bestIngr = FindMaxIngredientRun(lines, 0, lines.Count);
            
            string ingredients = "";
                    
            if (bestIngr.start >= 0 && bestIngr.len >= 2)
                ingredients = string.Join("\n", lines.Skip(bestIngr.start).Take(bestIngr.len));

            if (!string.IsNullOrWhiteSpace(ingredients))
            {
                // tag sublists using original "lines"
                ingredients = TagIngredientSublists(ingredients, lines);
            }
            
            // find steps before and after ingredients (in case of page breaks)
            var stepsList = new List<string>();
            
            // (Pre-ingredients): top -> just before ingredients head/block
            int preEnd = (bestIngr.start > 0 ? bestIngr.start : (idxIngredientsHdr >= 0 ? idxIngredientsHdr : lines.Count));
            if (preEnd > 0)
            {
                var pre = CaptureStepsInRange(lines, 0, preEnd);
                if(pre.Count > 0) stepsList.AddRange(pre);
            }
            
            // (B) first step-like line AFTER ingredients block; else slice after a directions header (not in banner))
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
                var post = CaptureStepsInRange(lines, idxFirstStep, lines.Count);
                if (post.Any()) stepsList.AddRange(post);
            }
            else
            {
                int idxStageStart = FindIndex(
                    lines,
                    "(?:^|\\b)(instructions?|directions?|method|prep\\s*work|make\\s*the\\s*meat\\s*sauce|preheat.*noodles|assemble|bake|cooking(?:\\s*steps)?)\\b"
                );

                // Use whichever header we found (classic directions or stage header)
                int startHdr = (idxDirectionsHdr >= 0) ? idxDirectionsHdr : idxStageStart;
                if (startHdr >= 0)
                {
                    // local helper identical to what you had
                    string SliceSection(List<string> _lines, int startIndex, params int[] otherHeadings)
                    {
                        if (startIndex < 0) return "";
                        var next = otherHeadings.Where(i => i >= 0 && i > startIndex).DefaultIfEmpty(_lines.Count).Min();
                        var start = startIndex + 1;
                        var end = Math.Max(start - 1, next - 1);
                        return (end >= start) ? string.Join("\n", _lines.Skip(start).Take(end - start + 1)) : "";
                    }

                    var raw = SliceSection(lines, startHdr, idxIngredientsHdr, idxEquipmentHdr);
                    var post = TrimAfterNotes(raw.Split('\n'));
                    if (post.Any()) stepsList.AddRange(post);
                }
            }

            var steps = string.Join("\n", stepsList.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
            
            // tag sublists using original "lines"
            if (!string.IsNullOrWhiteSpace(steps))
            {
                steps = TagStepSublists(steps.Split('\n'));
            }
            
            // 3) EQUIPMENT:
            //    Preferred: short lines BETWEEN ingredients and steps that look like tools.
            string equipment = "";
            if (!string.IsNullOrWhiteSpace(ingredients) && !string.IsNullOrWhiteSpace(steps))
            {
                int ingrEnd = bestIngr.start + bestIngr.len - 1;
                int gapEndExclusive = (idxFirstStep >= 0 ? idxFirstStep : lines.Count);
                if (gapEndExclusive > ingrEnd + 1)
                {
                    var gap = lines.Skip(ingrEnd + 1).Take(gapEndExclusive - (ingrEnd + 1))
                        .Where(l => !IsStepLine(l))
                        .Where(IsLikelyEquipmentLine)
                        .Take(4).ToList();
                    if (gap.Count is >= 1 and <= 4)
                        equipment = string.Join("\n", gap);
                }
            }

          //// Fallback: if still empty, try a tight slice after EQUIPMENT header (but not banner),
          //// stop at next header or when ingredient/step-like lines start appearing.
          //if (string.IsNullOrWhiteSpace(equipment) && idxEquipmentHdr >= 0)
          //{
          //    var list = new List<string>();
          //    for (int i = idxEquipmentHdr + 1; i < lines.Count; i++)
          //    {
          //        var l = lines[i];
          //        if (IsHeadingLine(l)) break;
          //        if (IsIngredientLine(l) || IsStepLine(l)) break;
          //        if (IsLikelyEquipmentLine(l)) list.Add(l);
          //        else break; // keep it tight
          //        if (list.Count >= 4) break;
          //    }
          //    if (list.Count > 0) equipment = string.Join("\n", list);
          //}

            // Final sanity cleanup: if ingredients still include steps, prune them out
            if (!string.IsNullOrWhiteSpace(ingredients))
            {
                var pruned = ingredients.Split('\n').Where(l => !IsStepLine(l)).ToList();
                if (pruned.Count >= 2) ingredients = string.Join("\n", pruned);
            }

            return (title, servings, ingredients, steps, equipment);
        }
        private static bool IsPageMarker(string line) =>
            Regex.IsMatch(line, @"^=== PAGE \d+ (START|END) ===$");

        private static bool IsStepHeading(string line) =>
            Regex.IsMatch(line, @"(?i)^\s*Step\s+\d+\s*$");
        
        private static void RemoveHeaderBanner(List<string> lines)
        {
            // Remove a top “banner” made of consecutive pure heading lines (e.g., INGREDIENTS / EQUIPMENT / INSTRUCTIONS)
            // Only consider the very first ~8 lines.
            int i = 0; int count = 0;
            while (i < Math.Min(lines.Count, 8) && (IsHeadingLine(lines[i]) || IsStepHeading(lines[i])))
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
            int runStart = -1, runLen = 0, grace = 0;

            for (int i = startIndexInclusive; i < endIndexExclusive; i++)
            {
                var l = lines[i];
                
                if (IsIngredientLine(l))
                {
                    if (runStart < 0)
                    {
                        runStart = i;
                        runLen = 0;
                        grace = 0;
                    }
                    runLen++;
                    continue;
                }
                
                // allow up to 2 subsection headers within an ingredient run (e.g., "Cheese Filling", "Meat Sauce")
                if (runStart >= 0 && IsSubsectionHeading(l) && grace < 2)
                {
                    grace++;
                    continue;
                }

                // close current run if active
                if (runStart >= 0 && runLen > best.len) best = (runStart, runLen);
                runStart = -1; runLen = 0; grace = 0;
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
        
        private static string TagIngredientSublists(string ingredients, IEnumerable<string> sourceLines)
        {
            var src = sourceLines.ToList();
            if (string.IsNullOrWhiteSpace(ingredients)) return ingredients;
            var set = new HashSet<string>(ingredients.Split('\n'));

            // Prepend a tiny tag "[Cheese Filling]" right before the first ingredient that follows such a header.
            var output = new List<string>();
            string? currentTag = null;
            foreach (var l in src)
            {
                if (IsSubsectionHeading(l)) { currentTag = l.Trim(); continue; }
                if (!set.Contains(l)) continue;
                if (!string.IsNullOrEmpty(currentTag))
                {
                    output.Add($"[{currentTag}]");
                    currentTag = null;
                }
                output.Add(l);
            }
            return (output.Count > 0) ? string.Join("\n", output) : ingredients;
        }
        
        private static string TagStepSublists(IEnumerable<string> stepLines)
        {
            var output = new List<string>();
            string? currentTag = null;

            foreach (var l in stepLines)
            {
                if (IsSubsectionHeading(l))
                {
                    currentTag = l.Trim();
                    continue; // don’t keep the raw heading line
                }

                if (!string.IsNullOrEmpty(currentTag))
                {
                    output.Add($"[{currentTag}]");
                    currentTag = null;
                }

                output.Add(l);
            }
            return string.Join("\n", output);
        }
        
        // Stop step capture at NOTES, URLs, or social lines
        private static List<string> TrimAfterNotes(IEnumerable<string> input)
        {
            // keep only bona fide step lines, ignore any notes/tips/headers
            var all = input.ToList();
            
            // build a compact list of step lines
            var stepsOnly = new List<string>();
            foreach (var l in all)
            {
                if (IsStepLine(l)) stepsOnly.Add(l);
            }

            if (stepsOnly.Count == 0) return new List<string>(); // nothing step-like found

            return stepsOnly;
        }
        
        // Heuristics
        private static bool IsHeadingLine(string line) =>
            Regex.IsMatch(line, @"(?i)^\s*(ingredients?|equipment|tools?|instructions?|directions?|method|steps|notes|yield|serves|servings?|chef's notes|nutrition facts)\s*:?\s*$")
                || IsStepHeading(line);

        // 1) Recognize blog-style subsection headers
        private static bool IsSubsectionHeading(string line) =>
            Regex.IsMatch(line, @"(?i)^\s*(cheese\s*filling|meat\s*sauce|lasagna\s*noodles.*topping|prep\s*work|"
                                + @"make\s*the\s*meat\s*sauce|preheat.*noodles|assemble|bake|pro\s*tips?|notes?|nutrition\s*facts?)\s*:?\s*$");

        
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
        // Imperative (non-numbered) step start
        private static readonly Regex RxImperativeStart = new(
            @"(?i)^(?:Heat|Add|Stir|Bake|Combine|Serve|Wrap|Slice|Preheat|Whisk|Cook|Mix|Saute|Sauté|Simmer|Bring|Reduce|Pour|Spread|Season|Fold|Arrange|Gather|Place|Pound|Transfer|Beat|Layer|Make|Assemble)\b",
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
        private static bool IsMetaLine(string line)
        {
            // Require explicit time units to be present (min/hour) and forbid “Per serving/Nutrition”
            if (Regex.IsMatch(line, @"(?i)\b(per\s+serving|nutrition)\b")) return false;

            bool hasServings = Regex.IsMatch(line, @"(?i)\b(yield|serves?|servings?)\b");
            bool hasTime     = Regex.IsMatch(line, @"(?i)\b(prep|cook|total)\b")
                               && Regex.IsMatch(line, @"(?i)\b(min(ute)?s?|hour(s)?)\b");
            return hasServings && hasTime;
        }
        private static bool IsContinuationLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (IsHeadingLine(line) || IsJunkLine(line)) return false;
            if (IsIngredientLine(line)) return false;

            // If it looks like a new step, it's NOT a continuation.
            if (IsStepLine(line)) return false;

            // Short, linky, or pure “NOTES” lines are not continuations for steps;
            // TrimAfterNotes will also guard later.
            if (Regex.IsMatch(line, @"(?i)^(notes\b|pro\s*tips?\b|nutrition\b|tested by\b|https?://)")) return false;

            // Otherwise, treat as continuation (typical: "and gochujang. In a gallon size …")
            return true;
        }
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
        
        private static List<string> CaptureStepsInRange(List<string> lines, int start, int endExclusive)
        {
            var collected = new List<string>();
            
            var stopRx = new Regex(@"(?i)^(notes\b|pro\s*tips?\b|nutrition\b|tested by\b|https?://)");
            string? cur = null;
            
            for (int i = start; i < Math.Min(endExclusive, lines.Count); i++)
            {
                var l = lines[i];

                // Section boundary? Bail out (but flush current step first).
                if (Regex.IsMatch(l, @"(?i)^(ingredients?|equipment|nutrition|tested by)"))
                    break;

                // Skip explicit "Step N" label, but pull its content (next valid line) as a new step.
                if (IsStepHeading(l))
                {
                    // Flush previous step if pending
                    if (!string.IsNullOrWhiteSpace(cur)) { collected.Add(cur.Trim()); cur = null; }

                    int j = i + 1;
                    if (j < endExclusive && j < lines.Count)
                    {
                        var next = lines[j];
                        if (!IsHeadingLine(next) && !IsJunkLine(next) && !IsIngredientLine(next))
                        {
                            cur = next.Trim();
                            i = j; // we consumed the next line as content
                        }
                    }
                    continue;
                }

                // Hard stop at notes/urls AFTER we started steps
                if (stopRx.IsMatch(l))
                {
                    if (!string.IsNullOrWhiteSpace(cur)) collected.Add(cur.Trim());
                    break;
                }

                if (IsStepLine(l))
                {
                    // Starting a NEW step: flush previous buffer
                    if (!string.IsNullOrWhiteSpace(cur)) { collected.Add(cur.Trim()); cur = null; }
                    cur = l.Trim();
                    continue;
                }

                if (cur != null && IsContinuationLine(l))
                {
                    // Join continuation smartly: handle hyphenated line breaks
                    if (cur.EndsWith("-", StringComparison.Ordinal))
                    {
                        cur = cur[..^1] + l.TrimStart(); // glue without extra space
                    }
                    else
                    {
                        cur += " " + l.Trim();
                    }
                    continue;
                }

                // Non-step, non-continuation: ignore
            }
            
            if(!string.IsNullOrWhiteSpace(cur)) collected.Add(cur.Trim());
            
            // Final trim at notes/urls if any slipped through
            return TrimAfterNotes(collected);
        }
}