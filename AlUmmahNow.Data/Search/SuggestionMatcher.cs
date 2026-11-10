using System.Globalization;
using System.Text;

namespace AlUmmahNow.Data.Search;

/// <summary>
/// Suggestions for a search box as the user types: the candidates that match the typed text,
/// best first (exact, then starts with, then a word starts with, then contains). Matching ignores
/// case, Arabic diacritics and tatweel, and folds alef, yeh, teh marbuta and hamza carriers, like
/// the platform's search (AlUmmahNow packages/shared-types search/normalize.ts), so "احمد" finds
/// "أَحمد". Used by the AutoSuggestBox behaviour in the importer (Helpers/AutoSuggest.cs).
/// </summary>
public static class SuggestionMatcher
{
    /// <summary>Suggestions shown at once: enough to choose from, few enough to read.</summary>
    public const int DefaultLimit = 8;

    public static IReadOnlyList<string> Suggest(IEnumerable<string?> candidates, string? query, int limit = DefaultLimit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var distinct = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var q = Normalize(query);
        if (q.Length == 0) return distinct.Take(limit).ToList();

        return distinct
            .Select((text, index) => (text, index, rank: Rank(Normalize(text), q)))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.index)
            .Take(limit)
            .Select(x => x.text)
            .ToList();
    }

    /// <summary>0 exact, 1 starts with, 2 a word starts with, 3 contains, -1 no match.</summary>
    private static int Rank(string candidate, string query)
    {
        if (candidate == query) return 0;
        if (candidate.StartsWith(query, StringComparison.Ordinal)) return 1;
        var at = candidate.IndexOf(query, StringComparison.Ordinal);
        if (at < 0) return -1;
        while (at > 0)
        {
            if (!char.IsLetterOrDigit(candidate[at - 1])) return 2;
            at = candidate.IndexOf(query, at + 1, StringComparison.Ordinal);
            if (at < 0) return 3;
        }
        return 2;
    }

    /// <summary>Lower case, no diacritics or tatweel, folded Arabic letter forms, single spaces.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var raw in text.Trim().Normalize(NormalizationForm.FormC))
        {
            var ch = raw;
            if (IsArabicMark(ch) || ch == 'ـ') continue;
            ch = ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' or 'ٲ' or 'ٳ' => 'ا',
                'ى' or 'ی' or 'ئ' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ک' => 'ك',
                _ => ch,
            };
            if (char.IsWhiteSpace(ch))
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
                continue;
            }
            space = false;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsArabicMark(char ch) =>
        (ch >= 'ً' && ch <= 'ٟ') || ch == 'ٰ' || (ch >= 'ۖ' && ch <= 'ۭ') ||
        CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark;
}
