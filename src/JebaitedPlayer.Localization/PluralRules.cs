namespace JebaitedPlayer.Localization;

/// <summary>
/// Oblici množine (prema CLDR-u, samo za cijele brojeve). Jezik u svojoj datoteci bira pravilo
/// (<c>_language.plural</c>), pa novi jezik ne traži izmjenu koda:
/// <list type="bullet">
/// <item><c>one-other</c>: engleski, njemački i većina jezika (1 file / 2 files).</item>
/// <item><c>south-slavic</c>: hrvatski, bosanski, srpski (1 epizoda / 2 epizode / 5 epizoda, 21 epizoda).</item>
/// <item><c>slovenian</c>: slovenski (1 epizoda / 2 epizodi / 3 epizode / 5 epizod).</item>
/// <item><c>none</c>: jezici bez množine (samo <c>other</c>).</item>
/// </list>
/// </summary>
public static class PluralRules
{
    public const string OneOther = "one-other";
    public const string SouthSlavic = "south-slavic";
    public const string Slovenian = "slovenian";
    public const string None = "none";

    public static IReadOnlyList<string> FormsOf(string rule) => rule switch
    {
        SouthSlavic => ["one", "few", "other"],
        Slovenian => ["one", "two", "few", "other"],
        None => ["other"],
        _ => ["one", "other"],
    };

    public static string Form(string rule, long n)
    {
        n = Math.Abs(n);
        long mod10 = n % 10;
        long mod100 = n % 100;
        return rule switch
        {
            SouthSlavic when mod10 == 1 && mod100 != 11 => "one",
            SouthSlavic when mod10 is >= 2 and <= 4 && (mod100 is < 12 or > 14) => "few",
            SouthSlavic => "other",
            Slovenian => mod100 switch { 1 => "one", 2 => "two", 3 or 4 => "few", _ => "other" },
            None => "other",
            _ => n == 1 ? "one" : "other",
        };
    }
}
