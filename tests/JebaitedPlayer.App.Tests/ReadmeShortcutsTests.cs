using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>
/// 2.0: tablica „Tipkovnički prečaci” u README_HR/EN/DE sadrži svaku zadanu tipku iz registra (Shortcuts.Actions),
/// pa README ne može zaostati kad se doda radnja ili promijeni zadana tipka.
/// </summary>
[TestClass]
public sealed class ReadmeShortcutsTests
{
    [TestMethod]
    [DataRow("README_HR.md", "### Tipkovnički prečaci", "Razmaknica")]
    [DataRow("README_EN.md", "### Keyboard shortcuts", "Space")]
    [DataRow("README_DE.md", "### Tastenkürzel", "Leertaste")]
    public void ReadmeTableListsEveryDefaultKey(string file, string heading, string space)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), file)).Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = text.IndexOf(heading + "\n", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"{file}: nema odjeljka „{heading}”.");
        int end = text.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        string section = end > start ? text[start..end] : text[start..];

        // Prvi stupac tablice (bez zaglavlja i crte ispod njega).
        List<string> keyCells = section.Split('\n')
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal))
            .Skip(1)
            .Select(l => l.Split('|')[1].Trim())
            .ToList();
        Assert.IsTrue(keyCells.Count >= 20, $"{file}: tablica prečaca nije pronađena ({keyCells.Count} redaka).");
        string keys = " " + string.Join(" / ", keyCells) + " ";

        var missing = new List<string>();
        foreach (ShortcutAction action in Shortcuts.Actions)
        {
            foreach (KeyChord chord in action.Defaults)
            {
                string notation = Notation(chord, space);
                if (!Regex.IsMatch(keys, @"(?<=[\s/,])" + Regex.Escape(notation) + @"(?=[\s/,])"))
                {
                    missing.Add($"{action.Id}: {notation}");
                }
            }
        }

        Assert.AreEqual(0, missing.Count, $"{file}: u tablici prečaca nedostaje: {string.Join("; ", missing)}");
    }

    /// <summary>Zapis tipke kakav se koristi u README tablicama („Ctrl+Shift+O”, „←”, „Num +”).</summary>
    private static string Notation(KeyChord chord, string space)
    {
        string mods = (chord.Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : string.Empty)
            + (chord.Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : string.Empty)
            + (chord.Modifiers.HasFlag(ModifierKeys.Shift) ? "Shift+" : string.Empty);
        string key = chord.Key switch
        {
            Key.Space => space,
            Key.Left => "←",
            Key.Right => "→",
            Key.Up => "↑",
            Key.Down => "↓",
            Key.Back => "Backspace",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemPlus => "+",
            Key.OemMinus => "−",
            Key.Add => "Num +",
            Key.Subtract => "Num −",
            _ => chord.Key.ToString(), // slova, F1–F24, Home
        };
        return mods + key;
    }

    private static string RepoRoot()
    {
        string? dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        while (dir != null && !File.Exists(Path.Combine(dir, "jebaited_player.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Korijen repozitorija nije pronađen.");
    }
}
