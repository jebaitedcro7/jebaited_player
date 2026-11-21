using System.Text.Json;
using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Localization;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.App.Tests;

/// <summary>2.1.7: pozicioniranje prozora, izgledi mini playera, podaci o pjesmi, izgled i prognoza na početnom ekranu.</summary>
[TestClass]
public sealed class UiImprovementsTests
{
    // --- Prozori ostaju u vidljivom području ------------------------------------------------------------------------

    private static readonly Rect Work = new(0, 0, 1920, 1040); // 1080p, taskbar 40 px dolje

    [TestMethod]
    public void Window_below_the_taskbar_is_moved_up()
    {
        // Mini player pri dnu, otvoren Equalizer: prozor naraste i izlazi ispod taskbara.
        Rect fitted = WindowPlacement.Clamp(new Rect(1400, 900, 460, 380), Work);
        Assert.AreEqual(660, fitted.Top, 0.001);
        Assert.AreEqual(1040, fitted.Bottom, 0.001);
        Assert.AreEqual(1400, fitted.Left, 0.001);
    }

    [TestMethod]
    public void Window_off_any_edge_is_pulled_back_and_a_visible_window_is_left_alone()
    {
        Assert.AreEqual(new Rect(1460, 100, 460, 200), WindowPlacement.Clamp(new Rect(1800, 100, 460, 200), Work));
        Assert.AreEqual(new Rect(0, 0, 460, 200), WindowPlacement.Clamp(new Rect(-300, -50, 460, 200), Work));
        var inside = new Rect(300, 200, 460, 200);
        Assert.AreEqual(inside, WindowPlacement.Clamp(inside, Work));
    }

    [TestMethod]
    public void Second_monitor_with_negative_coordinates_and_oversized_windows_are_handled()
    {
        var left = new Rect(-1920, 0, 1920, 1040); // drugi zaslon lijevo od glavnog
        Assert.AreEqual(new Rect(-460, 800, 460, 240), WindowPlacement.Clamp(new Rect(-100, 900, 460, 240), left));
        // Prozor veći od područja: gornji lijevi kut ostaje vidljiv (naslov i tipke).
        Rect big = WindowPlacement.Clamp(new Rect(500, 500, 3000, 2000), Work);
        Assert.AreEqual(0, big.Left);
        Assert.AreEqual(0, big.Top);
    }

    // --- Izgledi mini playera ---------------------------------------------------------------------------------------

    [TestMethod]
    public void Mini_player_has_five_designs_and_the_choice_survives_save_and_load()
    {
        CollectionAssert.AreEqual(new[] { "classic", "card", "strip", "side", "ufo" }, MiniPlayerSettings.Designs.ToArray());
        var s = new AppSettings { MiniPlayer = { Design = "ufo" } };
        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        loaded.Normalize();
        Assert.AreEqual("ufo", loaded.MiniPlayer.ActiveDesign);

        loaded.MiniPlayer.Compact = true; // uska traka ima prednost (dizajn 3), a prethodni izgled se pamti
        Assert.AreEqual("strip", loaded.MiniPlayer.ActiveDesign);
        Assert.AreEqual("ufo", loaded.MiniPlayer.Design);
    }

    [TestMethod]
    public void Settings_from_older_versions_open_the_classic_design()
    {
        AppSettings s = JsonSerializer.Deserialize<AppSettings>("""{ "MiniPlayer": { "Compact": false, "Design": "nepoznato" } }""")!;
        s.Normalize();
        Assert.AreEqual("classic", s.MiniPlayer.ActiveDesign);
        CollectionAssert.AreEqual(new[] { "artist", "title" }, s.MiniPlayer.InfoFields);

        AppSettings strip = JsonSerializer.Deserialize<AppSettings>("""{ "MiniPlayer": { "Compact": true } }""")!;
        strip.Normalize();
        Assert.AreEqual("strip", strip.MiniPlayer.ActiveDesign);
    }

    [TestMethod]
    public void Every_design_and_info_field_and_home_position_has_a_translation()
    {
        IReadOnlyDictionary<string, string> en = Loc.LoadAll("en");
        foreach (string id in MiniPlayerSettings.Designs)
        {
            Assert.IsTrue(en.ContainsKey("miniDesign." + id), id);
        }

        foreach (string id in TrackInfo.AllFields)
        {
            Assert.IsTrue(en.ContainsKey("info." + id), id);
        }

        foreach (string id in HomeInfoSettings.Positions)
        {
            Assert.IsTrue(en.ContainsKey("homePos." + id), id);
        }

        foreach (string id in HomeInfoSettings.AllDetails)
        {
            Assert.IsTrue(en.ContainsKey("weatherDetail." + id), id);
        }
    }

    // --- Podaci o pjesmi --------------------------------------------------------------------------------------------

    private static readonly TrackMeta Song = new("Parni valjak", "Jedan dan života", "Live", "1988", "Rock", "320000", "48000", "01 - Jedan dan.mp3", null, null, false);

    [TestMethod]
    public void Default_shows_only_artist_and_title()
    {
        string text = TrackInfo.Format(Song, TrackInfo.DefaultFields.ToArray());
        Assert.AreEqual("Parni valjak — Jedan dan života", text);
        StringAssert.DoesNotMatch(text, new System.Text.RegularExpressions.Regex("kbps|Live|1988|Rock|\\.mp3"));
    }

    [TestMethod]
    public void Chosen_fields_are_added_and_unchosen_are_left_out()
    {
        string all = TrackInfo.Format(Song, TrackInfo.AllFields.ToArray());
        Assert.AreEqual("Parni valjak — Jedan dan života  ···  Live · 1988 · Rock · 320 kbps · 48 kHz · 01 - Jedan dan.mp3", all);
        Assert.AreEqual("Jedan dan života", TrackInfo.Format(Song, new[] { "title" }));
        Assert.AreEqual("Parni valjak", TrackInfo.Format(Song, new[] { "artist" }));
        Assert.AreEqual("Jedan dan života  ···  Live", TrackInfo.Format(Song, new[] { "title", "album" }));
        Assert.AreEqual("Jedan dan života", TrackInfo.Format(Song, Array.Empty<string>()), "traka nikad ne ostaje prazna");
    }

    [TestMethod]
    public void Radio_shows_station_and_current_song_and_unknown_fields_are_dropped()
    {
        var radio = new TrackMeta(null, "stream", null, null, null, "128000", null, null, "Narodni radio", "Artist - Pjesma", true);
        Assert.AreEqual("Narodni radio — Artist - Pjesma", TrackInfo.Format(radio, TrackInfo.DefaultFields.ToArray()));
        Assert.AreEqual("Narodni radio", TrackInfo.Format(radio, new[] { "artist" }));
        CollectionAssert.AreEqual(new[] { "artist", "year" }, TrackInfo.Normalize(new[] { "year", "bogus", "artist", "artist" }));
    }

    // --- Početni ekran: izgled i prognoza ---------------------------------------------------------------------------

    [TestMethod]
    public void Home_look_defaults_survive_save_and_load_and_reset_keeps_place_and_toggles()
    {
        var s = new AppSettings();
        s.HomeInfo.City = "Zagreb";
        (s.HomeInfo.Latitude, s.HomeInfo.Longitude) = (45.8, 15.9);
        s.HomeInfo.ShowWeather = true;
        s.HomeInfo.TimeStyle = new HomeElementStyle { Font = "Georgia", Size = 48, Position = "tr" };
        s.HomeInfo.WeatherDetails = ["uv", "feels"];

        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        loaded.Normalize();
        Assert.AreEqual("Georgia", loaded.HomeInfo.TimeStyle.Font);
        Assert.AreEqual(48, loaded.HomeInfo.TimeStyle.Size);
        Assert.AreEqual("tr", loaded.HomeInfo.TimeStyle.Position);
        CollectionAssert.AreEqual(new[] { "feels", "uv" }, loaded.HomeInfo.WeatherDetails);

        loaded.HomeInfo.ResetStyle();
        Assert.AreEqual(string.Empty, loaded.HomeInfo.TimeStyle.Font);
        Assert.AreEqual("tl", loaded.HomeInfo.TimeStyle.Position);
        CollectionAssert.AreEqual(HomeInfoSettings.DefaultDetails.ToArray(), loaded.HomeInfo.WeatherDetails);
        Assert.AreEqual("Zagreb", loaded.HomeInfo.City);
        Assert.IsTrue(loaded.HomeInfo.ShowWeather);
    }

    [TestMethod]
    public void Invalid_home_look_values_are_corrected()
    {
        var h = new HomeInfoSettings
        {
            TimeStyle = { Font = "Nepostojeći", Size = 500, Position = "mc" },
            DateStyle = { Size = double.NaN },
            WeatherStyle = { Size = 3 },
        };
        h.Normalize();
        Assert.AreEqual(string.Empty, h.TimeStyle.Font);
        Assert.AreEqual(HomeInfoSettings.MaxSize, h.TimeStyle.Size);
        Assert.AreEqual("tl", h.TimeStyle.Position, "sredina je zauzeta logom");
        Assert.AreEqual(0, h.DateStyle.Size);
        Assert.AreEqual(HomeInfoSettings.MinSize, h.WeatherStyle.Size);
    }

    [TestMethod]
    public void Wind_arrow_points_where_the_wind_blows_to()
    {
        Assert.AreEqual("↓", HomeInfoService.WindArrow(0)); // sjeverni vjetar puše prema jugu
        Assert.AreEqual("↗", HomeInfoService.WindArrow(225)); // jugozapadni puše prema sjeveroistoku
        Assert.AreEqual(string.Empty, HomeInfoService.WindArrow(null));
    }

    [TestMethod]
    public void Mouse_wheel_step_keeps_volume_inside_0_to_100()
    {
        Assert.IsTrue(ViewModels.PlayerViewModel.VolumeWheelStep > 0 && ViewModels.PlayerViewModel.VolumeWheelStep <= 5);
        double volume = 99;
        volume = Math.Clamp(Math.Round(volume + ViewModels.PlayerViewModel.VolumeWheelStep), 0, 100);
        Assert.AreEqual(100, volume);
        volume = Math.Clamp(Math.Round(1 - ViewModels.PlayerViewModel.VolumeWheelStep), 0, 100);
        Assert.AreEqual(0, volume);
    }

    [TestMethod]
    public void Mouse_wheel_volume_is_proportional_to_delta_and_keeps_fractions()
    {
        var vm = new ViewModels.PlayerViewModel(new AppSettings { Volume = 50 }, null);
        vm.Volume = 50;
        vm.ChangeVolumeByWheel(120);
        Assert.AreEqual(50 + ViewModels.PlayerViewModel.VolumeWheelStep, vm.Volume, "jedan zarez = jedan korak");
        vm.ChangeVolumeByWheel(-120);
        Assert.AreEqual(50, vm.Volume);

        // Fini kotačić (npr. 30 po događaju) ne smije se izgubiti zaokruživanjem: 4 događaja = jedan zarez.
        for (int i = 0; i < 4; i++)
        {
            vm.ChangeVolumeByWheel(30);
        }

        Assert.AreEqual(50 + ViewModels.PlayerViewModel.VolumeWheelStep, vm.Volume);
        vm.ChangeVolumeByWheel(120 * 40);
        Assert.AreEqual(100, vm.Volume);
        vm.ChangeVolumeByWheel(-120 * 40);
        Assert.AreEqual(0, vm.Volume);
    }

    [TestMethod]
    public void Next_episode_lead_defaults_to_the_end_and_survives_save_and_load()
    {
        Assert.AreEqual(0, new AppSettings().UpNextLeadSeconds, "zadano kao prije: tek na kraju");
        AppSettings loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { UpNextLeadSeconds = 45 }))!;
        Assert.AreEqual(45, loaded.UpNextLeadSeconds);
    }
}
