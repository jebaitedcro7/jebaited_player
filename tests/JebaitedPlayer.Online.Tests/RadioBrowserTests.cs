using JebaitedPlayer.Online;

namespace JebaitedPlayer.Online.Tests;

/// <summary>Ciklus 3, V3: Radio Browser (parsiranje odgovora, upit, provjera URL-a), bez mreže.</summary>
[TestClass]
public sealed class RadioBrowserTests
{
    private const string Stations = """
        [
          { "stationuuid": "a1", "name": " Jazz Radio ", "url": "http://x/jazz.pls", "url_resolved": "https://stream.example/jazz", "country": "France",
            "countrycode": "fr", "tags": "jazz,smooth jazz", "codec": "mp3", "bitrate": 128 },
          { "stationuuid": "a2", "name": "Bez URL-a", "url": "", "url_resolved": "" },
          { "stationuuid": "a3", "name": "Lokalna datoteka", "url_resolved": "file:///C:/x.mp3" },
          { "stationuuid": "a4", "name": "Duplikat", "url_resolved": "https://stream.example/jazz" },
          { "stationuuid": "a5", "name": "Samo url", "url": "http://radio.example/live", "bitrate": "nije broj" }
        ]
        """;

    [TestMethod]
    public void Stations_are_parsed_and_unsafe_or_duplicate_entries_skipped()
    {
        IReadOnlyList<RadioStation> list = RadioBrowserClient.ParseStations(Stations);
        Assert.AreEqual(2, list.Count);
        RadioStation jazz = list[0];
        Assert.AreEqual("Jazz Radio", jazz.Name);
        Assert.AreEqual("https://stream.example/jazz", jazz.Url, "url_resolved ima prednost.");
        Assert.AreEqual("FR", jazz.CountryCode);
        Assert.AreEqual("MP3", jazz.Codec);
        Assert.AreEqual(128, jazz.Bitrate);
        Assert.AreEqual("http://radio.example/live", list[1].Url);
        Assert.AreEqual(0, list[1].Bitrate);
    }

    [TestMethod]
    public void Countries_and_tags_are_parsed()
    {
        var countries = RadioBrowserClient.ParseCountries("""[{ "name": "Croatia", "iso_3166_1": "hr", "stationcount": 312 }, { "name": "?", "iso_3166_1": "" }]""");
        Assert.AreEqual(1, countries.Count);
        Assert.AreEqual(new RadioCountry("HR", "Croatia", 312), countries[0]);
        CollectionAssert.AreEqual(new[] { "pop", "rock" }, RadioBrowserClient.ParseTags("""[{ "name": "pop" }, { "name": "x" }, { "name": "rock" }, { "name": "pop" }]""").ToArray());
    }

    [TestMethod]
    public void Search_sends_only_what_the_user_chose()
    {
        string all = RadioBrowserClient.SearchQuery(null, " ", null, 60);
        Assert.AreEqual("/json/stations/search?hidebroken=true&limit=60&order=clickcount&reverse=true", all);
        string q = RadioBrowserClient.SearchQuery("Radio Dalmacija", "pop", "hr", 999);
        StringAssert.Contains(q, "name=Radio+Dalmacija");
        StringAssert.Contains(q, "tag=pop");
        StringAssert.Contains(q, "countrycode=HR");
        StringAssert.Contains(q, "limit=200");
    }

    [TestMethod]
    public void Only_http_and_https_streams_are_accepted()
    {
        Assert.IsTrue(RadioBrowserClient.IsStreamUrl(" https://stream.example/live.mp3 "));
        Assert.IsTrue(RadioBrowserClient.IsStreamUrl("http://1.2.3.4:8000/;"));
        foreach (string bad in new[] { "", "radio", "file:///C:/x.mp3", "ftp://x/y", "javascript:alert(1)", @"C:\Glazba\a.mp3" })
        {
            Assert.IsFalse(RadioBrowserClient.IsStreamUrl(bad), bad);
        }
    }
}
