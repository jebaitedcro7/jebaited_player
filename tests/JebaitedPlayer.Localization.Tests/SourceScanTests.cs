using System.Text.RegularExpressions;

namespace JebaitedPlayer.Localization.Tests;

/// <summary>
/// Pregled izvornog koda: svaki ključ koji kod koristi postoji, nijedan ključ nije višak, a u sučelju nema
/// tekstova upisanih izravno (sve ide kroz Languages/*.json).
/// </summary>
[TestClass]
public sealed partial class SourceScanTests
{
    // Vlastita imena i tehnički nazivi koji se ne prevode.
    private static readonly HashSet<string> AllowedXamlTexts =
    [
        "jebaited_player", "OpenSubtitles.com", "SubDL.com", "Titlovi.com", "s", "−5 s", "−1 s", "+1 s", "+5 s",
        "1.", "2.", "3.", "4.", "jebaited_cro",
    ];

    // Datoteke u kojima su naša slova dio podataka, ne sučelja (prepoznavanje jezika titlova, testni titl u dijagnostici).
    private static readonly string[] DataFiles =
    [
        "SubtitleLanguage.cs", "SubtitleEncoding.cs", "DiagnosticsRunner.cs", "MediaNameParser.cs",
    ];

    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "jebaited_player.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Korijen repozitorija nije pronađen.");
        }
    }

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [TestMethod]
    public void Every_key_used_in_code_exists_in_english()
    {
        IReadOnlyDictionary<string, string> english = Loc.LoadAll("en");
        HashSet<string> bases = LanguageFileTests.PluralBases(english);
        var missing = new List<string>();
        foreach ((string file, string key) in UsedKeys())
        {
            if (!english.ContainsKey(key) && !bases.Contains(key))
            {
                missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }

        Assert.AreEqual(0, missing.Count, "Ključevi bez teksta: " + string.Join(", ", missing.Distinct()));
    }

    [TestMethod]
    public void Every_english_key_is_used_somewhere()
    {
        IReadOnlyDictionary<string, string> english = Loc.LoadAll("en");
        HashSet<string> used = UsedKeys().Select(u => u.Key).ToHashSet();
        string aligner = File.ReadAllText(Path.Combine(Root, "src", "JebaitedPlayer.Sync", "SubtitleAligner.cs"));
        foreach (Match statement in DetailKeyRegex().Matches(aligner))
        {
            foreach (Match word in QuotedWordRegex().Matches(statement.Value))
            {
                used.Add("syncDetail." + word.Groups[1].Value);
            }
        }

        // Equalizer (ciklus 1): naziv preseta je "eq." + id iz popisa preseta u Equalizer.cs.
        string equalizer = File.ReadAllText(Path.Combine(Root, "src", "JebaitedPlayer.App", "Infrastructure", "Equalizer.cs"));
        foreach (Match preset in EqualizerPresetRegex().Matches(equalizer))
        {
            used.Add("eq." + preset.Groups[1].Value);
        }

        string[] unused = LanguageFileTests.Simple(english)
            .Concat(LanguageFileTests.PluralBases(english))
            .Where(k => !used.Contains(k))
            .Order()
            .ToArray();
        Assert.AreEqual(0, unused.Length, "Ključevi koje kod ne koristi: " + string.Join(", ", unused));
    }

    [TestMethod]
    public void Xaml_has_no_hard_coded_texts()
    {
        var found = new List<string>();
        foreach (string file in SourceFiles("*.xaml"))
        {
            foreach (Match m in XamlTextRegex().Matches(File.ReadAllText(file)))
            {
                string value = m.Groups[2].Value;
                if (value.StartsWith('{') || value.StartsWith("&#", StringComparison.Ordinal) || !value.Any(char.IsLetter)
                    || AllowedXamlTexts.Contains(value))
                {
                    continue;
                }

                found.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}=\"{value}\"");
            }
        }

        Assert.AreEqual(0, found.Count, "Tekst upisan izravno u XAML: " + string.Join(" | ", found));
    }

    [TestMethod]
    public void Code_has_no_croatian_texts_outside_logs_and_comments()
    {
        var found = new List<string>();
        foreach (string file in SourceFiles("*.cs").Where(f => !DataFiles.Contains(Path.GetFileName(f))))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*') || LogRegex().IsMatch(line)
                    || line.Contains("čćžšđ", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in StringLiteralRegex().Matches(line))
                {
                    if (CroatianLetterRegex().IsMatch(m.Value) && !IsLogContinuation(lines, i))
                    {
                        found.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Value}");
                    }
                }
            }
        }

        Assert.AreEqual(0, found.Count, "Hrvatski tekst u kodu (treba ključ): " + string.Join(" | ", found));
    }

    /// <summary>Nastavak zapisa u log u sljedećem retku (npr. <c>Log.Info($"..." +</c> pa <c>$"..."</c>).</summary>
    private static bool IsLogContinuation(string[] lines, int index)
    {
        for (int i = index; i >= Math.Max(0, index - 4); i--)
        {
            string line = lines[i].Trim();
            if (LogRegex().IsMatch(line))
            {
                return true;
            }

            if (i < index && (line.EndsWith(';') || line.EndsWith('{') || line.EndsWith('}')))
            {
                return false;
            }
        }

        return false;
    }

    private static IEnumerable<(string File, string Key)> UsedKeys()
    {
        HashSet<string> prefixes = Loc.LoadAll("en").Keys.Where(k => !k.StartsWith('_')).Select(k => k[..k.IndexOf('.')]).ToHashSet();
        foreach (string file in SourceFiles("*.cs").Concat(SourceFiles("*.xaml")))
        {
            string text = File.ReadAllText(file);
            foreach (Match m in XamlKeyRegex().Matches(text))
            {
                yield return (file, m.Groups[1].Value);
            }

            foreach (Match m in QuotedKeyRegex().Matches(text))
            {
                string key = m.Groups[1].Value;
                string prefix = key[..key.IndexOf('.')];
                if (prefixes.Contains(prefix) && (prefix == "languageName" || !FileNameRegex().IsMatch(key)))
                {
                    yield return (file, key);
                }
            }
        }
    }

    [GeneratedRegex(@"\{loc:T ([A-Za-z0-9_.]+)\}")]
    private static partial Regex XamlKeyRegex();

    [GeneratedRegex(@"""([a-z][A-Za-z0-9]*\.[A-Za-z0-9_.]*[A-Za-z0-9])""")]
    private static partial Regex QuotedKeyRegex();

    [GeneratedRegex(@"\.(json|srt|ass|ssa|txt|log|exe|dll|db|jpg|png|ico|xaml|cs|md)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNameRegex();

    [GeneratedRegex(@"[Dd]etailKey = [^;,]*")]
    private static partial Regex DetailKeyRegex();

    [GeneratedRegex(@"""([A-Za-z]+)""")]
    private static partial Regex QuotedWordRegex();

    [GeneratedRegex(@"\(""([a-z]+)"", \[")]
    private static partial Regex EqualizerPresetRegex();

    [GeneratedRegex(@"\b(Text|Content|ToolTip|Header|Title)=""([^""]*)""")]
    private static partial Regex XamlTextRegex();

    [GeneratedRegex(@"\bLog\.(?:(?:Playback|Subtitles|Ui|Stability|Updates|Usage)\.)?(Info|Warn|Error|Debug)\(|LogManual\(|Log\.Init|LogMessage\?\.Invoke|Console\.Write|Detail = |throw new ArgumentException|LastAutoResult = |_pendingLog|RunHook\(|string channels = |InvalidDataException\(""(Nepoznat|Neispravna|Analiza)")]
    private static partial Regex LogRegex();

    [GeneratedRegex(@"\$?@?""(?:[^""\\]|\\.)*""")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"[čćžšđČĆŽŠĐ]")]
    private static partial Regex CroatianLetterRegex();
}
