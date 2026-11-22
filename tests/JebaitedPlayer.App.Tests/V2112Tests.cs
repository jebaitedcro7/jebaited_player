using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Services.Discord;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.1.12: glasnoća za Discord (formula, pretvorba u 16 bita) i koje datoteke uopće traže omot.</summary>
[TestClass]
public sealed class DiscordVolumeTests
{
    [TestMethod]
    public void Following_the_player_never_changes_the_sound()
    {
        foreach (double volume in new[] { 0.0, 3, 50, 100, 130 })
        {
            Assert.AreEqual(1, DiscordVolume.Gain(true, volume, false, 10));
            Assert.AreEqual(1, DiscordVolume.Gain(true, volume, true, 10));
        }
    }

    [TestMethod]
    public void Own_volume_cancels_the_player_volume_with_the_mpv_curve()
    {
        Assert.AreEqual(1, DiscordVolume.Gain(false, 60, false, 60), 1e-12);
        Assert.AreEqual(8, DiscordVolume.Gain(false, 50, false, 100), 1e-9, "player at half = 1/8 (−18 dB), so ×8");
        Assert.AreEqual(1.0 / 8, DiscordVolume.Gain(false, 100, false, 50), 1e-12);
        Assert.AreEqual(DiscordVolume.MaxGain, DiscordVolume.Gain(false, 2, false, 100), "at most +40 dB");
        Assert.AreEqual(0, DiscordVolume.Gain(false, 0, false, 60), "player at 0 sends silence");
        Assert.AreEqual(0, DiscordVolume.Gain(false, 80, true, 60), "muted player sends silence");
        Assert.AreEqual(0, DiscordVolume.Gain(false, 80, false, 0));
        Assert.AreEqual(DiscordVolume.Gain(false, 80, false, 100), DiscordVolume.Gain(false, 80, false, 250), "Discord volume is capped at 100");
    }

    [TestMethod]
    public void Float_to_pcm16_applies_gain_and_clips()
    {
        float[] input = [0f, 0.5f, -0.5f, 1f, -1f, 0.01f, float.NaN];
        short[] output = new short[input.Length];
        int peak = DiscordVolume.ToPcm16(input, output, 1);
        CollectionAssert.AreEqual(new short[] { 0, 16384, -16384, 32767, -32767, 328, 0 }, output);
        Assert.AreEqual(32767, peak);

        peak = DiscordVolume.ToPcm16([0.25f, -0.25f, 0.001f], output, 8);
        Assert.AreEqual(32767, output[0], "clipped");
        Assert.AreEqual(-32768, output[1], "clipped");
        Assert.AreEqual(262, output[2]);
        Assert.AreEqual(32767, peak);
    }

    [TestMethod]
    public void Pcm16_gain_without_float_capture()
    {
        short[] samples = [100, -100, 20000, -20000];
        Assert.AreEqual(20000, DiscordVolume.ApplyGain(samples, 1));
        CollectionAssert.AreEqual(new short[] { 100, -100, 20000, -20000 }, samples, "gain 1 leaves samples untouched");
        int peak = DiscordVolume.ApplyGain(samples, 2);
        CollectionAssert.AreEqual(new short[] { 200, -200, 32767, -32768 }, samples);
        Assert.AreEqual(32767, peak);
    }

    [TestMethod]
    public void Settings_default_to_following_the_player()
    {
        var s = new AppSettings();
        Assert.IsTrue(s.DiscordFollowPlayerVolume);
        Assert.AreEqual(DiscordVolume.DefaultVolume, s.DiscordVolume);
        AppSettings saved = AppSettings.Parse("""{ "DiscordFollowPlayerVolume": false, "DiscordVolume": 35 }""");
        Assert.IsFalse(saved.DiscordFollowPlayerVolume);
        Assert.AreEqual(35, saved.DiscordVolume);
    }
}

[TestClass]
public sealed class CoverArtLoaderTests
{
    [TestMethod]
    public void Only_local_music_files_ask_for_a_cover()
    {
        Assert.IsTrue(CoverArtLoader.IsEligible(@"C:\Glazba\pjesma.mp3"));
        Assert.IsTrue(CoverArtLoader.IsEligible(@"C:\Glazba\pjesma.FLAC"));
        Assert.IsFalse(CoverArtLoader.IsEligible(@"C:\Filmovi\film.mkv"), "video has its own picture");
        Assert.IsFalse(CoverArtLoader.IsEligible("https://stream.example/radio.mp3"), "radio");
        Assert.IsFalse(CoverArtLoader.IsEligible("pjesma.mp3"), "relative path");
        Assert.IsFalse(CoverArtLoader.IsEligible(null));
        Assert.IsFalse(CoverArtLoader.IsEligible(string.Empty));
    }

    [TestMethod]
    public void Images_are_decoded_small_and_bad_data_gives_null()
    {
        // 1×1 PNG.
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var image = CoverArtLoader.Decode(png);
        Assert.IsNotNull(image);
        Assert.IsTrue(image.IsFrozen, "usable from any thread");
        Assert.IsNull(CoverArtLoader.Decode([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]));
    }
}
