using System.Windows.Threading;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Services;

/// <summary>N1 (ciklus 6): pristanak za slanje izvještaja o greškama. Zadano isključeno dok korisnik ne odgovori.</summary>
public sealed class ErrorReportSettings
{
    /// <summary>Korisnik je dopustio slanje redigiranih izvještaja.</summary>
    public bool SendReports { get; set; }

    /// <summary>Pitanje o pristanku je već postavljeno (pita se jednom).</summary>
    public bool ConsentAsked { get; set; }
}

/// <summary>
/// N1: greške i padovi postaju redigirani izvještaji u lokalnom redu (logs\reports). Šalju se samo uz pristanak i samo
/// kad je postavljena prijemna točka (HTTPS). Dok je nema, korisnik može sam poslati pripremljen GitHub issue.
/// </summary>
internal sealed class ErrorReporting : IDisposable
{
    /// <summary>
    /// Prijemna točka (npr. Cloudflare Worker iz tools\diag-worker). Prazno dok je vlasnik ne postavi: izvještaji tada
    /// samo čekaju lokalno i ništa se ne šalje.
    /// </summary>
    public const string Endpoint = "";

    public const string Repository = "https://github.com/jebaitedcro7/jebaited_player";

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _sendTimer;
    private Uri? _endpoint = Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? u) && u.Scheme == Uri.UriSchemeHttps ? u : null;
    private HttpClient? _http;

    public ErrorReporting(AppSettings settings, string logsDir)
    {
        _settings = settings;
        Queue = new ErrorReportQueue(Path.Combine(logsDir, "reports"));
        Queue.Prune(DateTime.UtcNow);
        _sendTimer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Background, async (_, _) =>
        {
            _sendTimer!.Stop();
            await SendAsync();
        }, Dispatcher.CurrentDispatcher);
        _sendTimer.Stop();
        DiagLog.Problem += OnProblem;
    }

    public ErrorReportQueue Queue { get; }

    public bool HasEndpoint => _endpoint is not null;

    public int LastSent { get; private set; }

    /// <summary>Dijagnostika: lokalni testni poslužitelj umjesto prave prijemne točke.</summary>
    internal void UseTestEndpoint(Uri endpoint)
    {
        _endpoint = endpoint;
        _http?.Dispose();
        _http = null;
    }

    private void OnProblem(LogLevel level, string category, string type, string component, string message, Exception? ex)
    {
        ErrorReport report = ErrorReport.Create(level, category, type, component, message, ex, AppPaths.Version,
            AppPackage.IsStore ? "Store" : "GitHub", DateTime.UtcNow);
        if (Queue.Add(report, DateTime.UtcNow) && _settings.ErrorReports.SendReports && HasEndpoint)
        {
            // Nekoliko grešaka zaredom šalje se zajedno.
            _sendTimer.Dispatcher.BeginInvoke(() => _sendTimer.Start());
        }
    }

    /// <summary>Pošalji red (uz pristanak i prijemnu točku). Vraća broj poslanih.</summary>
    public async Task<int> SendAsync()
    {
        if (!_settings.ErrorReports.SendReports || _endpoint is null)
        {
            return 0;
        }

        _http ??= Online.OnlineHttp.CreateClient($"jebaited_player/{AppPaths.Version}");
        LastSent = await new ErrorReportSender(_http, _endpoint).SendAsync(Queue, CancellationToken.None);
        if (LastSent > 0)
        {
            Log.Info($"Dijagnostika: poslano izvještaja {LastSent}.");
        }

        return LastSent;
    }

    public string GitHubIssueUrl() => Queue.GitHubIssueUrl(Repository, AppPaths.Version);

    public void Dispose()
    {
        DiagLog.Problem -= OnProblem;
        _sendTimer.Stop();
        _http?.Dispose();
    }
}

/// <summary>N1: slanje izvještaja na prijemnu točku (HTTPS, JSON). Uspješno poslani se brišu; pri grešci ostaju u redu.
/// Namjerno je u aplikaciji, a ne u projektu Diagnostics, koji ne smije imati mrežni kod.</summary>
internal sealed class ErrorReportSender(System.Net.Http.HttpClient http, Uri endpoint)
{
    public async Task<int> SendAsync(ErrorReportQueue queue, CancellationToken ct)
    {
        int sent = 0;
        foreach ((string file, ErrorReport report) in queue.Pending())
        {
            try
            {
                using var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(report), System.Text.Encoding.UTF8, "application/json");
                using System.Net.Http.HttpResponseMessage response = await http.PostAsync(endpoint, content, ct);
                if (!response.IsSuccessStatusCode)
                {
                    break;
                }

                queue.Remove(file);
                sent++;
            }
            catch (System.Net.Http.HttpRequestException)
            {
                break;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                break;
            }
        }

        return sent;
    }
}
