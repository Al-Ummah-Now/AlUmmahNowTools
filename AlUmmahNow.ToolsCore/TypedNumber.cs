using System.Globalization;

namespace AlUmmahNow.ToolsCore;

/// <summary>A number someone types to confirm what they are about to do, in Western, Arabic or Persian digits.</summary>
public static class TypedNumber
{
    /// <summary>From this many on, what cannot be undone is confirmed by typing how many.</summary>
    public const int AskFrom = 10;

    public static bool Is(string? text, int number)
    {
        var digits = string.Empty;
        var arabic = false;
        foreach (var c in (text ?? string.Empty).Trim())
        {
            // Grouping, as either language writes it
            if (c is ',' or '٬' or '،' or ' ' or ' ' or ' ') continue;
            var value = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= '٠' and <= '٩' => c - '٠',
                >= '۰' and <= '۹' => c - '۰',
                _ => -1,
            };
            if (value < 0 || digits.Length == 9) return false;
            arabic |= c is >= '٠' and <= '٩';
            digits += value.ToString(CultureInfo.InvariantCulture);
        }
        if (digits.Length == 0) return false;
        // A box on macOS and Windows draws Arabic digits from right to left: whoever types what the question shows may type
        // either end first
        return Number(digits) == number || (arabic && Number(new string(digits.Reverse().ToArray())) == number);

        static int Number(string text) => int.Parse(text, CultureInfo.InvariantCulture);
    }
}
