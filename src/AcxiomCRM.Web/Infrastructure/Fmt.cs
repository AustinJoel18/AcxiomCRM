using System.Globalization;
using System.Text.RegularExpressions;

namespace AcxiomCRM.Web.Infrastructure;

/// <summary>Consistent display formatting (Indian digit grouping for currency).</summary>
public static partial class Fmt
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    public static string Money(decimal value) => "₹" + value.ToString("N2", India);
    public static string Money(decimal? value) => value is { } v ? Money(v) : "—";
    public static string MoneyShort(decimal value) => value switch
    {
        >= 10_000_000 => "₹" + (value / 10_000_000).ToString("0.##", India) + " Cr",
        >= 100_000 => "₹" + (value / 100_000).ToString("0.##", India) + " L",
        _ => Money(value)
    };
    public static string Number(int value) => value.ToString("N0", India);
    public static string Percent(double value) => value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    public static string Date(DateTime value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public static string Date(DateTime? value) => value is { } v ? Date(v) : "—";
    public static string DateTime(DateTime value) => value.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture);
    public static string DateTime(DateTime? value) => value is { } v ? DateTime(v) : "—";

    /// <summary>"SocialMedia" → "Social Media", "SalesExecutive" → "Sales Executive".</summary>
    public static string Humanize(object? value) => value is null ? "—" : SplitWords().Replace(value.ToString()!, "$1 $2");

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex SplitWords();
}
