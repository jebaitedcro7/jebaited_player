using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 3: radna kopija postavki (M2a) i zadane vrijednosti po kartici (M2b).</summary>
[TestClass]
public sealed class SettingsWorkingCopyTests
{
    private static AppSettings Mine() => new()
    {
        Language = "hr", Theme = "light", Volume = 40, WindowLeft = 300, ResumeMode = "resume", SeekStepSeconds = 15, AutoUpdate = false,
        NormalizeAudio = true, VideoQuality = "high", ControlsHideSeconds = 6, SubtitleLanguages = ["en"],
        Equalizer = { Enabled = true, Preset = "rock" }, MusicEqualizer = new EqualizerSettings { Enabled = true, Preset = "bass" },
        SubtitleStyle = { FontSize = 60 }, MiniPlayer = { Left = 900, Top = 700, Scale = 1.5, Font = "digital", VuStyle = "led", AutoSwitch = false },
        Online = { OpenSubtitlesApiKey = "kljuc", OpenSubtitlesPassword = "lozinka", AutoSearch = false },
    };

    [TestMethod]
    public void Volume_and_positions_are_not_unsaved_changes_but_settings_are()
    {
        AppSettings s = Mine();
        string saved = SettingsWorkingCopy.Snapshot(s);
        s.Volume = 80;
        s.WindowLeft = 10;
        s.MiniPlayer.Left = 5;
        s.MiniPlayer.Scale = 2;
        Assert.IsFalse(SettingsWorkingCopy.IsDirty(s, saved));
        s.Theme = "dark";
        Assert.IsTrue(SettingsWorkingCopy.IsDirty(s, saved));
    }

    [TestMethod]
    public void Discard_goes_back_to_the_last_save_and_keeps_volume_positions_and_keys()
    {
        AppSettings s = Mine();
        string saved = SettingsWorkingCopy.Snapshot(s);
        s.Theme = "dark";
        s.Equalizer.Preset = "pop";
        s.MiniPlayer.VuStyle = "off";
        s.Volume = 90;
        s.MiniPlayer.Left = 50;
        s.Online.OpenSubtitlesApiKey = "novi-kljuc";

        SettingsWorkingCopy.Revert(s, saved);
        Assert.AreEqual("light", s.Theme);
        Assert.AreEqual("rock", s.Equalizer.Preset);
        Assert.AreEqual("led", s.MiniPlayer.VuStyle);
        Assert.AreEqual(90, s.Volume, "Glasnoća nije postavka koja se odbacuje.");
        Assert.AreEqual(50, s.MiniPlayer.Left, "Položaj mini playera ostaje.");
        Assert.AreEqual("novi-kljuc", s.Online.OpenSubtitlesApiKey, "Ključevi se ne vraćaju.");
        Assert.IsFalse(SettingsWorkingCopy.IsDirty(s, saved));
    }

    [TestMethod]
    public void Default_for_one_tab_changes_only_that_tab()
    {
        AppSettings s = Mine();
        SettingsDefaults.Reset(s, "general");
        Assert.AreEqual("ask", s.ResumeMode);
        Assert.AreEqual(5, s.SeekStepSeconds);
        Assert.AreEqual("light", s.Theme, "Izgled nije dirnut.");
        Assert.AreEqual("high", s.VideoQuality, "Slika nije dirnuta.");

        SettingsDefaults.Reset(s, "audio");
        Assert.IsFalse(s.NormalizeAudio || s.Equalizer.Enabled || s.MusicEqualizer.Enabled);
        Assert.AreEqual("flat", s.MusicEqualizer.Preset);
        Assert.AreEqual(60, s.SubtitleStyle.FontSize, "Titlovi nisu dirnuti.");
    }

    [TestMethod]
    public void Default_for_all_keeps_language_keys_volume_and_positions()
    {
        AppSettings s = Mine();
        SettingsDefaults.Reset(s, null);
        Assert.AreEqual("dark", s.Theme);
        Assert.AreEqual("balanced", s.VideoQuality);
        Assert.AreEqual(2.5, s.ControlsHideSeconds);
        Assert.IsTrue(s.AutoUpdate && s.Online.AutoSearch && s.MiniPlayer.AutoSwitch);
        Assert.AreEqual("lcd", s.MiniPlayer.Font);
        Assert.AreEqual(44, s.SubtitleStyle.FontSize);
        CollectionAssert.AreEqual(new[] { "hr", "sr", "bs", "en" }, s.SubtitleLanguages, "Titlovi prema jeziku sučelja.");

        Assert.AreEqual("hr", s.Language);
        Assert.AreEqual(40, s.Volume);
        Assert.AreEqual(300, s.WindowLeft);
        Assert.AreEqual(900, s.MiniPlayer.Left);
        Assert.AreEqual(1.5, s.MiniPlayer.Scale);
        Assert.AreEqual("kljuc", s.Online.OpenSubtitlesApiKey);
        Assert.AreEqual("lozinka", s.Online.OpenSubtitlesPassword);
    }
}
