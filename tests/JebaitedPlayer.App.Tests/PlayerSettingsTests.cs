using System.Text;
using System.Text.Json;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Subtitles;

namespace JebaitedPlayer.App.Tests;

/// <summary>Nove postavke (UPGRADE 3): stari settings.json se učitava, zadane vrijednosti, primjena na mpv.</summary>
[TestClass]
public sealed class PlayerSettingsTests
{
    [TestMethod]
    public void Settings_from_1_1_get_safe_defaults_for_new_options()
    {
        const string old = """
            { "Volume": 55, "Theme": "light", "Language": "de", "AutoPlayNext": false, "UpNextSeconds": 15 }
            """;
        AppSettings s = JsonSerializer.Deserialize<AppSettings>(old)!;
        s.Normalize();
        Assert.AreEqual(55, s.Volume);
        Assert.AreEqual("de", s.Language);
        Assert.AreEqual("ask", s.ResumeMode, "Kao dosad: kartica „Nastaviti gledanje?”.");
        Assert.AreEqual(5, s.SeekStepSeconds);
        Assert.IsTrue(s.HardwareDecoding);
        Assert.AreEqual("balanced", s.VideoQuality);
        Assert.AreEqual("auto", s.AudioDevice);
        Assert.AreEqual("auto", s.DefaultAspect);
        Assert.AreEqual(2.5, s.ControlsHideSeconds);
        Assert.IsNull(s.AudioLanguage);
        Assert.IsNull(s.SubtitleCodePage);
        Assert.IsFalse(s.OpenFullscreen || s.OnTopWhilePlaying || s.NormalizeAudio || s.HdrPassthrough);
        Assert.AreEqual(0, s.AudioDelayMs);
    }

    [TestMethod]
    public void New_options_survive_save_and_load()
    {
        var s = new AppSettings
        {
            ResumeMode = "resume", SeekStepSeconds = 10, OpenFullscreen = true, OnTopWhilePlaying = true, AudioLanguage = "en",
            AudioDevice = "wasapi/{abc}", AudioDelayMs = -250, NormalizeAudio = true, HardwareDecoding = false, VideoQuality = "high",
            DefaultAspect = "16:9", HdrPassthrough = true, ControlsHideSeconds = 6, SubtitleCodePage = 1250,
        };
        AppSettings l = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        l.Normalize();
        Assert.AreEqual("resume", l.ResumeMode);
        Assert.AreEqual(10, l.SeekStepSeconds);
        Assert.IsTrue(l.OpenFullscreen && l.OnTopWhilePlaying && l.NormalizeAudio && l.HdrPassthrough);
        Assert.IsFalse(l.HardwareDecoding);
        Assert.AreEqual("en", l.AudioLanguage);
        Assert.AreEqual("wasapi/{abc}", l.AudioDevice);
        Assert.AreEqual(-250, l.AudioDelayMs);
        Assert.AreEqual("high", l.VideoQuality);
        Assert.AreEqual("16:9", l.DefaultAspect);
        Assert.AreEqual(6, l.ControlsHideSeconds);
        Assert.AreEqual(1250, l.SubtitleCodePage);
    }

    [TestMethod]
    public void Hand_edited_invalid_values_fall_back_to_defaults()
    {
        var s = new AppSettings { ResumeMode = "maybe", SeekStepSeconds = 7, VideoQuality = "ultra", ControlsHideSeconds = 0.1, AudioDelayMs = 99999, AudioDevice = " " };
        s.Normalize();
        Assert.AreEqual("ask", s.ResumeMode);
        Assert.AreEqual(5, s.SeekStepSeconds);
        Assert.AreEqual("balanced", s.VideoQuality);
        Assert.AreEqual(2.5, s.ControlsHideSeconds);
        Assert.AreEqual(2000, s.AudioDelayMs);
        Assert.AreEqual("auto", s.AudioDevice);
    }

    [TestMethod]
    public void Default_settings_add_no_mpv_options_before_start()
    {
        var options = MpvConfig.PlaybackOptions(new AppSettings(), includeDefaults: false).ToList();
        Assert.AreEqual(0, options.Count, "Zadano ponašanje playera ostaje isto kao u 1.1.0: " + string.Join(", ", options));
    }

    [TestMethod]
    public void Changed_settings_become_mpv_options()
    {
        var s = new AppSettings
        {
            HardwareDecoding = false, AudioLanguage = "de", AudioDevice = "wasapi/{x}", AudioDelayMs = 300, NormalizeAudio = true,
            HdrPassthrough = true, VideoQuality = "fast",
        };
        Dictionary<string, string> o = MpvConfig.PlaybackOptions(s, includeDefaults: false).ToDictionary(x => x.Name, x => x.Value);
        Assert.AreEqual("no", o["hwdec"]);
        Assert.AreEqual("de,deu,ger", o["alang"]);
        Assert.AreEqual("wasapi/{x}", o["audio-device"]);
        Assert.AreEqual("0.3", o["audio-delay"]);
        Assert.AreEqual(MpvConfig.NormalizeFilter, o["af"]);
        Assert.AreEqual("yes", o["target-colorspace-hint"]);
        Assert.AreEqual("bilinear", o["scale"]);
        Assert.AreEqual("no", o["deband"]);
    }

    [TestMethod]
    public void Live_change_back_to_defaults_clears_the_options()
    {
        Dictionary<string, string> o = MpvConfig.PlaybackOptions(new AppSettings(), includeDefaults: true).ToDictionary(x => x.Name, x => x.Value);
        Assert.AreEqual("auto-safe", o["hwdec"]);
        Assert.AreEqual(string.Empty, o["alang"]);
        Assert.AreEqual("auto", o["audio-device"]);
        Assert.AreEqual("0", o["audio-delay"]);
        Assert.AreEqual(string.Empty, o["af"]);
        Assert.AreEqual("no", o["target-colorspace-hint"]);
    }

    [TestMethod]
    public void High_quality_uses_sharper_scaling_and_debanding()
    {
        Dictionary<string, string> o = MpvConfig.PlaybackOptions(new AppSettings { VideoQuality = "high" }, includeDefaults: false)
            .ToDictionary(x => x.Name, x => x.Value);
        Assert.AreEqual("ewa_lanczossharp", o["scale"]);
        Assert.AreEqual("yes", o["deband"]);
    }

    [TestMethod]
    public void Audio_devices_from_mpv_are_parsed()
    {
        const string json = """
            [{"name":"auto","description":"Autoselect device"},{"name":"wasapi/{0.0.0}","description":"Speakers (Realtek)"},{"name":"wasapi/{1}","description":""},{"description":"broken"}]
            """;
        var devices = MainWindow.ParseAudioDevices(json);
        CollectionAssert.AreEqual(new[] { "auto", "wasapi/{0.0.0}", "wasapi/{1}" }, devices.Select(d => d.Name).ToArray());
        Assert.AreEqual("Speakers (Realtek)", devices[1].Description);
        Assert.AreEqual("wasapi/{1}", devices[2].Description, "Bez opisa se prikazuje naziv.");
        Assert.AreEqual(0, MainWindow.ParseAudioDevices("not json").Count);
        Assert.AreEqual(0, MainWindow.ParseAudioDevices(null).Count);
    }

    [TestMethod]
    public void Old_subtitles_use_the_encoding_from_settings_but_utf8_stays_utf8()
    {
        byte[] cp1250 = SubtitleEncoding.Get(1250).GetBytes("Čćžšđ – šuma");
        string text = SubtitleEncoding.Decode(cp1250, null, out EncodingDetection detection, 1250);
        Assert.AreEqual("Čćžšđ – šuma", text);
        Assert.AreEqual(1250, detection.Encoding.CodePage);

        byte[] utf8 = Encoding.UTF8.GetBytes("Čćžšđ");
        Assert.AreEqual("Čćžšđ", SubtitleEncoding.Decode(utf8, null, out EncodingDetection utf8Detection, 1251));
        Assert.AreEqual(65001, utf8Detection.Encoding.CodePage, "UTF-8 ima prednost pred postavkom.");

        // Postavka mijenja pogađanje: isti bajtovi s CP1251 daju ćirilicu.
        Assert.AreEqual(1251, (SubtitleEncoding.Decode(cp1250, null, out EncodingDetection forced, 1251), forced.Encoding.CodePage).Item2);
    }
}
