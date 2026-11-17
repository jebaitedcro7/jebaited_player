using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Online;
using JebaitedPlayer.Services;
using JebaitedPlayer.Subtitles;
using JebaitedPlayer.Sync;

namespace JebaitedPlayer.ViewModels;

/// <summary>Redak u prozoru "Titlovi s interneta".</summary>
public sealed class CandidateRow(SubtitleCandidate candidate, bool isBest)
{
    public SubtitleCandidate Candidate { get; } = candidate;

    public bool IsBest { get; } = isBest;

    public string LanguageCode => Candidate.Language.ToUpperInvariant();

    public string Title => Candidate.DisplayName;

    public string Level => Candidate.Confidence switch
    {
        MatchConfidence.Sure => "sure",
        MatchConfidence.Likely => "likely",
        _ => "unsure",
    };

    public string ConfidenceText => OnlineSubtitleController.ConfidenceText(Candidate.Confidence);

    public string Details => string.Join(" · ", new[]
        {
            SubtitleLanguage.DisplayName(Candidate.Language),
            Candidate.Provider,
            ConfidenceText,
        }
        .Concat(Candidate.Reasons)
        .Append(Candidate.Downloads > 0 ? Loc.T("online.downloads", Candidate.Downloads) : null)
        .Where(s => !string.IsNullOrEmpty(s))!);
}

/// <summary>
/// Titlovi s interneta: automatska pretraga kad nema titlova na najvažnijem jeziku, automatsko učitavanje
/// samo kad je to isto izdanje, inače popis za ručni odabir. Preuzeti titlovi spremaju se uz video.
/// </summary>
public sealed class OnlineSubtitleController : ObservableObject
{
    private static readonly HttpClient Http = OnlineHttp.CreateClient($"jebaited_player v{AppPaths.Version.Split('-')[0]}");

    private readonly AppSettings _settings;
    private readonly PlayerViewModel _vm;
    private readonly SubtitleController _subtitles;
    private readonly IReadOnlyList<ISubtitleProvider>? _overrideProviders;
    private SubtitleSearchService _service;
    private SubtitleQuery? _query;
    private bool _busy;
    private bool _noticeShown;
    private Func<string> _status = () => string.Empty;
    private string _providerStatus = string.Empty;
    private string _queryTitle = string.Empty;
    private string _queryYear = string.Empty;
    private string _querySeason = string.Empty;
    private string _queryEpisode = string.Empty;

    public OnlineSubtitleController(AppSettings settings, PlayerViewModel vm, IReadOnlyList<ISubtitleProvider>? overrideProviders = null)
    {
        _settings = settings;
        _vm = vm;
        _subtitles = vm.Subtitles;
        _overrideProviders = overrideProviders;
        _service = BuildService();
        _subtitles.Settled += (_, e) => OnSubtitlesSettled(e);
        SearchCommand = new RelayCommand(() => _ = SearchManualAsync());
        LoadCommand = new RelayCommand<CandidateRow>(row => _ = DownloadAndLoadAsync(row.Candidate, auto: false));
    }

    public event Action<string>? Message;

    /// <summary>Provjera prema govoru prije automatskog učitavanja (M5). Bez nje vrijedi pravilo iz M4: samo "isto izdanje".</summary>
    public AutoSyncController? Verifier { get; set; }

    /// <summary>Najviše preuzimanja za automatsku provjeru po videu (štedi dnevni limit izvora).</summary>
    public const int MaxVerifyDownloads = 3;

    public ObservableCollection<CandidateRow> Results { get; } = [];

    public ICommand SearchCommand { get; }

    public ICommand LoadCommand { get; }

    /// <summary>Zadnja automatska radnja (za dijagnostiku i izvještaj).</summary>
    public string LastAutoResult { get; private set; } = string.Empty;

    public bool AnyProviderConfigured => _service.AnyAvailable;

    public bool IsBusy
    {
        get => _busy;
        private set => SetField(ref _busy, value);
    }

    /// <summary>Stanje pretrage na trenutnom jeziku (slaže se pri čitanju, pa prati promjenu jezika).</summary>
    public string Status => _status();

    /// <summary>Promjena jezika: stanje i opisi rezultata na novom jeziku.</summary>
    public void RefreshTexts()
    {
        for (int i = 0; i < Results.Count; i++)
        {
            Results[i] = new CandidateRow(Results[i].Candidate, Results[i].IsBest);
        }

        OnPropertyChanged(string.Empty);
    }

    private void SetStatus(Func<string> status)
    {
        _status = status;
        OnPropertyChanged(nameof(Status));
    }

    public string ProviderStatus
    {
        get => _providerStatus;
        private set => SetField(ref _providerStatus, value);
    }

    public string QueryTitle
    {
        get => _queryTitle;
        set => SetField(ref _queryTitle, value);
    }

    public string QueryYear
    {
        get => _queryYear;
        set => SetField(ref _queryYear, value);
    }

    public string QuerySeason
    {
        get => _querySeason;
        set => SetField(ref _querySeason, value);
    }

    public string QueryEpisode
    {
        get => _queryEpisode;
        set => SetField(ref _queryEpisode, value);
    }

    /// <summary>1 titl, 2 titla, 5 titlova, 21 titl…</summary>
    public static string CountText(int n) => Loc.Plural("online.count", n);

    public static string ConfidenceText(MatchConfidence confidence) => confidence switch
    {
        MatchConfidence.Sure => Loc.T("online.conf.sure"),
        MatchConfidence.Likely => Loc.T("online.conf.likely"),
        _ => Loc.T("online.conf.unsure"),
    };

    /// <summary>Nakon promjene ključeva u postavkama.</summary>
    public void RebuildProviders()
    {
        _service = BuildService();
        OnPropertyChanged(nameof(AnyProviderConfigured));
    }

    /// <summary>Otvaranje prozora pretrage: pripremi upit za trenutni video i pokreni pretragu ako još nema rezultata.</summary>
    public async Task PrepareForWindowAsync()
    {
        if (_vm.CurrentPath is not { } path)
        {
            return;
        }

        if (_query is null || !string.Equals(_query.VideoPath, path, StringComparison.OrdinalIgnoreCase))
        {
            Results.Clear();
            _query = await BuildQueryAsync(path, _settings.SubtitleLanguages);
            FillQueryFields(_query);
        }

        if (Results.Count == 0 && !IsBusy)
        {
            await SearchManualAsync();
        }
    }

    public async Task SearchManualAsync()
    {
        if (_vm.CurrentPath is not { } path)
        {
            return;
        }

        _query ??= await BuildQueryAsync(path, _settings.SubtitleLanguages);
        SubtitleQuery query = _query with
        {
            Languages = _settings.SubtitleLanguages.ToList(),
            Title = string.IsNullOrWhiteSpace(QueryTitle) ? _query.Info.Title : QueryTitle.Trim(),
            Year = ParseInt(QueryYear),
            Season = ParseInt(QuerySeason),
            Episode = ParseInt(QueryEpisode),
        };

        if (!_service.AnyAvailable)
        {
            SetStatus(() => Loc.T("online.noSources"));
            return;
        }

        await RunSearchAsync(query);
    }

    private async void OnSubtitlesSettled(SubtitlesSettledEventArgs e)
    {
        try
        {
            if (!_settings.Online.AutoSearch || e.Restored || e.VideoPath is null || _vm.IsAudioOnly)
            {
                return;
            }

            List<string> priority = _settings.SubtitleLanguages;
            int current = e.Language is null ? int.MaxValue : priority.IndexOf(e.Language);
            if (current == 0)
            {
                return;
            }

            List<string> wanted = priority.Take(current < 0 || current == int.MaxValue ? priority.Count : current).ToList();
            if (wanted.Count == 0)
            {
                return;
            }

            if (!_service.AnyAvailable)
            {
                if (!_noticeShown)
                {
                    _noticeShown = true;
                    Message?.Invoke(Loc.T("online.setupHint"));
                }

                return;
            }

            SubtitleQuery query = await BuildQueryAsync(e.VideoPath, wanted);
            _query = query;
            FillQueryFields(query);
            SearchOutcome outcome = await RunSearchAsync(query);
            if (!string.Equals(_vm.CurrentPath, e.VideoPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AutoDecision decision = SubtitleScorer.Decide(outcome.Candidates, query);
            string languages = string.Join(", ", wanted.Select(SubtitleLanguage.DisplayName));
            if (Verifier is { CanVerify: true } verifier && decision.Language is { } language &&
                await TryVerifiedAutoLoadAsync(verifier, outcome, query, language, e.VideoPath))
            {
                return;
            }

            if (decision.AutoLoad is { } sure)
            {
                LastAutoResult = $"auto: {sure.Provider} {sure.Language} {sure.DisplayName}";
                await DownloadAndLoadAsync(sure, auto: true);
            }
            else if (decision.Best is { } best)
            {
                LastAutoResult = $"ponuđeno: {best.Language} ({ConfidenceText(best.Confidence)})";
                Message?.Invoke(Loc.T("online.unsureFound", SubtitleLanguage.DisplayName(best.Language)));
            }
            else if (outcome.Providers.Any(p => p.Ok))
            {
                LastAutoResult = "nema rezultata";
                Message?.Invoke(Loc.T("online.noneFound", languages));
            }
            else
            {
                LastAutoResult = "greška: " + outcome.Providers.FirstOrDefault()?.Error;
                Message?.Invoke(Loc.T("online.searchFailed", outcome.Providers.FirstOrDefault()?.Error));
            }
        }
        catch (OperationCanceledException)
        {
            // Otvoren je drugi video dok je trajala provjera (analiza govora se prekida): to nije greška.
            Log.Info("Automatska pretraga titlova prekinuta (otvoren je drugi video).");
        }
        catch (Exception ex)
        {
            // Automatska pretraga nikad ne smije srušiti player.
            Log.Error("Automatska pretraga titlova", ex);
        }
    }

    /// <summary>
    /// M5: najbolji kandidati jezika (najviše <see cref="MaxVerifyDownloads"/>) preuzmu se u memoriju i provjere prema govoru.
    /// Učitava se prvi koji se poklapa s govorom; "isto izdanje" i kad provjera nije moguća. Titlovi koji se ne poklapaju
    /// s govorom se ne učitavaju. Vraća false kad ništa nije odlučeno (tada vrijedi pravilo iz M4).
    /// </summary>
    private async Task<bool> TryVerifiedAutoLoadAsync(AutoSyncController verifier, SearchOutcome outcome, SubtitleQuery query,
        string language, string video)
    {
        List<SubtitleCandidate> pool = outcome.Candidates.Where(c => c.Language == language)
            .OrderByDescending(c => c.Confidence).ThenByDescending(c => c.Score).Take(MaxVerifyDownloads).ToList();
        if (pool.Count == 0)
        {
            return false;
        }

        bool SameVideo() => string.Equals(_vm.CurrentPath, video, StringComparison.OrdinalIgnoreCase);
        int mismatched = 0;
        IsBusy = true;
        try
        {
            foreach (SubtitleCandidate c in pool)
            {
                SetStatus(() => Loc.T("online.verifying", c.DisplayName));
                DownloadedSubtitle file;
                try
                {
                    file = await _service.DownloadAsync(c, query, CancellationToken.None);
                }
                catch (Exception ex) when (ex is ProviderException or HttpRequestException or IOException or TaskCanceledException
                                               or System.Text.Json.JsonException)
                {
                    Log.Warn($"Preuzimanje za provjeru nije uspjelo ({c.Provider}): {ex.Message}");
                    continue;
                }

                SyncResult? r;
                try
                {
                    r = await verifier.VerifyAsync(SubtitleService.ReadCues(file.Content, file.FileName, _subtitles.VideoFrameRate), CancellationToken.None);
                }
                catch (InvalidDataException ex)
                {
                    Log.Warn($"Preuzeti titl nije čitljiv ({c.Provider}/{c.DownloadReference}): {ex.Message}");
                    continue;
                }

                if (!SameVideo())
                {
                    return true;
                }

                Log.Info($"Provjera prema govoru {c.Provider}/{c.DownloadReference} ({c.DisplayName}): {r?.Verdict.ToString() ?? "nije moguća"} {r?.Detail}");
                bool accept = r is { IsUsable: true } || (c.Confidence == MatchConfidence.Sure && r?.Verdict != SyncVerdict.Mismatch);
                if (accept)
                {
                    string path = SaveDownloaded(file, video, c);
                    _subtitles.LoadFiles([path]);
                    LastAutoResult = $"auto: {c.Provider} {c.Language} {c.DisplayName} (govor: {r?.Verdict.ToString() ?? "bez provjere"})";
                    SetStatus(() => Loc.T("online.loaded", Path.GetFileName(path)));
                    Message?.Invoke(Loc.T("online.downloadedAuto", SubtitleLanguage.DisplayName(c.Language), c.Provider));
                    return true;
                }

                if (r?.Verdict == SyncVerdict.Mismatch)
                {
                    mismatched++;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (mismatched == 0)
        {
            return false;
        }

        LastAutoResult = $"odbačeno: {mismatched} (ne poklapa se s govorom)";
        SetStatus(() => Loc.T("online.mismatchStatus", SubtitleLanguage.DisplayName(language)));
        Message?.Invoke(Loc.T("online.mismatchMessage", SubtitleLanguage.DisplayName(language)));
        return true;
    }

    private async Task<SearchOutcome> RunSearchAsync(SubtitleQuery query)
    {
        IsBusy = true;
        string what = $"{query.Title}{(query.IsEpisode ? $" S{query.Season:00}E{query.Episode:00}" : query.Year is int y ? $" ({y})" : string.Empty)}";
        SetStatus(() => Loc.T("online.searching", what));
        try
        {
            SearchOutcome outcome = await _service.SearchAsync(query, useCache: true, CancellationToken.None);
            Results.Clear();
            AutoDecision decision = SubtitleScorer.Decide(outcome.Candidates, query);
            foreach (SubtitleCandidate c in outcome.Candidates)
            {
                Results.Add(new CandidateRow(c, ReferenceEquals(c, decision.Best)));
            }

            ProviderStatus = string.Join(" · ", outcome.Providers.Select(p =>
                p.Ok ? $"{p.Provider}: {p.Count}{(p.FromCache ? $" ({Loc.T("online.cached")})" : string.Empty)}" : $"{p.Provider}: {p.Error}"));
            int found = outcome.Candidates.Count;
            SetStatus(() => found == 0 ? Loc.T("online.noResults") : Loc.T("online.found", CountText(found)));
            Log.Info($"Pretraga titlova '{query.Title}': {ProviderStatus}");
            return outcome;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DownloadAndLoadAsync(SubtitleCandidate candidate, bool auto)
    {
        if (_query is null || _vm.CurrentPath is not { } video)
        {
            return;
        }

        IsBusy = true;
        SetStatus(() => Loc.T("online.downloading", candidate.DisplayName));
        try
        {
            DownloadedSubtitle downloaded = await _service.DownloadAsync(candidate, _query, CancellationToken.None);
            string path = SaveDownloaded(downloaded, video, candidate);
            _subtitles.LoadFiles([path]);
            string text = $"{SubtitleLanguage.DisplayName(candidate.Language)} ({candidate.Provider}, {ConfidenceText(candidate.Confidence)})";
            SetStatus(() => Loc.T("online.loaded", Path.GetFileName(path)));
            Message?.Invoke(auto ? Loc.T("online.downloadedText", text) : Loc.T("online.loadedText", text));
            Log.Info($"Preuzeti titlovi {candidate.Provider}/{candidate.DownloadReference}{(_service.LastDownloadFromCache ? " (iz predmemorije, bez trošenja limita)" : string.Empty)} → {path}");
        }
        catch (Exception ex) when (ex is ProviderException or HttpRequestException or IOException or UnauthorizedAccessException
                                       or TaskCanceledException or System.Text.Json.JsonException)
        {
            SetStatus(() => Loc.T("online.downloadFailed", ex.Message));
            Message?.Invoke(Status);
            Log.Warn(Status);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Spremi uz video kao "Film.hr.srt" (sljedeći put se učita sam), inače u mapu aplikacije.</summary>
    private string SaveDownloaded(DownloadedSubtitle downloaded, string video, SubtitleCandidate candidate)
    {
        string ext = Path.GetExtension(downloaded.FileName).ToLowerInvariant();
        if (!SubtitleFormats.IsSubtitleFile("x" + ext) || ext == ".idx")
        {
            ext = ".srt";
        }

        string baseName = Path.GetFileNameWithoutExtension(video);
        var folders = new List<string>();
        if (_settings.Online.SaveNextToVideo && Path.GetDirectoryName(video) is { } videoDir)
        {
            folders.Add(videoDir);
        }

        folders.Add(AppPaths.DownloadedSubtitlesDir);
        string provider = new string(candidate.Provider.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        foreach (string folder in folders)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string[] names =
                [
                    $"{baseName}.{candidate.Language}{ext}",
                    $"{baseName}.{candidate.Language}.{provider}{ext}",
                    .. Enumerable.Range(2, 8).Select(i => $"{baseName}.{candidate.Language}.{provider}{i}{ext}"),
                ];
                foreach (string name in names)
                {
                    string target = Path.Combine(folder, name);
                    if (File.Exists(target))
                    {
                        if (File.ReadAllBytes(target).AsSpan().SequenceEqual(downloaded.Content))
                        {
                            return target;
                        }

                        continue;
                    }

                    File.WriteAllBytes(target, downloaded.Content);
                    return target;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Titlovi se ne mogu spremiti u {folder}: {ex.Message}");
            }
        }

        throw new IOException(Loc.T("online.saveFailed"));
    }

    private async Task<SubtitleQuery> BuildQueryAsync(string videoPath, IReadOnlyList<string> languages)
    {
        MediaInfo info = MediaNameParser.Parse(videoPath);
        (string? hash, long size) = await Task.Run(() =>
        {
            try
            {
                return (OpenSubtitlesHash.Compute(videoPath), new FileInfo(videoPath).Length);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return ((string?)null, 0L);
            }
        });
        return new SubtitleQuery(videoPath, info, hash, size, _subtitles.VideoFrameRate, languages);
    }

    private void FillQueryFields(SubtitleQuery query)
    {
        QueryTitle = query.Title;
        QueryYear = query.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        QuerySeason = query.Season?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        QueryEpisode = query.Episode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private SubtitleSearchService BuildService()
    {
        if (_overrideProviders != null)
        {
            return new SubtitleSearchService(_overrideProviders, null);
        }

        OnlineSettings o = _settings.Online;
        var providers = new List<ISubtitleProvider>();
        if (o.OpenSubtitlesEnabled)
        {
            providers.Add(CreateOpenSubtitles(Http, _settings));
        }

        if (o.SubDlEnabled)
        {
            providers.Add(new SubDlProvider(Http, () => Secret.Unprotect(_settings.Online.SubDlApiKey)));
        }

        if (o.TitloviEnabled)
        {
            providers.Add(CreateTitlovi(Http, _settings));
        }

        return new SubtitleSearchService(providers, AppPaths.SearchCacheDir, downloadCacheDir: AppPaths.DownloadCacheDir);
    }

    /// <summary>Osobni API ključ ako ga je korisnik upisao, inače ključ aplikacije iz izdanja (vidi <see cref="AppSecrets"/>).</summary>
    internal static OpenSubtitlesProvider CreateOpenSubtitles(HttpClient http, AppSettings settings) =>
        new(http, () => OnlineSetup.OpenSubtitlesCredentials(settings.Online));

    internal static TitloviProvider CreateTitlovi(HttpClient http, AppSettings settings) =>
        new(http, () => settings.Online.TitloviUsername is { Length: > 0 } user && Secret.Unprotect(settings.Online.TitloviPassword) is { Length: > 0 } pass
            ? new TitloviCredentials(user, pass)
            : null);

    internal static HttpClient SharedHttp => Http;

    private static int? ParseInt(string? text) =>
        int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : null;
}
