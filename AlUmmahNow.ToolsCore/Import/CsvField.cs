using System.Globalization;

namespace AlUmmahNow.ToolsCore.Import;

/// <summary>
/// RFC 4180 CSV field escaping with spreadsheet formula-injection neutralization:
/// fields starting with (or with leading whitespace/newlines before) <c>=</c>, <c>+</c>, <c>-</c> (unless a finite
/// number), <c>@</c>, <c>\t</c>, <c>\r</c>, or <c>\n</c> are prefixed with a single quote.
/// </summary>
public static class CsvField
{
    public static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        if (IsFormulaTrigger(text)) text = "'" + text;
        var needsQuotes = text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            || (text.Length > 0 && (text[0] == ' ' || text[^1] == ' '));
        return needsQuotes ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static bool IsFormulaTrigger(string text)
    {
        if (text.Length == 0) return false;
        var first = text[0];
        if (first is '=' or '+' or '@' or '\t' or '\r' or '\n') return true;
        if (first == '-') return !IsFiniteNumber(text);
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0) return false;
        var lead = trimmed[0];
        if (lead is '=' or '+' or '@') return true;
        return lead == '-' && !IsFiniteNumber(trimmed);
    }

    private static bool IsFiniteNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number);
}
