using System.Globalization;
using System.Text;
using Windows.ApplicationModel.Resources;

namespace AlUmmahNow.VisualImporterUno.Shell;

/// <summary>
/// Interface text and numbers in the chosen language. Strings come from Strings/{en,ar}/Resources.resw. Numbers follow
/// the platform rule (owner, 2026-11-21): Arabic uses Arabic-Indic digits, never grouped, with «٫» as the only mark;
/// English groups thousands. Counts of 1,000 or more are compact (4.8K, «٤٫٨ ألف»), cut to one decimal, never rounded up.
/// </summary>
public static class Loc
{
    public static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    /// <summary>The string for a resource key; the key itself when it is missing, so a gap shows up in review.</summary>
    public static string Get(string key)
    {
        try
        {
            var value = ResourceLoader.GetForViewIndependentUse().GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    /// <summary>
    /// A resource string with placeholders; whole-number arguments are written as exact numbers. When the first argument is a
    /// whole number and the key has plural forms (Key_One, Key_Other, …), the form for that number is used.
    /// </summary>
    public static string Format(string key, params object?[] args)
    {
        key = PluralKey(key, args);
        var shown = args.Select(a => a switch
        {
            int i => Number(i),
            long l => Number(l),
            double d => Decimal(d),
            _ => a,
        }).ToArray();
        return string.Format(CultureInfo.CurrentUICulture, Get(key), shown);
    }

    /// <summary>A count phrase such as "3 channels" / «٣ قنوات», for use inside another string.</summary>
    public static string Plural(string key, long count) => Format(key, count);

    /// <summary>
    /// The plural category of a whole number. Arabic follows CLDR: 0 zero, 1 one, 2 two, 3–10 few and 11–99 many (by the last
    /// two digits), the rest other («١٠٠ مرئي»). English has one and other. Either language may add a Zero form.
    /// </summary>
    public static string PluralCategory(long n, bool arabic)
    {
        if (!arabic) return n == 1 ? "One" : "Other";
        var lastTwo = Math.Abs(n) % 100;
        return n switch
        {
            0 => "Zero",
            1 => "One",
            2 => "Two",
            _ when lastTwo is >= 3 and <= 10 => "Few",
            _ when lastTwo is >= 11 and <= 99 => "Many",
            _ => "Other",
        };
    }

    private static string PluralKey(string key, object?[] args)
    {
        if (args.Length == 0 || args[0] is not (int or long)) return key;
        var n = System.Convert.ToInt64(args[0]);
        string[] tries = n == 0 ? [key + "_Zero", key + "_" + PluralCategory(n, IsArabic), key + "_Other"] : [key + "_" + PluralCategory(n, IsArabic), key + "_Other"];
        foreach (var candidate in tries)
            if (Get(candidate) != candidate) return candidate;
        return key;
    }

    /// <summary>
    /// Interface text with a raw detail (an error message, a file or channel name) on a line of its own. Uno lays out a line
    /// left to right and only reverses runs of right-to-left text, so Arabic sharing a line with a Latin name comes out in
    /// the wrong order; on separate lines each reads correctly.
    /// </summary>
    public static string WithDetail(string text, string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? text : text + "\n" + detail.Trim();

    /// <summary>A resource string followed by a raw detail on its own line.</summary>
    public static string GetWithDetail(string key, string? detail) => WithDetail(Get(key), detail);

    /// <summary>An exact whole number: "4,887" in English, «٤٨٨٧» in Arabic.</summary>
    public static string Number(long value) =>
        IsArabic ? ToArabicDigits(value.ToString(CultureInfo.InvariantCulture)) : value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>A number with up to <paramref name="places"/> decimals: "12.5" in English, «١٢٫٥» in Arabic.</summary>
    public static string Decimal(double value, int places = 1)
    {
        var text = Math.Round(value, places).ToString("0." + new string('#', places), CultureInfo.InvariantCulture);
        if (!IsArabic) return text;
        return ToArabicDigits(text.Replace('.', '٫'));
    }

    /// <summary>A count, compact from 1,000: "4.8K" / «٤٫٨ ألف», "1.2M" / «١٫٢ م».</summary>
    public static string Count(long value)
    {
        if (Math.Abs(value) < 1000) return Number(value);
        var (scaled, unit) = Math.Abs(value) switch
        {
            >= 1_000_000_000 => (value / 1_000_000_000d, IsArabic ? " مليار" : "B"),
            >= 1_000_000 => (value / 1_000_000d, IsArabic ? " م" : "M"),
            _ => (value / 1000d, IsArabic ? " ألف" : "K"),
        };
        var cut = Math.Truncate(scaled * 10) / 10;
        return Decimal(cut) + unit;
    }

    /// <summary>A percentage of 0 to 100: "42%" / «٤٢٪».</summary>
    public static string Percent(double value) => IsArabic ? Decimal(value, 0) + "٪" : Decimal(value, 0) + "%";

    /// <summary>A time of day in the device's zone: "2:05 PM" / «٢:٠٥ م».</summary>
    public static string Time(DateTime local)
    {
        var text = local.ToString("t", CultureInfo.CurrentUICulture);
        return IsArabic ? ToArabicDigits(text) : text;
    }

    /// <summary>A date and time in the device's zone, digits per language.</summary>
    public static string When(DateTime local)
    {
        var today = DateTime.Today;
        var time = local.ToString("t", CultureInfo.CurrentUICulture);
        var text = local.Date == today ? Format("When_Today", time)
            : local.Date == today.AddDays(-1) ? Format("When_Yesterday", time)
            : Format("When_Date", local.ToString(local.Year == today.Year ? "d MMM" : "d MMM yyyy", CultureInfo.CurrentUICulture), time);
        return IsArabic ? ToArabicDigits(text) : text;
    }

    /// <summary>A file size: "1.4 GB" / «١٫٤ غيغابايت».</summary>
    public static string Bytes(long bytes)
    {
        string[] en = { "B", "KB", "MB", "GB", "TB" };
        string[] ar = { "بايت", "كيلوبايت", "ميغابايت", "غيغابايت", "تيرابايت" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < en.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return Decimal(size) + " " + (IsArabic ? ar[unit] : en[unit]);
    }

    /// <summary>A duration such as "1h 5m" / «١ س ٥ د».</summary>
    public static string Duration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return Format("Duration_HoursMinutes", (int)span.TotalHours, span.Minutes);
        if (span.TotalMinutes >= 1) return Format("Duration_MinutesSeconds", span.Minutes, span.Seconds);
        return Format("Duration_Seconds", Math.Max(0, span.Seconds));
    }

    private static readonly System.Text.RegularExpressions.Regex DigitRun = new("[0-9][0-9٫:]*[0-9]");

    /// <summary>Western digits to Arabic-Indic; digits already Arabic-Indic are left as they are.</summary>
    public static string ToArabicDigits(string text)
    {
#if HAS_UNO
        // Uno's Skia text lays a line out left to right and flips each right-to-left run character by character, and it
        // counts Arabic-Indic digits as right-to-left, so «١٤» would show as «٤١». Each number is stored back to front so
        // the flip puts it the right way round. Windows draws text natively and needs nothing.
        text = DigitRun.Replace(text, m => Convert(new string(m.Value.Reverse().ToArray())));
#endif
        return Convert(text);
    }

    private static string Convert(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text) builder.Append(c is >= '0' and <= '9' ? (char)('٠' + (c - '0')) : c);
        return builder.ToString();
    }
}
