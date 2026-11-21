using System.Windows.Input;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.0: registar prečaca, vlastite tipke u postavkama, F1 sa stvarnim tipkama.</summary>
[TestClass]
public sealed class ShortcutsTests
{
    private const ModifierKeys None = ModifierKeys.None;
    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys Shift = ModifierKeys.Shift;

    [TestCleanup]
    public void Cleanup() => KeyboardShortcuts.Settings = new AppSettings();

    [TestMethod]
    [DataRow(Key.Space, None, "playPause")]
    [DataRow(Key.K, None, "playPause")]
    [DataRow(Key.Left, None, "seekBack")]
    [DataRow(Key.Right, None, "seekForward")]
    [DataRow(Key.Left, Ctrl, "seekBackLong")]
    [DataRow(Key.Right, Ctrl, "seekForwardLong")]
    [DataRow(Key.Home, None, "seekStart")]
    [DataRow(Key.Up, None, "volumeUp")]
    [DataRow(Key.Down, None, "volumeDown")]
    [DataRow(Key.M, None, "mute")]
    [DataRow(Key.F, None, "fullscreen")]
    [DataRow(Key.F11, None, "fullscreen")]
    [DataRow(Key.OemComma, None, "frameBack")]
    [DataRow(Key.OemPeriod, None, "frameForward")]
    [DataRow(Key.OemPlus, None, "speedUp")]
    [DataRow(Key.Add, None, "speedUp")]
    [DataRow(Key.OemMinus, None, "speedDown")]
    [DataRow(Key.Subtract, None, "speedDown")]
    [DataRow(Key.Back, None, "speedNormal")]
    [DataRow(Key.B, None, "nextAudio")]
    [DataRow(Key.O, None, "aspect")]
    [DataRow(Key.S, None, "screenshot")]
    [DataRow(Key.S, Shift, "screenshotPictureOnly")]
    [DataRow(Key.T, Ctrl, "loadSubtitles")]
    [DataRow(Key.F, Ctrl, "searchSubtitles")]
    [DataRow(Key.V, None, "toggleSubtitles")]
    [DataRow(Key.J, None, "nextSubtitles")]
    [DataRow(Key.G, None, "subtitleEarlier")]
    [DataRow(Key.H, None, "subtitleLater")]
    [DataRow(Key.G, Ctrl, "subtitleEarlierFine")]
    [DataRow(Key.H, Ctrl, "subtitleLaterFine")]
    [DataRow(Key.G, Shift, "subtitleEarlierCoarse")]
    [DataRow(Key.H, Shift, "subtitleLaterCoarse")]
    [DataRow(Key.Z, None, "markAudio")]
    [DataRow(Key.X, None, "markSubtitle")]
    [DataRow(Key.A, None, "autoSync")]
    [DataRow(Key.Back, Ctrl, "resetSync")]
    [DataRow(Key.N, None, "nextEpisode")]
    [DataRow(Key.P, None, "previousEpisode")]
    [DataRow(Key.L, None, "playlist")]
    [DataRow(Key.D, Ctrl, "favorite")]
    [DataRow(Key.O, Ctrl, "openFile")]
    [DataRow(Key.O, Ctrl | Shift, "openFolder")]
    [DataRow(Key.M, Ctrl, "miniPlayer")]
    [DataRow(Key.R, Ctrl, "radio")]
    [DataRow(Key.S, Ctrl | Shift, "spotify")]
    [DataRow(Key.Y, Ctrl | Shift, "youTube")]
    [DataRow(Key.D, Ctrl | Shift, "discord")]
    [DataRow(Key.F1, None, "help")]
    public void Default_keys_do_what_they_did_before_2_0(Key key, ModifierKeys mods, string expected) =>
        Assert.AreEqual(expected, Shortcuts.Resolve(new AppSettings(), key, mods));

    [TestMethod]
    public void Shift_alone_falls_back_to_the_plain_key_but_Ctrl_does_not()
    {
        var s = new AppSettings();
        Assert.AreEqual("mute", Shortcuts.Resolve(s, Key.M, Shift));        // kao prije: Shift+M utišava
        Assert.AreEqual("speedUp", Shortcuts.Resolve(s, Key.OemPlus, Shift)); // „+” na engleskom rasporedu
        Assert.AreEqual("subtitleEarlierCoarse", Shortcuts.Resolve(s, Key.G, Shift)); // točna kombinacija ima prednost
        Assert.IsNull(Shortcuts.Resolve(s, Key.Q, None));
        Assert.IsNull(Shortcuts.Resolve(s, Key.V, Ctrl));
        Assert.IsNull(Shortcuts.Resolve(s, Key.Escape, None)); // Esc ima stalnu ulogu izvan registra
    }

    [TestMethod]
    public void Defaults_have_no_conflicts_no_reserved_keys_and_at_most_two_keys()
    {
        var seen = new Dictionary<KeyChord, string>();
        foreach (ShortcutAction action in Shortcuts.Actions)
        {
            Assert.IsTrue(action.Defaults.Length <= Shortcuts.Slots, action.Id);
            Assert.IsTrue(Shortcuts.Categories.Contains(action.Category), action.Id);
            foreach (KeyChord chord in action.Defaults)
            {
                Assert.IsFalse(Shortcuts.IsReserved(chord), $"{action.Id}: {chord}");
                Assert.IsTrue(seen.TryAdd(chord, action.Id), $"{chord} is used by {seen.GetValueOrDefault(chord)} and {action.Id}");
            }
        }

        Assert.AreEqual(Shortcuts.Actions.Count, Shortcuts.Actions.Select(a => a.Id).Distinct().Count(), "action ids are unique");
    }

    [TestMethod]
    public void Every_action_and_category_has_a_name_in_all_six_languages()
    {
        try
        {
            foreach (string language in new[] { "en", "de", "hr", "bs", "sr", "sl" })
            {
                JebaitedPlayer.Localization.Loc.SetLanguage(language);
                foreach (ShortcutAction action in Shortcuts.Actions)
                {
                    string name = Shortcuts.Name(action);
                    Assert.IsTrue(name.Length > 1 && name != action.NameKey, $"{language} {action.Id}: '{name}'");
                }

                var categories = Shortcuts.Categories.Select(Shortcuts.CategoryName).ToList();
                Assert.AreEqual(categories.Count, categories.Distinct().Count(), language);
                Assert.IsTrue(categories.All(c => !c.StartsWith("shortcutCategory.", StringComparison.Ordinal)), language);
            }
        }
        finally
        {
            JebaitedPlayer.Localization.Loc.SetLanguage("hr");
        }
    }

    [TestMethod]
    public void Key_chords_are_written_and_read_back_and_bad_text_is_rejected()
    {
        foreach (KeyChord chord in Shortcuts.Actions.SelectMany(a => a.Defaults))
        {
            Assert.AreEqual(chord, KeyChord.Parse(chord.Serialize()), chord.Serialize());
        }

        Assert.AreEqual("Ctrl+Shift+D", new KeyChord(Key.D, Ctrl | Shift).Serialize());
        Assert.AreEqual("Ctrl + Shift + D", new KeyChord(Key.D, Ctrl | Shift).Display());
        Assert.AreEqual("Ctrl + ←", new KeyChord(Key.Left, Ctrl).Display());
        Assert.AreEqual("Razmaknica", new KeyChord(Key.Space, None).Display());
        Assert.AreEqual(new KeyChord(Key.O, Ctrl | ModifierKeys.Alt | Shift), KeyChord.Parse("Ctrl+Alt+Shift+O"));

        foreach (string bad in new[] { "", "   ", "Ctrl+", "Foo", "Ctrl+Ctrl+A", "65", "Win+A", "Ctrl+LeftShift", "Ctrl+None" })
        {
            Assert.IsFalse(KeyChord.TryParse(bad, out _), $"'{bad}'");
        }
    }

    [TestMethod]
    public void Assigning_a_key_saves_only_changes_and_moves_a_key_from_another_action()
    {
        var s = new AppSettings();

        // Nova tipka za Otvori video: Ctrl+O više ne otvara, Ctrl+U otvara; spremljena je samo ta promjena.
        Assert.IsNull(Shortcuts.Assign(s, "openFile", 0, new KeyChord(Key.U, Ctrl)));
        Assert.AreEqual("openFile", Shortcuts.Resolve(s, Key.U, Ctrl));
        Assert.IsNull(Shortcuts.Resolve(s, Key.O, Ctrl));
        CollectionAssert.AreEqual(new[] { "openFile" }, s.Shortcuts.Keys.ToArray());

        // V ima „Prikaži titlove”: dodjela je premješta i javlja odakle.
        Assert.AreEqual("toggleSubtitles", Shortcuts.Assign(s, "help", 1, new KeyChord(Key.V, None)));
        Assert.AreEqual("help", Shortcuts.Resolve(s, Key.V, None));
        Assert.IsTrue(Shortcuts.Get(s, "toggleSubtitles").All(k => k.IsEmpty));
        Assert.AreEqual("F1 / V", Shortcuts.Display(s, "help"));

        // Ista tipka na drugo mjesto iste radnje ne udvostručuje se.
        Shortcuts.Assign(s, "help", 0, new KeyChord(Key.V, None));
        Assert.AreEqual("V", Shortcuts.Display(s, "help"));

        // Vraćanje na zadano: promjena nestaje; vraćena zadana tipka oduzima se onome tko ju je u međuvremenu dobio.
        Shortcuts.Assign(s, "openFile", 0, new KeyChord(Key.O, Ctrl));
        Assert.IsFalse(s.Shortcuts.ContainsKey("openFile"), "back to the default = nothing saved");
        Shortcuts.Assign(s, "radio", 1, new KeyChord(Key.F1, None));
        CollectionAssert.AreEqual(new[] { "radio" }, Shortcuts.Reset(s, "help").ToArray());
        Assert.AreEqual("help", Shortcuts.Resolve(s, Key.F1, None));
        Assert.AreEqual("Ctrl + R", Shortcuts.Display(s, "radio"));

        // Delete: prazna tipka briše mjesto.
        Shortcuts.Assign(s, "mute", 0, KeyChord.None);
        Assert.IsNull(Shortcuts.Resolve(s, Key.M, None));
        Assert.AreEqual(string.Empty, Shortcuts.Gesture(s, "mute"));
    }

    [TestMethod]
    public void Saved_shortcuts_survive_a_settings_file_and_bad_entries_are_cleaned()
    {
        var s = new AppSettings();
        Shortcuts.Assign(s, "discord", 0, new KeyChord(Key.F9, None));
        AppSettings back = AppSettings.Parse(System.Text.Json.JsonSerializer.Serialize(s));
        Assert.AreEqual("discord", Shortcuts.Resolve(back, Key.F9, None));

        // Ručno pokvarena datoteka: nepoznata radnja, neispravna tipka, rezervirana tipka, dvostruka dodjela, null.
        AppSettings bad = AppSettings.Parse("""
            { "Shortcuts": {
                "noSuchAction": [ "F9" ],
                "mute": [ "Banana", "Escape" ],
                "radio": [ "Space" ],
                "spotify": null
            } }
            """);
        Assert.IsFalse(bad.Shortcuts.ContainsKey("noSuchAction"));
        Assert.IsTrue(Shortcuts.Get(bad, "mute").All(k => k.IsEmpty), "invalid and reserved keys are dropped");
        Assert.AreEqual("playPause", Shortcuts.Resolve(bad, Key.Space, None), "the first action keeps a key used twice");
        Assert.IsTrue(Shortcuts.Get(bad, "radio").All(k => k.IsEmpty));
        Assert.AreEqual("Ctrl + Shift + S", Shortcuts.Display(bad, "spotify"), "null falls back to the default");

        // Zadano za karticu Prečaci briše sve promjene.
        SettingsDefaults.Reset(back, "shortcuts");
        Assert.AreEqual(0, back.Shortcuts.Count);
        Assert.AreEqual("Ctrl + Shift + D", Shortcuts.Display(back, "discord"));
    }

    [TestMethod]
    public void F1_list_shows_the_real_keys_and_hides_actions_without_keys()
    {
        var s = new AppSettings();
        KeyboardShortcuts.Settings = s;
        Assert.IsTrue(KeyboardShortcuts.All.Any(r => r.Keys == "Razmaknica / K"));
        Assert.IsTrue(KeyboardShortcuts.All.Any(r => r.Keys == "Z, zatim X"));
        Assert.IsTrue(KeyboardShortcuts.All.Any(r => r.Keys.Contains("Ctrl + Shift + D", StringComparison.Ordinal)));

        Shortcuts.Assign(s, "playPause", 0, new KeyChord(Key.P, Ctrl));
        Shortcuts.Assign(s, "playPause", 1, KeyChord.None);
        Shortcuts.Assign(s, "favorite", 0, KeyChord.None);
        string favorite = JebaitedPlayer.Localization.Loc.T("shortcut.favorite");
        Assert.IsTrue(KeyboardShortcuts.All.Any(r => r.Keys == "Ctrl + P"));
        Assert.IsFalse(KeyboardShortcuts.All.Any(r => r.Description == favorite), "an action without keys is not listed");
    }
}
