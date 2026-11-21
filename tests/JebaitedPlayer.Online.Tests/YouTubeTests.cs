using System.Net;
using System.Net.Sockets;
using System.Text;
using JebaitedPlayer.Online;

namespace JebaitedPlayer.Online.Tests;

/// <summary>Ciklus 6, V9: linkovi i YouTube Data API klijent (simulirani poslužitelj).</summary>
[TestClass]
public sealed class YouTubeTests
{
    [TestMethod]
    public void Links_from_youtube_and_youtube_music_are_understood()
    {
        Assert.AreEqual(new YouTubeTarget("M7lc1UVf-VE", null), YouTubeLinks.Parse("https://www.youtube.com/watch?v=M7lc1UVf-VE"));
        Assert.AreEqual(new YouTubeTarget("M7lc1UVf-VE", null), YouTubeLinks.Parse("youtu.be/M7lc1UVf-VE"));
        Assert.AreEqual(new YouTubeTarget("M7lc1UVf-VE", null), YouTubeLinks.Parse("M7lc1UVf-VE"));
        Assert.AreEqual(new YouTubeTarget("abcdefghijk", "PLxyz1234567890"), YouTubeLinks.Parse("https://music.youtube.com/watch?v=abcdefghijk&list=PLxyz1234567890"));
        Assert.AreEqual(new YouTubeTarget(null, "OLAK5uy_abcdefghij"), YouTubeLinks.Parse("https://music.youtube.com/playlist?list=OLAK5uy_abcdefghij"));
        Assert.AreEqual(new YouTubeTarget("abcdefghijk", null), YouTubeLinks.Parse("https://www.youtube.com/shorts/abcdefghijk"));
        Assert.IsNull(YouTubeLinks.Parse("https://example.com/watch?v=abcdefghijk"));
        Assert.IsNull(YouTubeLinks.Parse("https://evilyoutube.com/watch?v=abcdefghijk"));
        Assert.AreEqual(new YouTubeTarget("abcdefghijk", null), YouTubeLinks.Parse("https://m.youtube.com/watch?v=abcdefghijk"));
        Assert.IsNull(YouTubeLinks.Parse("nije link"));
        Assert.IsNull(YouTubeLinks.Parse("javascript:alert(1)"));
    }

    private static (Uri Base, Task Server, List<string> Paths) Serve(Func<string, (int, string)> route, int requests)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var paths = new List<string>();
        Task server = Task.Run(async () =>
        {
            for (int i = 0; i < requests; i++)
            {
                using TcpClient c = await listener.AcceptTcpClientAsync();
                NetworkStream s = c.GetStream();
                var buf = new byte[8192];
                int n = await s.ReadAsync(buf);
                string path = Encoding.ASCII.GetString(buf, 0, n).Split(' ')[1];
                paths.Add(path);
                (int status, string body) = route(path);
                byte[] payload = Encoding.UTF8.GetBytes(body);
                await s.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} X\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
                await s.WriteAsync(payload);
            }

            listener.Stop();
        });
        return (new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/"), server, paths);
    }

    [TestMethod]
    public async Task Search_and_playlist_items_are_parsed()
    {
        (Uri baseUri, Task server, List<string> paths) = Serve(path => path.StartsWith("/youtube/v3/search", StringComparison.Ordinal)
            ? (200, """{ "items": [ { "id": { "videoId": "aaaaaaaaaaa" }, "snippet": { "title": "Pjesma &amp; spot", "channelTitle": "Kanal", "thumbnails": { "medium": { "url": "https://i.ytimg.com/a.jpg" } } } } ] }""")
            : (200, """{ "items": [ { "snippet": { "title": "Prva", "videoOwnerChannelTitle": "Izvođač", "resourceId": { "videoId": "bbbbbbbbbbb" } } }, { "snippet": { "title": "Deleted video", "resourceId": { "videoId": "x" } } } ] }"""), 2);
        using var client = new YouTubeClient("test", baseUri);

        YouTubeItem found = (await client.SearchMusicAsync("KEY", "neka pjesma", CancellationToken.None)).Single();
        Assert.AreEqual(("aaaaaaaaaaa", "Pjesma & spot", "Kanal"), (found.VideoId, found.Title, found.Channel));
        YouTubeItem item = (await client.PlaylistItemsAsync("KEY", "PL123456789", CancellationToken.None)).Single();
        Assert.AreEqual(("bbbbbbbbbbb", "Izvođač"), (item.VideoId, item.Channel));
        await server;
        StringAssert.Contains(paths[0], "videoCategoryId=10");
        StringAssert.Contains(paths[0], "maxResults=10");
        StringAssert.Contains(paths[1], "playlistId=PL123456789");
    }

    [TestMethod]
    public async Task Quota_and_bad_key_are_recognised()
    {
        (Uri baseUri, Task server, _) = Serve(path => path.Contains("q=a", StringComparison.Ordinal)
            ? (403, """{ "error": { "code": 403, "errors": [ { "reason": "quotaExceeded" } ] } }""")
            : (400, """{ "error": { "code": 400, "message": "API key not valid. Please pass a valid API key.", "errors": [ { "reason": "badRequest" } ] } }"""), 2);
        using var client = new YouTubeClient("test", baseUri);
        Assert.AreEqual(YouTubeError.Quota, (await Assert.ThrowsExceptionAsync<YouTubeException>(() => client.SearchMusicAsync("K", "a", CancellationToken.None))).Kind);
        Assert.AreEqual(YouTubeError.BadKey, (await Assert.ThrowsExceptionAsync<YouTubeException>(() => client.SearchMusicAsync("K", "b", CancellationToken.None))).Kind);
        await server;
    }
}
