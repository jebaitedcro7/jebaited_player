using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Localization;

namespace JebaitedPlayer.App.Tests;

/// <summary>Equalizer (ciklus 1, V2): mpv filtar, preseti, vlastiti preset, spremanje i stare postavke.</summary>
[TestClass]
public sealed class EqualizerTests
{
    private static Dictionary<string, string> Options(AppSettings s, bool defaults = false) =>
        MpvConfig.PlaybackOptions(s, includeDefaults: defaults).ToDictionary(x => x.Name, x => x.Value);

    [TestMethod]
    public void Off_by_default_and_adds_no_audio_filter()
    {
        var s = new AppSettings();
        Assert.IsFalse(s.Equalizer.Enabled);
        Assert.AreEqual("flat", s.Equalizer.Preset);
        Assert.IsFalse(Options(s).ContainsKey("af"), "Zadano: zvuk točno kao u datoteci.");
        Assert.AreEqual(string.Empty, Options(s, defaults: true)["af"], "Isključivanje tijekom rada vraća prazan filtar.");
    }

    [TestMethod]
    public void Flat_preset_at_zero_preamp_costs_nothing()
    {
        var s = new AppSettings { Equalizer = { Enabled = true, Preset = "flat", Preamp = 0 } };
        Assert.IsNull(Equalizer.BuildFilters(s.Equalizer));
        Assert.AreEqual(string.Empty, MpvConfig.AudioFilter(s));
    }

    [TestMethod]
    public void Rock_preset_builds_ten_bands_with_headroom()
    {
        var s = new AppSettings { Equalizer = { Enabled = true, Preset = "rock" } };
        string af = MpvConfig.AudioFilter(s);
        StringAssert.StartsWith(af, "lavfi=[volume=-5dB,equalizer=f=31:t=o:w=1:g=5,");
        StringAssert.EndsWith(af, "equalizer=f=16000:t=o:w=1:g=5]");
        Assert.AreEqual(10, af.Split("equalizer=").Length - 1);
    }

    [TestMethod]
    public void Zero_bands_are_skipped_and_cut_only_needs_no_headroom()
    {
        var s = new AppSettings { Equalizer = { Enabled = true, Preset = Equalizer.Custom, CustomGains = [0, 0, -3, 0, 0, 0, 0, 0, 0, -6] } };
        Assert.AreEqual("equalizer=f=125:t=o:w=1:g=-3,equalizer=f=16000:t=o:w=1:g=-6", Equalizer.BuildFilters(s.Equalizer));
    }

    [TestMethod]
    public void Preamp_is_added_on_top_of_headroom()
    {
        var s = new AppSettings { Equalizer = { Enabled = true, Preset = Equalizer.Custom, Preamp = 3, CustomGains = [2, 0, 0, 0, 0, 0, 0, 0, 0, 0] } };
        Assert.AreEqual("volume=1dB,equalizer=f=31:t=o:w=1:g=2", Equalizer.BuildFilters(s.Equalizer));
    }

    [TestMethod]
    public void Works_together_with_normalization_in_one_graph()
    {
        var s = new AppSettings { NormalizeAudio = true, Equalizer = { Enabled = true, Preset = "bass" } };
        string af = Options(s)["af"];
        StringAssert.StartsWith(af, "lavfi=[volume=-7dB,equalizer=f=31");
        StringAssert.EndsWith(af, "," + MpvConfig.NormalizeGraph + "]");
        Assert.AreEqual(1, af.Split("lavfi=").Length - 1);

        s.Equalizer.Enabled = false;
        Assert.AreEqual(MpvConfig.NormalizeFilter, Options(s)["af"], "Bez equalizera ostaje samo ujednačavanje, kao u 1.2.0.");
    }

    [TestMethod]
    public void Builtin_presets_are_valid_and_translated()
    {
        Assert.IsTrue(Equalizer.Presets.Count >= 7, "Kriterij ciklusa: najmanje 7 preseta.");
        Assert.AreEqual(Equalizer.Presets.Count, Equalizer.Presets.Select(p => p.Id).Distinct().Count());
        foreach (var (id, gains) in Equalizer.Presets)
        {
            Assert.AreEqual(Equalizer.BandCount, gains.Length, id);
            Assert.IsTrue(gains.All(g => Math.Abs(g) <= Equalizer.MaxGain), id);
        }

        foreach (LanguageInfo language in Loc.Available)
        {
            IReadOnlyDictionary<string, string> texts = Loc.LoadAll(language.Code);
            foreach (string id in Equalizer.Presets.Select(p => p.Id).Append(Equalizer.Custom))
            {
                Assert.IsTrue(texts.ContainsKey("eq." + id), $"{language.Code}: nema prijevoda eq.{id}");
            }
        }
    }

    [TestMethod]
    public void Custom_preset_survives_save_and_load()
    {
        var s = new AppSettings { Equalizer = { Enabled = true, Preset = Equalizer.Custom, Preamp = -2.5, CustomGains = [1, 2, 3, 4, 5, -1, -2, -3, -4, -5] } };
        AppSettings l = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        l.Normalize();
        Assert.IsTrue(l.Equalizer.Enabled);
        Assert.AreEqual(Equalizer.Custom, l.Equalizer.Preset);
        Assert.AreEqual(-2.5, l.Equalizer.Preamp);
        CollectionAssert.AreEqual(s.Equalizer.CustomGains, l.Equalizer.Gains);
        Assert.AreEqual(Equalizer.BuildFilters(s.Equalizer), Equalizer.BuildFilters(l.Equalizer));
    }

    [TestMethod]
    public void Settings_from_1_2_have_equalizer_off()
    {
        AppSettings s = JsonSerializer.Deserialize<AppSettings>("""{ "Volume": 70, "NormalizeAudio": true }""")!;
        s.Normalize();
        Assert.IsFalse(s.Equalizer.Enabled);
        Assert.AreEqual(Equalizer.BandCount, s.Equalizer.CustomGains.Length);
        Assert.AreEqual(MpvConfig.NormalizeFilter, MpvConfig.AudioFilter(s));
    }

    [TestMethod]
    public void Hand_edited_equalizer_values_are_repaired()
    {
        AppSettings s = JsonSerializer.Deserialize<AppSettings>(
            """{ "Equalizer": { "Enabled": true, "Preset": "metal", "Preamp": 40, "CustomGains": [99, -99, 1.26] } }""")!;
        s.Normalize();
        Assert.AreEqual("flat", s.Equalizer.Preset);
        Assert.AreEqual(Equalizer.MaxGain, s.Equalizer.Preamp);
        CollectionAssert.AreEqual(new double[] { 12, -12, 1.5, 0, 0, 0, 0, 0, 0, 0 }, s.Equalizer.CustomGains);

        AppSettings nulled = JsonSerializer.Deserialize<AppSettings>("""{ "Equalizer": null }""")!;
        nulled.Normalize();
        Assert.IsNotNull(nulled.Equalizer);
    }

    [TestMethod]
    public void Frequency_labels_are_short()
    {
        CollectionAssert.AreEqual(
            new[] { "31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" },
            Equalizer.Frequencies.Select(Equalizer.FrequencyLabel).ToArray());
    }
}
