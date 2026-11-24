using System.Collections.Generic;
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

    /// <summary>
    /// The last two words of each sentence and of each line joined by a no-break space, so wrapped text never leaves a
    /// lone word on a line.
    /// </summary>
    public static string KeepLastWords(string text) =>
        string.Join("\n", text.Split('\n').Select(line => LastWords.Replace(line.TrimEnd(), "\u00A0")));

    // A space before the last word of a sentence (one ending in . ! ? ؟ :) or of the line
    private static readonly System.Text.RegularExpressions.Regex LastWords = new(@"(?<=\S) (?=\S+(?:[.!?؟:](?=\s)|$))");

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

    /// <summary>
    /// An app version: "2026.11.23" / «٢٠٢٦.١١.٢٣» (wiki AUN-APP-UPDATES §12). The dots stay dots (a version is not a
    /// decimal number). No bidi isolate marks: Uno's Skia text draws them as boxes; Arabic-Indic digits joined by dots
    /// already read as one number in an Arabic line.
    /// </summary>
    public static string Version(string? version)
    {
        if (string.IsNullOrEmpty(version) || !IsArabic) return version ?? string.Empty;
#if HAS_UNO
        // Uno's Skia text flips an Arabic line part by part, so the parts go in reversed (as ToArabicDigits does for digits)
        version = string.Join('.', version.Split('.').Reverse());
#endif
        return ToArabicDigits(version);
    }

    /// <summary>A percentage of 0 to 100: "42%" / «٤٢٪».</summary>
    public static string Percent(double value) => IsArabic ? Decimal(value, 0) + "٪" : Decimal(value, 0) + "%";

    /// <summary>Languages in a list: "Arabic, English" / «العربية والإنجليزية» (Arabic joins with «و», so «،» is left to separate groups).</summary>
    public static string Languages(IEnumerable<string> names) => string.Join(IsArabic ? " و" : ", ", names);

    /// <summary>Groups on one line: "captions: Arabic, English · chapters" / «ترجمات: العربية والإنجليزية؛ فصول».</summary>
    /// <summary>A short list ("5 sources · 2 need attention" / «٥ مصادر، بحاجة إلى نظر: ٢»): the Arabic comma, not «؛».</summary>
    public static string Commas(IEnumerable<string> parts) => string.Join(IsArabic ? "، " : "\u00A0· ", parts);

    public static string Groups(IEnumerable<string> groups) => string.Join(IsArabic ? "؛ " : "\u00A0· ", groups);

    /// <summary>A time of day in the device's zone: "2:05 PM" / «٢:٠٥ م».</summary>
    public static string Time(DateTime local)
    {
        var text = local.ToString("t", CultureInfo.CurrentUICulture);
        return IsArabic ? ToArabicDigits(text) : text;
    }

    /// <summary>
    /// The device's zone at that moment as the admin pages name it (Intl's short zone name, as on admin
    /// Costs): "EST" / "EDT" and the other US zones' names in English, else "GMT+3" / "GMT-3:30"; Arabic
    /// always «غرينتش-٥» (Arabic digits, a hyphen), all Arabic so the line keeps its order. UTC is in the
    /// tooltip (<see cref="UtcText"/>).
    /// </summary>
    public static string ZoneLabel(DateTime local) => ZoneLabel(local, TimeZoneInfo.Local, IsArabic);

    /// <summary>The label for a given zone (tests pin the zone and the language).</summary>
    public static string ZoneLabel(DateTime local, TimeZoneInfo zone, bool arabic)
    {
        var offset = zone.GetUtcOffset(local);
        if (!arabic && UsZoneName(zone, local) is { } us) return us;
        if (IsUtc(zone)) return arabic ? "غرينتش" : "UTC";
        var name = arabic ? "غرينتش" : "GMT";
        if (offset == TimeSpan.Zero) return name;
        var hours = Math.Abs(offset.Hours).ToString(CultureInfo.InvariantCulture);
        var number = offset.Minutes == 0 ? hours : hours + ":" + Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture);
        return name + (offset < TimeSpan.Zero ? "-" : "+") + (arabic ? ToArabicDigits(number) : number);
    }

    // The zones Intl names in English (en-US short names); every other zone shows its offset
    private static readonly Dictionary<string, (string Standard, string Daylight)> UsZones = new(StringComparer.Ordinal)
    {
        ["America/New_York"] = ("EST", "EDT"), ["America/Detroit"] = ("EST", "EDT"), ["America/Toronto"] = ("EST", "EDT"),
        ["America/Indiana/Indianapolis"] = ("EST", "EDT"), ["America/Kentucky/Louisville"] = ("EST", "EDT"),
        ["America/Chicago"] = ("CST", "CDT"), ["America/Winnipeg"] = ("CST", "CDT"),
        ["America/Denver"] = ("MST", "MDT"), ["America/Edmonton"] = ("MST", "MDT"), ["America/Boise"] = ("MST", "MDT"),
        ["America/Phoenix"] = ("MST", "MST"),
        ["America/Los_Angeles"] = ("PST", "PDT"), ["America/Vancouver"] = ("PST", "PDT"),
        ["America/Anchorage"] = ("AKST", "AKDT"),
        ["Pacific/Honolulu"] = ("HST", "HST"),
    };

    private static string IanaId(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;

    private static bool IsUtc(TimeZoneInfo zone) => IanaId(zone) is "UTC" or "Etc/UTC" or "Etc/UCT" or "Etc/Universal" or "Etc/Zulu";

    private static string? UsZoneName(TimeZoneInfo zone, DateTime local) =>
        UsZones.TryGetValue(IanaId(zone), out var names) ? (zone.IsDaylightSavingTime(local) ? names.Daylight : names.Standard) : null;

    /// <summary>A time of day with the device's zone: "2:05 PM EST" / «٢:٠٥ م غرينتش-٥».</summary>
    public static string TimeWithZone(DateTime local) => Time(local) + " " + ZoneLabel(local);

    /// <summary>The same moment in UTC, with its date, for a time's tooltip: "Nov 22, 2026, 7:05 PM UTC" / «٢٢ نوفمبر ٢٠٢٦، ٧:٠٥ م غرينتش».</summary>
    public static string UtcText(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        var text = Format("When_Date", utc.ToString("d MMM yyyy", CultureInfo.CurrentUICulture), utc.ToString("t", CultureInfo.CurrentUICulture)) + " " + (IsArabic ? "غرينتش" : "UTC");
        return IsArabic ? ToArabicDigits(text) : text;
    }

    /// <summary>A date and time in the device's zone, digits per language.</summary>
    public static string When(DateTime local)
    {
        var today = DateTime.Today;
        var time = local.ToString("t", CultureInfo.CurrentUICulture);
        var text = local.Date == today ? Format("When_Today", time)
            : local.Date == today.AddDays(-1) ? Format("When_Yesterday", time)
            : local.Date == today.AddDays(1) ? Format("When_Tomorrow", time)
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

    private static readonly System.Text.RegularExpressions.Regex NeutralBreak = new(@"(\s*[:;!?.()\[\]\-–—|/]+\s*)");

    /// <summary>
    /// An Arabic line (a title) shown in the English interface. Uno's Skia text lays the line out in the window's direction,
    /// whatever the element's own FlowDirection, and breaks an Arabic line into pieces at punctuation such as ": ", so in an
    /// English window «فقه الصيام: النية» shows with its halves swapped. The pieces are stored in reverse order so they land
    /// the right way round. Arabic windows and Windows draw text natively and need nothing.
    /// </summary>
    public static string RightToLeftLine(string text) => IsArabic ? text : Reordered(text);

    /// <summary>
    /// A line of a dialog. Uno draws popups outside the window's right-to-left layout, so an Arabic line in a dialog
    /// breaks into swapped pieces even in the Arabic interface; its pieces are stored in reverse order as for
    /// <see cref="RightToLeftLine"/>.
    /// </summary>
    public static string PopupLine(string text) => Reordered(text);

    private static string Reordered(string text)
    {
#if HAS_UNO
        if (text.FirstOrDefault(char.IsLetter) is >= '\u0600' and <= '\u06FF')
        {
            // The punctuation between pieces is mirrored too, so ": " sits after the first half as it does natively
            var pieces = NeutralBreak.Split(text).Where(p => p.Length > 0).Reverse()
                .Select(p => NeutralBreak.IsMatch(p) && p.Trim().Length > 0 && NeutralBreak.Match(p).Length == p.Length ? new string(p.Reverse().ToArray()) : p);
            return string.Concat(pieces);
        }
#endif
        return text;
    }

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
