using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Online.Tests;

/// <summary>Ciklus 5, V8: Spotify klijent prema lokalnom simuliranom poslužitelju (bez prave prijave i Premiuma).</summary>
[TestClass]
public sealed class SpotifyTests
{
    /// <summary>Najmanji HTTP poslužitelj: ruta → (status, JSON); bilježi zahtjeve.</summary>
    private sealed class FakeSpotify : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<string, string, string, (int Status, string Body)> _route;

        public FakeSpotify(Func<string, string, string, (int, string)> route)
        {
            _route = route;
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public Uri Base => new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/");

        public ConcurrentQueue<string> Requests { get; } = new();

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        NetworkStream s = client.GetStream();
                        var raw = new StringBuilder();
                        var buf = new byte[8192];
                        int contentLength = 0;
                        while (true)
                        {
                            int n = await s.ReadAsync(buf);
                            if (n == 0)
                            {
                                return;
                            }

                            raw.Append(Encoding.UTF8.GetString(buf, 0, n));
                            string text = raw.ToString();
                            int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                            if (end < 0)
                            {
                                continue;
                            }

                            foreach (string line in text[..end].Split("\r\n"))
                            {
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                {
                                    contentLength = int.Parse(line[15..].Trim());
                                }
                            }

                            if (text.Length - end - 4 >= contentLength)
                            {
                                string[] first = text.Split("\r\n")[0].Split(' ');
                                string body = text[(end + 4)..];
                                Requests.Enqueue($"{first[0]} {first[1]} {body}".Trim());
                                (int status, string json) = _route(first[0], first[1], body);
                                byte[] payload = Encoding.UTF8.GetBytes(json);
                                string head = $"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                                await s.WriteAsync(Encoding.ASCII.GetBytes(head));
                                await s.WriteAsync(payload);
                                return;
                            }
                        }
                    }
                });
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }

    private const string PlayerJson = """
        { "is_playing": true, "progress_ms": 61000,
          "device": { "id": "dev1", "name": "Moje računalo", "volume_percent": 55 },
          "item": { "name": "Pjesma", "uri": "spotify:track:abc", "duration_ms": 200000,
                    "external_urls": { "spotify": "https://open.spotify.com/track/abc" },
                    "artists": [ { "name": "Izvođač A" }, { "name": "Izvođač B" } ],
                    "album": { "name": "Album", "images": [ { "url": "https://i.scdn.co/image/cover" } ] } } }
        """;

    [TestMethod]
    public void Pkce_challenge_matches_rfc_7636_and_the_url_has_everything()
    {
        Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", SpotifyPkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        string verifier = SpotifyPkce.CreateVerifier();
        Assert.IsTrue(verifier.Length is >= 43 and <= 128 && verifier.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'));

        string url = SpotifyPkce.AuthorizeUrl(SpotifyClient.DefaultAccounts, "cid123", "http://127.0.0.1:43821/callback", "chal", "st");
        StringAssert.StartsWith(url, "https://accounts.spotify.com/authorize?");
        foreach (string part in new[] { "client_id=cid123", "response_type=code", "code_challenge_method=S256", "code_challenge=chal", "state=st",
                     "redirect_uri=http%3A%2F%2F127.0.0.1%3A43821%2Fcallback", "user-modify-playback-state" })
        {
            StringAssert.Contains(url, part);
        }
    }

    [TestMethod]
    public async Task Loopback_accepts_only_the_right_state()
    {
        int port = FreePort();
        Task<string> wait = SpotifyLoopback.WaitForCodeAsync(port, "dobro", "<p>ok</p>", CancellationToken.None);
        using var http = new HttpClient();
        string page = await http.GetStringAsync($"http://127.0.0.1:{port}/callback?code=KOD123&state=dobro");
        Assert.AreEqual("KOD123", await wait);
        Assert.AreEqual("<p>ok</p>", page);

        int port2 = FreePort();
        Task<string> bad = SpotifyLoopback.WaitForCodeAsync(port2, "dobro", "x", CancellationToken.None);
        await http.GetStringAsync($"http://127.0.0.1:{port2}/callback?code=KOD&state=tude");
        SpotifyException ex = await Assert.ThrowsExceptionAsync<SpotifyException>(() => bad);
        Assert.AreEqual(SpotifyError.Unauthorized, ex.Kind);
    }

    [TestMethod]
    public async Task Token_exchange_and_refresh_keep_the_refresh_token()
    {
        using var fake = new FakeSpotify((method, path, body) =>
            body.Contains("grant_type=authorization_code", StringComparison.Ordinal)
                ? (200, """{ "access_token": "A1", "refresh_token": "R1", "expires_in": 3600 }""")
                : (200, """{ "access_token": "A2", "expires_in": 3600 }"""));
        using var client = new SpotifyClient("test", fake.Base);

        SpotifyTokens t1 = await client.ExchangeCodeAsync("cid", "code", "http://127.0.0.1:43821/callback", "ver", CancellationToken.None);
        Assert.AreEqual(("A1", "R1"), (t1.AccessToken, t1.RefreshToken));
        Assert.IsTrue(t1.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(50));
        SpotifyTokens t2 = await client.RefreshAsync("cid", "R1", CancellationToken.None);
        Assert.AreEqual(("A2", "R1"), (t2.AccessToken, t2.RefreshToken), "Bez novog refresh tokena ostaje stari.");
        StringAssert.Contains(fake.Requests.First(), "code_verifier=ver");
    }

    [TestMethod]
    public async Task Playback_state_lists_and_commands()
    {
        using var fake = new FakeSpotify((method, path, body) => (method, path) switch
        {
            ("GET", "/v1/me/player?additional_types=track,episode") => (200, PlayerJson),
            ("GET", "/v1/me/playlists?limit=50") => (200, """{ "items": [ { "name": "Moja lista", "uri": "spotify:playlist:p1", "owner": { "display_name": "ja" }, "images": [] } ] }"""),
            ("GET", "/v1/me/tracks?limit=50") => (200, """{ "items": [ { "track": { "name": "T1", "uri": "spotify:track:t1", "artists": [ { "name": "X" } ] } } ] }"""),
            _ when path.StartsWith("/v1/search", StringComparison.Ordinal) => (200, """{ "tracks": { "items": [ { "name": "Nađena", "uri": "spotify:track:s1", "artists": [] } ] } }"""),
            ("GET", "/v1/me/player/devices") => (200, """{ "devices": [ { "id": "d1", "name": "Mobitel", "type": "Smartphone", "is_active": false, "volume_percent": 30 } ] }"""),
            _ => (204, string.Empty),
        });
        using var client = new SpotifyClient("test", fake.Base);

        SpotifyPlayback p = (await client.PlaybackAsync("tok", CancellationToken.None))!;
        Assert.IsTrue(p.IsPlaying);
        Assert.AreEqual(("Pjesma", "Izvođač A, Izvođač B", "Album"), (p.Track, p.Artists, p.Album));
        Assert.AreEqual((61000L, 200000L, 55), (p.ProgressMs, p.DurationMs, p.Volume ?? -1));
        Assert.AreEqual("https://i.scdn.co/image/cover", p.ImageUrl);
        Assert.AreEqual("Moje računalo", p.DeviceName);

        Assert.AreEqual("spotify:playlist:p1", (await client.PlaylistsAsync("tok", CancellationToken.None)).Single().Uri);
        SpotifyItem saved = (await client.SavedTracksAsync("tok", CancellationToken.None)).Single();
        Assert.IsTrue(saved.IsTrack);
        Assert.AreEqual("Nađena", (await client.SearchTracksAsync("tok", "nešto", CancellationToken.None)).Single().Name);
        Assert.AreEqual("Mobitel", (await client.DevicesAsync("tok", CancellationToken.None)).Single().Name);

        await client.PlayAsync("tok", "spotify:playlist:p1", null, null, CancellationToken.None);
        await client.PauseAsync("tok", CancellationToken.None);
        await client.NextAsync("tok", CancellationToken.None);
        await client.SeekAsync("tok", 5000, CancellationToken.None);
        await client.VolumeAsync("tok", 140, CancellationToken.None);
        await client.TransferAsync("tok", "d1", play: true, CancellationToken.None);
        string[] commands = fake.Requests.Where(r => !r.StartsWith("GET", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "PUT /v1/me/player/play {\"context_uri\":\"spotify:playlist:p1\"}", "PUT /v1/me/player/pause", "POST /v1/me/player/next",
            "PUT /v1/me/player/seek?position_ms=5000", "PUT /v1/me/player/volume?volume_percent=100", "PUT /v1/me/player {\"device_ids\":[\"d1\"],\"play\":true}",
        }, commands);
        StringAssert.Contains(fake.Requests.First(r => r.StartsWith("GET /v1/search", StringComparison.Ordinal)), "limit=10");
    }

    [TestMethod]
    public async Task Nothing_playing_and_errors_are_mapped()
    {
        using var fake = new FakeSpotify((method, path, body) => (method, path) switch
        {
            ("GET", _) when path.StartsWith("/v1/me/player?", StringComparison.Ordinal) => (204, string.Empty),
            ("PUT", "/v1/me/player/play") => (404, """{ "error": { "status": 404, "message": "No active device", "reason": "NO_ACTIVE_DEVICE" } }"""),
            ("PUT", "/v1/me/player/pause") => (403, """{ "error": { "status": 403, "reason": "PREMIUM_REQUIRED" } }"""),
            ("POST", "/v1/me/player/next") => (401, """{ "error": { "status": 401, "message": "expired" } }"""),
            ("GET", "/v1/me/playlists?limit=50") => (403, """{ "error": { "status": 403, "message": "User not registered" } }"""),
            _ => (429, string.Empty),
        });
        using var client = new SpotifyClient("test", fake.Base);

        Assert.IsNull(await client.PlaybackAsync("tok", CancellationToken.None));
        Assert.AreEqual(SpotifyError.NoActiveDevice, (await Assert.ThrowsExceptionAsync<SpotifyException>(() => client.PlayAsync("tok", null, null, null, CancellationToken.None))).Kind);
        Assert.AreEqual(SpotifyError.PremiumRequired, (await Assert.ThrowsExceptionAsync<SpotifyException>(() => client.PauseAsync("tok", CancellationToken.None))).Kind);
        Assert.AreEqual(SpotifyError.Unauthorized, (await Assert.ThrowsExceptionAsync<SpotifyException>(() => client.NextAsync("tok", CancellationToken.None))).Kind);
        Assert.AreEqual(SpotifyError.Forbidden, (await Assert.ThrowsExceptionAsync<SpotifyException>(() => client.PlaylistsAsync("tok", CancellationToken.None))).Kind);
        Assert.AreEqual(SpotifyError.RateLimited, (await Assert.ThrowsExceptionAsync<SpotifyException>(() => client.DevicesAsync("tok", CancellationToken.None))).Kind);
    }

    [TestMethod]
    public void Real_client_only_talks_to_spotify_over_https()
    {
        // Lokalni testni poslužitelj prihvaća se samo na 127.0.0.1; svaka druga „testna” adresa vraća pravi Spotify.
        using var client = new SpotifyClient("test", new Uri("http://example.com/"));
        Assert.AreEqual(SpotifyClient.DefaultAccounts, client.Accounts);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
