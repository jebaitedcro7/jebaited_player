using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 4 (V4, priprema za Discord): zaseban izlaz za glazbu i radio.</summary>
[TestClass]
public sealed class MusicOutputTests
{
    [TestCleanup]
    public void Cleanup() => MpvConfig.MusicMode = false;

    private static string? Device(AppSettings s, bool music, bool includeDefaults)
    {
        MpvConfig.MusicMode = music;
        return MpvConfig.PlaybackOptions(s, includeDefaults).Where(o => o.Name == "audio-device").Select(o => o.Value).FirstOrDefault();
    }

    [TestMethod]
    public void Music_uses_its_own_output_and_movies_stay_on_the_main_one()
    {
        var s = new AppSettings { AudioDevice = "wasapi/{speakers}", MusicAudioDevice = "wasapi/{cable}" };

        Assert.AreEqual("wasapi/{cable}", Device(s, music: true, includeDefaults: true));
        Assert.AreEqual("wasapi/{speakers}", Device(s, music: false, includeDefaults: true));
    }

    [TestMethod]
    public void Empty_music_output_follows_the_main_output()
    {
        var s = new AppSettings { AudioDevice = "wasapi/{speakers}" };

        Assert.AreEqual(string.Empty, s.MusicAudioDevice);
        Assert.AreEqual("wasapi/{speakers}", Device(s, music: true, includeDefaults: false));

        // Sve zadano: pri pokretanju se ništa ne postavlja, a tijekom rada se vraća "auto".
        var d = new AppSettings();
        Assert.IsNull(Device(d, music: true, includeDefaults: false));
        Assert.AreEqual("auto", Device(d, music: true, includeDefaults: true));
    }

    [TestMethod]
    public void Music_output_is_reset_with_the_audio_tab_and_kept_by_old_files()
    {
        var s = new AppSettings { MusicAudioDevice = "wasapi/{cable}" };
        SettingsDefaults.Reset(s, "audio");
        Assert.AreEqual(string.Empty, s.MusicAudioDevice);

        AppSettings old = AppSettings.Parse("""{ "Volume": 30, "AudioDevice": "wasapi/{x}" }""");
        Assert.AreEqual(string.Empty, old.MusicAudioDevice);
        Assert.AreEqual("wasapi/{x}", old.OutputDevice(music: true));
    }
}
