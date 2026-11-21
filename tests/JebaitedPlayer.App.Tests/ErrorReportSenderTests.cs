using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.App.Tests;

/// <summary>Ciklus 6, N1: slanje izvještaja (u aplikaciji, jer projekt Diagnostics nema mrežni kod).</summary>
[TestClass]
public sealed class ErrorReportSenderTests
{
    private static readonly DateTime T0 = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);

    private static ErrorReport Report(string message, DateTime at) =>
        ErrorReport.Create(LogLevel.Error, "playback", "file.error", "Test", message, null, "1.8.4", "GitHub", at);

    [TestMethod]
    public async Task Sender_removes_only_what_the_server_accepted()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jp-reports-" + Guid.NewGuid().ToString("N"));
        var q = new ErrorReportQueue(dir);
        q.Add(Report("a", T0), T0);
        q.Add(Report("b", T0.AddSeconds(1)), T0);
        int accepted = 0;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task server = Task.Run(async () =>
        {
            for (int i = 0; i < 2; i++)
            {
                using TcpClient c = await listener.AcceptTcpClientAsync();
                NetworkStream s = c.GetStream();
                var buf = new byte[16384];
                _ = await s.ReadAsync(buf); // zahtjev se ne provjerava, samo se pročita
                string status = accepted++ == 0 ? "200 OK" : "500 X";
                await s.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            }
        });
        using var http = new HttpClient();
        int sent = await new ErrorReportSender(http, new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/report")).SendAsync(q, CancellationToken.None);
        await server;
        listener.Stop();
        Assert.AreEqual(1, sent);
        Assert.AreEqual("b", q.Pending().Single().Report.Message, "Odbijeni izvještaj ostaje u redu.");
        Directory.Delete(dir, recursive: true);
    }
}
