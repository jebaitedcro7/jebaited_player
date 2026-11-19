using System.Text.Json;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 2, V1 Mini Music Player: postavke i VU metar u audio filtru.</summary>
[TestClass]
public sealed class MiniPlayerTests
{
    [TestCleanup]
    public void Cleanup() => MpvConfig.VuMeter = false;

    [TestMethod]
    public void Settings_from_1_3_open_the_full_design_at_normal_size()
    {
        AppSettings s = JsonSerializer.Deserialize<AppSettings>("""{ "Volume": 60, "Equalizer": { "Enabled": true } }""")!;
        s.Normalize();
        Assert.AreEqual(1, s.MiniPlayer.Scale);
        Assert.IsFalse(s.MiniPlayer.Compact || s.MiniPlayer.ShowEqualizer || s.MiniPlayer.ShowPlaylist);
        Assert.IsNull(s.MiniPlayer.Left);
    }

    [TestMethod]
    public void Size_and_layout_survive_save_and_load_and_size_is_limited()
    {
        var s = new AppSettings { MiniPlayer = { Scale = 1.5, Compact = true, ShowPlaylist = true, Left = 100, Top = 200 } };
        AppSettings l = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        l.Normalize();
        Assert.AreEqual(1.5, l.MiniPlayer.Scale);
        Assert.IsTrue(l.MiniPlayer.Compact && l.MiniPlayer.ShowPlaylist);
        Assert.AreEqual(200, l.MiniPlayer.Top);

        foreach ((double input, double expected) in new[] { (9.0, MiniPlayerSettings.MaxScale), (0.1, MiniPlayerSettings.MinScale), (double.NaN, 1.0) })
        {
            var m = new MiniPlayerSettings { Scale = input };
            m.Normalize();
            Assert.AreEqual(expected, m.Scale, input.ToString());
        }
    }

    [TestMethod]
    public void Vu_meter_is_added_only_while_the_mini_player_is_open()
    {
        var s = new AppSettings { NormalizeAudio = true };
        Assert.AreEqual(MpvConfig.NormalizeFilter, MpvConfig.AudioFilter(s));

        MpvConfig.VuMeter = true;
        Assert.AreEqual(MpvConfig.NormalizeFilter + "," + MpvConfig.VuMeterFilter, MpvConfig.AudioFilter(s), "Mjeri se na kraju lanca (s EQ-om).");
        Assert.AreEqual(MpvConfig.VuMeterFilter, MpvConfig.AudioFilter(new AppSettings()));

        MpvConfig.VuMeter = false;
        Assert.AreEqual(string.Empty, MpvConfig.AudioFilter(new AppSettings()));
    }
}

/// <summary>1.4.1: odvojeni equalizeri (filmovi / glazba), font i VU mini playera.</summary>
[TestClass]
public sealed class SeparateEqualizerTests
{
    [TestCleanup]
    public void Cleanup() => MpvConfig.MusicMode = false;

    [TestMethod]
    public void Settings_from_1_4_give_music_its_own_copy_of_the_equalizer()
    {
        AppSettings s = JsonSerializer.Deserialize<AppSettings>("""{ "Volume": 60, "Equalizer": { "Enabled": true, "Preset": "rock" } }""")!;
        s.Normalize();
        Assert.IsTrue(s.MusicEqualizer.Enabled);
        Assert.AreEqual("rock", s.MusicEqualizer.Preset, "Zvuk glazbe se nakon nadogradnje ne mijenja.");

        s.MusicEqualizer.Preset = "bass";
        s.MusicEqualizer.CustomGains[0] = 6;
        Assert.AreEqual("rock", s.Equalizer.Preset, "Odvojeni su: promjena glazbe ne dira filmove.");
        Assert.AreEqual(0, s.Equalizer.CustomGains[0]);
    }

    [TestMethod]
    public void The_equalizer_for_what_is_playing_is_used()
    {
        var s = new AppSettings
        {
            Equalizer = { Enabled = false },
            MusicEqualizer = new EqualizerSettings { Enabled = true, Preset = "bass" },
        };
        MpvConfig.MusicMode = false;
        Assert.AreEqual(string.Empty, MpvConfig.AudioFilter(s), "Film: filmski equalizer je isključen.");
        MpvConfig.MusicMode = true;
        StringAssert.Contains(MpvConfig.AudioFilter(s), "equalizer=f=31:t=o:w=1:g=7", "Glazba: Bass preset.");
    }

    [TestMethod]
    public void Both_equalizers_survive_save_load_and_export_import()
    {
        var s = new AppSettings { Equalizer = { Enabled = true, Preset = "vocal" }, MusicEqualizer = new EqualizerSettings { Enabled = true, Preset = "electronic" } };
        AppSettings l = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        l.Normalize();
        Assert.AreEqual("vocal", l.Equalizer.Preset);
        Assert.AreEqual("electronic", l.MusicEqualizer.Preset);

        var target = new AppSettings();
        SettingsTransfer.ApplyTo(target, SettingsTransfer.Read(SettingsTransfer.Export(s, "1.4.1", DateTime.UtcNow)));
        Assert.AreEqual("electronic", target.MusicEqualizer.Preset);
        Assert.AreEqual("vocal", target.Equalizer.Preset);
    }

    [TestMethod]
    public void Unknown_font_and_vu_style_fall_back_and_defaults_are_sensible()
    {
        var m = new MiniPlayerSettings { Font = "comic", VuStyle = "rainbow" };
        m.Normalize();
        Assert.AreEqual("lcd", m.Font);
        Assert.AreEqual("bars", m.VuStyle);
        Assert.IsTrue(new MiniPlayerSettings().AutoSwitch);
        Assert.IsFalse(new MiniPlayerSettings().WasOpen);
        Assert.AreEqual("Bahnschrift", MiniPlayerSettings.FontFamilyFor("digital"));
        Assert.AreEqual("Consolas", MiniPlayerSettings.FontFamilyFor("nepostojeci"));
    }
}
