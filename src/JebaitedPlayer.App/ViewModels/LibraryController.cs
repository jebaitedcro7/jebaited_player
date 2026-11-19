using System.Collections.ObjectModel;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;

namespace JebaitedPlayer.ViewModels;

/// <summary>Jedna stavka popisa (epizoda ili film).</summary>
public sealed class PlaylistItem(string path, MediaInfo info) : ObservableObject
{
    private double _progress;
    private bool _watched;
    private bool _isCurrent;
    private string _progressText = string.Empty;
    private WatchEntry? _entry;

    public string Path { get; } = path;
    public MediaInfo Info { get; } = info;

    public string Label => Info.IsEpisode
        ? Info.EpisodeCode + (Info.EpisodeTitle is { Length: > 0 } t ? " · " + t : string.Empty)
        : Info.Year is int y ? $"{Info.Title} ({y})" : System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>Naslov skupine u popisu ("Serija · Sezona 1").</summary>
    public string Group => Info.IsEpisode
        ? Info.Season is int s ? Loc.T("playlist.seasonGroup", Info.Title, s) : Info.Title
        : Loc.T("playlist.moviesGroup");

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public bool Watched
    {
        get => _watched;
        set => SetField(ref _watched, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetField(ref _isCurrent, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }

    /// <summary>Promjena jezika: tekst napretka i naziv skupine na novom jeziku.</summary>
    public void RefreshTexts()
    {
        Apply(_entry);
        OnPropertyChanged(nameof(Group));
    }

    public void Apply(WatchEntry? entry)
    {
        _entry = entry;
        Watched = entry?.Watched ?? false;
        Progress = entry?.WatchedProgress ?? 0;
        ProgressText = Watched ? Loc.T("playlist.watched")
            : entry is { Position: >= 30, Duration: > 0 } e ? $"{TimeFormat.Format(e.Position, e.Duration >= 3600)} / {TimeFormat.Format(e.Duration, e.Duration >= 3600)}"
            : string.Empty;
    }
}

/// <summary>Stavka "Nastavi gledati" na početnom ekranu (pogledano: natpis i puna zelena traka).</summary>
public sealed record RecentItem(string Path, string Title, string Detail, double Progress, bool Watched = false);

/// <summary>Omiljeni film ili serija na početnom ekranu.</summary>
public sealed record FavoriteItem(string Path, string Title, string Detail, bool IsSeries)
{
    public string Glyph => IsSeries ? "" : "";
}

/// <summary>
/// Popis epizoda i filmova, nastavak gledanja (uz pitanje), sljedeća epizoda, spremanje pozicije i povijest.
/// </summary>
public sealed class LibraryController : ObservableObject
{
    private const double MinResumeSeconds = 30;
    private const double EndFraction = 0.95;
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    // Ciklus 1 (M6): stvarno odgledani dijelovi trenutnog videa. Premotavanje se ne broji.
    private WatchCoverage? _coverage;
    private string? _coveragePath;
    private bool _coverageWasWatched;
    private double _trackPosition;
    private long _trackTick;

    private readonly PlayerViewModel _vm;
    private readonly WatchStore? _store;
    private PlaylistItem? _current;
    private string _playlistTitle = string.Empty;
    private double? _resumeOffer;
    private string? _pendingResumePath;
    private double? _pendingResumePosition;
    private bool _pendingResumeAuto;
    private PlaylistItem? _upNext;
    private int _upNextSeconds;
    private long _upNextDeadline;
    private long _lastSave;
    private bool _isCurrentFavorite;
    private string? _playlistTitleKey;

    public LibraryController(PlayerViewModel vm, WatchStore? store)
    {
        _vm = vm;
        _store = store;
        PlayCommand = new RelayCommand<PlaylistItem>(PlayItem);
        ToggleWatchedCommand = new RelayCommand<PlaylistItem>(ToggleWatched);
        OpenRecentCommand = new RelayCommand<RecentItem>(item => OpenRecentRequested?.Invoke(this, item.Path));
        RemoveRecentCommand = new RelayCommand<RecentItem>(RemoveRecent);
        ResumeContinueCommand = new RelayCommand(ResumeContinue);
        ResumeFromStartCommand = new RelayCommand(ResumeFromStart);
        UpNextPlayCommand = new RelayCommand(UpNextPlayNow);
        UpNextCancelCommand = new RelayCommand(UpNextCancel);
        ToggleFavoriteCommand = new RelayCommand(ToggleFavorite);
        OpenFavoriteCommand = new RelayCommand<FavoriteItem>(item => OpenRecentRequested?.Invoke(this, item.Path));
        RemoveFavoriteCommand = new RelayCommand<FavoriteItem>(RemoveFavorite);

        vm.MediaLoaded += (_, path) => OnMediaLoaded(path);
        vm.EndReached += (_, _) => OnEndReached();
        vm.EndCleared += (_, _) => UpNextCancel();
    }

    public event Action<string>? Message;

    /// <summary>Korisnik je kliknuo stavku "Nastavi gledati" (otvara se kao datoteka, s okolnim epizodama).</summary>
    public event EventHandler<string>? OpenRecentRequested;

    /// <summary>Otvorena je mapa: prikaži popis epizoda.</summary>
    public event EventHandler? PlaylistOpened;

    public ObservableCollection<PlaylistItem> Playlist { get; } = [];

    public ObservableCollection<RecentItem> Recent { get; } = [];

    public ICommand PlayCommand { get; }
    public ICommand ToggleWatchedCommand { get; }
    public ICommand OpenRecentCommand { get; }
    public ICommand RemoveRecentCommand { get; }
    public ICommand ResumeContinueCommand { get; }
    public ICommand ResumeFromStartCommand { get; }
    public ICommand UpNextPlayCommand { get; }
    public ICommand UpNextCancelCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand OpenFavoriteCommand { get; }
    public ICommand RemoveFavoriteCommand { get; }

    public ObservableCollection<FavoriteItem> Favorites { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>Početni ekran ima povijest ili omiljene (tada se prečaci prikazuju tek na klik).</summary>
    public bool HasLibraryContent => Recent.Count > 0 || Favorites.Count > 0;

    /// <summary>Je li trenutni film (ili serija trenutne epizode) među omiljenima.</summary>
    public bool IsCurrentFavorite
    {
        get => _isCurrentFavorite;
        private set
        {
            if (SetField(ref _isCurrentFavorite, value))
            {
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteToolTip));
            }
        }
    }

    public string FavoriteGlyph => IsCurrentFavorite ? "" : "";

    public string FavoriteToolTip => IsCurrentFavorite ? Loc.T("controls.removeFavorite") : Loc.T("controls.addFavorite");

    /// <summary>Koliko sekundi traje odbrojavanje do sljedeće epizode.</summary>
    public int UpNextDelaySeconds { get; set; } = 10;

    /// <summary>Isključeno: na kraju epizode kartica samo ponudi sljedeću, bez odbrojavanja.</summary>
    public bool AutoPlayNext { get; set; } = true;

    /// <summary>
    /// Ponovno otvaranje započetog videa (UPGRADE 3): "ask" = kartica "Nastaviti gledanje?" (zadano), "resume" = nastavi
    /// sam od spremljene pozicije, "start" = od početka (pozicija se i dalje sprema za "Nastavi gledati").
    /// </summary>
    public string ResumeMode { get; set; } = "ask";

    public string PlaylistTitle
    {
        get => _playlistTitle;
        private set => SetField(ref _playlistTitle, value);
    }

    public PlaylistItem? Current
    {
        get => _current;
        private set => SetField(ref _current, value);
    }

    public bool HasPlaylist => Playlist.Count > 1;

    public bool HasRecent => Recent.Count > 0;

    public double? ResumeOffer
    {
        get => _resumeOffer;
        private set
        {
            if (SetField(ref _resumeOffer, value))
            {
                OnPropertyChanged(nameof(HasResumeOffer));
                OnPropertyChanged(nameof(ResumeText));
                OnPropertyChanged(nameof(IsPromptVisible));
            }
        }
    }

    public bool HasResumeOffer => ResumeOffer.HasValue;

    public string ResumeText => ResumeOffer is double p ? Loc.T("cards.resumeFrom", TimeFormat.Format(p, p >= 3600)) : string.Empty;

    public PlaylistItem? UpNext
    {
        get => _upNext;
        private set
        {
            if (SetField(ref _upNext, value))
            {
                OnPropertyChanged(nameof(HasUpNext));
                OnPropertyChanged(nameof(IsPromptVisible));
                OnPropertyChanged(nameof(UpNextText));
            }
        }
    }

    public bool HasUpNext => UpNext != null;

    public int UpNextSeconds
    {
        get => _upNextSeconds;
        private set
        {
            if (SetField(ref _upNextSeconds, value))
            {
                OnPropertyChanged(nameof(UpNextText));
            }
        }
    }

    public string UpNextText => UpNext is null ? string.Empty : AutoPlayNext ? Loc.T("cards.nextIn", UpNextSeconds) : Loc.T("menu.nextEpisode");

    public bool IsPromptVisible => HasResumeOffer || HasUpNext;

    /// <summary>Otvara datoteke i/ili mape i pokreće pravu stavku.</summary>
    public async Task OpenAsync(IReadOnlyList<string> files, IReadOnlyList<string> folders)
    {
        List<string> paths;
        string? start;
        string title;
        bool fromFolder = folders.Count > 0;

        if (fromFolder)
        {
            List<ScanResult> scans = await Task.Run(() => folders.Select(f => MediaScanner.ScanFolder(f)).ToList());
            paths = MediaScanner.Sort(scans.SelectMany(s => s.Files).Concat(files).Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
            if (paths.Count == 0)
            {
                Message?.Invoke(Loc.T("notice.noVideosInFolder"));
                return;
            }

            int skipped = scans.Sum(s => s.SkippedSamples);
            if (skipped > 0)
            {
                Log.Info($"Preskočeno {skipped} \"sample\" datoteka.");
            }

            start = PickStart(paths);
            _playlistTitleKey = folders.Count == 1 ? null : "playlist.selectedFolders";
            title = _playlistTitleKey is null ? Path.GetFileName(folders[0].TrimEnd('\\', '/')) : Loc.T(_playlistTitleKey);
        }
        else if (files.Count == 1)
        {
            MediaInfo info = MediaNameParser.Parse(files[0]);
            // Ciklus 2: jedna pjesma otvara cijeli album (ostale pjesme iz iste mape).
            bool audio = MediaScanner.IsAudio(files[0]);
            paths = info.IsEpisode ? (await Task.Run(() => EpisodeNeighbours(files[0]))).ToList()
                : audio ? (await Task.Run(() => MediaScanner.SiblingAudio(files[0]))).ToList()
                : [files[0]];
            start = files[0];
            _playlistTitleKey = null;
            title = info.IsEpisode ? info.Title : Path.GetFileName(Path.GetDirectoryName(files[0])) ?? string.Empty;
        }
        else
        {
            paths = MediaScanner.Sort(files).ToList();
            start = paths.FirstOrDefault();
            _playlistTitleKey = "playlist.selectedFiles";
            title = Loc.T(_playlistTitleKey);
        }

        SetPlaylist(paths, title);
        PlaylistItem? first = Playlist.FirstOrDefault(p => string.Equals(p.Path, start, StringComparison.OrdinalIgnoreCase))
                              ?? Playlist.FirstOrDefault();
        if (first != null)
        {
            PlayItem(first);
        }

        if (fromFolder && HasPlaylist)
        {
            PlaylistOpened?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PlayItem(PlaylistItem item)
    {
        SaveCurrentPosition();
        ResumeOffer = null;
        UpNext = null;

        WatchEntry? entry = Safe(() => _store?.Get(item.Path));
        bool resume = entry is { Watched: false } e && e.Position >= MinResumeSeconds &&
                      (e.Duration <= 0 || e.Position < e.Duration - 20);
        if (ResumeMode == "start" || MediaScanner.IsAudio(item.Path))
        {
            // Pjesma uvijek kreće od početka (ciklus 2, mini player).
            resume = false;
        }

        _pendingResumePath = resume ? item.Path : null;
        _pendingResumePosition = resume ? entry!.Position : null;
        _pendingResumeAuto = resume && ResumeMode == "resume";

        foreach (PlaylistItem p in Playlist)
        {
            p.IsCurrent = ReferenceEquals(p, item);
        }

        Current = item;
        _vm.Load(item.Path, startPaused: resume, item.Info.DisplayTitle);
    }

    public void PlayNext()
    {
        if (Neighbour(+1) is { } next)
        {
            PlayItem(next);
        }
        else
        {
            Message?.Invoke(Loc.T("notice.lastInList"));
        }
    }

    public void PlayPrevious()
    {
        if (Neighbour(-1) is { } previous)
        {
            PlayItem(previous);
        }
        else
        {
            Message?.Invoke(Loc.T("notice.firstInList"));
        }
    }

    public void ResumeContinue()
    {
        if (ResumeOffer is null)
        {
            return;
        }

        ResumeOffer = null;
        _vm.SetPaused(false);
    }

    public void ResumeFromStart()
    {
        if (ResumeOffer is null)
        {
            return;
        }

        ResumeOffer = null;
        _vm.SeekAbsolute(0, exact: true);
        _vm.SetPaused(false);
    }

    public void UpNextPlayNow()
    {
        if (UpNext is { } next)
        {
            UpNext = null;
            PlayItem(next);
        }
    }

    public void UpNextCancel() => UpNext = null;

    /// <summary>
    /// Tipka Home: spremi poziciju (pa se pri ponovnom otvaranju nudi nastavak) i makni kartice.
    /// Dok je otvorena kartica "Nastaviti gledanje?", ostaje ranije spremljena pozicija.
    /// </summary>
    public void LeaveToHome()
    {
        SaveCurrentPosition();
        ResumeOffer = null;
        UpNext = null;
    }

    /// <summary>Poziva UI timer: povremeno spremanje pozicije i odbrojavanje do sljedeće epizode.</summary>
    public void OnTick()
    {
        long now = Environment.TickCount64;
        TrackPlayback(now);
        if (UpNext != null && AutoPlayNext)
        {
            int remaining = (int)Math.Ceiling((_upNextDeadline - now) / 1000.0);
            if (remaining <= 0)
            {
                UpNextPlayNow();
                return;
            }

            UpNextSeconds = remaining;
        }

        if (now - _lastSave >= SaveInterval.TotalMilliseconds && _vm.HasMedia && !_vm.IsPaused)
        {
            SaveCurrentPosition();
        }
    }

    /// <summary>Sprema poziciju trenutne datoteke (i pri promjeni datoteke, pauzi i zatvaranju).</summary>
    public void SaveCurrentPosition()
    {
        _lastSave = Environment.TickCount64;
        if (_store is null || _vm.CurrentPath is not { } path || !_vm.HasMedia || _vm.IsLoading || ResumeOffer != null)
        {
            return;
        }

        double duration = _vm.Duration;
        double position = _vm.Position;
        if (duration <= 0)
        {
            return;
        }

        // M6b: pogledano samo uz stvarno odgledano vrijeme (premotavanje do kraja se ne broji).
        // M6a: već pogledan video ostaje pogledan i kad se ponovo otvori i zatvori ranije (baza ga ne može poništiti).
        WatchCoverage? coverage = string.Equals(_coveragePath, path, StringComparison.OrdinalIgnoreCase) ? _coverage : null;
        bool watched = (coverage != null && _coverageWasWatched) || coverage?.IsWatched == true;
        bool atEnd = _vm.EofReached || position >= duration * EndFraction;
        double saved = watched && atEnd ? 0 : position;
        string? coverageText = coverage?.ToText();
        Safe(() => _store.SavePosition(path, saved, duration, watched, coverageText));
        if (Current is { } item && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            item.Apply(Safe(() => _store.Get(path)) ??
                       new WatchEntry(path, null, null, null, duration, saved, watched, null, 0, null, coverageText));
        }
    }

    /// <summary>
    /// Svakih 200 ms: dio odgledan od zadnjeg poziva broji se samo ako je pozicija napredovala koliko i stvarno vrijeme
    /// (uz brzinu reprodukcije). Skok premotavanja, pauza i kartica „Nastaviti gledanje?” se ne broje.
    /// </summary>
    private void TrackPlayback(long now)
    {
        if (_coverage is null || !_vm.HasMedia || _vm.IsLoading ||
            !string.Equals(_vm.CurrentPath, _coveragePath, StringComparison.OrdinalIgnoreCase))
        {
            _trackTick = 0;
            return;
        }

        double position = _vm.Position;
        if (_trackTick != 0 && !_vm.IsPaused && ResumeOffer is null)
        {
            double elapsed = (now - _trackTick) / 1000.0;
            double advanced = position - _trackPosition;
            double expected = elapsed * Math.Max(0.25, _vm.Extras.Speed);
            if (advanced > 0 && advanced <= expected * 1.5 + 0.75)
            {
                _coverage.AddPlayed(_trackPosition, position, _vm.Duration);
            }
        }

        _trackPosition = position;
        _trackTick = now;
    }

    /// <summary>Novi video: učitaj što je od njega već stvarno odgledano.</summary>
    private void StartTracking(string path)
    {
        WatchEntry? entry = Safe(() => _store?.Get(path));
        _coverage = WatchCoverage.FromEntry(entry);
        _coveragePath = path;
        _coverageWasWatched = entry?.Watched ?? false;
        _trackTick = 0;
    }

    public void RefreshRecent()
    {
        if (_store is null)
        {
            return;
        }

        IReadOnlyList<WatchEntry> entries = Safe(() => _store.Recent(100)) ?? [];
        bool anyFavorites = (Safe(() => _store.Favorites()) ?? []).Count > 0;
        Recent.Clear();

        // S omiljenima povijest je kraća, da se na početnom ekranu odmah vidi i jedno i drugo.
        // Glazba ne puni „Nastavi gledati” (album od 12 pjesama bi istisnuo filmove i serije).
        foreach (WatchEntry e in entries.Where(e => !MediaScanner.IsAudio(e.Path) && File.Exists(e.Path)).Take(anyFavorites ? 5 : 8))
        {
            MediaInfo info = MediaNameParser.Parse(e.Path);
            string detail = e.Watched ? Loc.T("history.watched")
                : e.Position >= MinResumeSeconds && e.Duration > 0
                    ? Loc.T("history.stoppedAt", TimeFormat.Format(e.Position, e.Duration >= 3600), TimeFormat.Format(e.Duration, e.Duration >= 3600))
                    : Loc.T("history.started");
            Recent.Add(new RecentItem(e.Path, info.DisplayTitle, detail, e.WatchedProgress, e.Watched));
        }

        OnPropertyChanged(nameof(HasRecent));
        RefreshFavorites();
    }

    public void RefreshFavorites()
    {
        IReadOnlyList<Favorite> all = Safe(() => _store?.Favorites()) ?? [];
        Favorites.Clear();
        foreach (Favorite f in all.Where(f => f.IsSeries ? Directory.Exists(f.Path) : File.Exists(f.Path)))
        {
            Favorites.Add(new FavoriteItem(f.Path, f.Title, f.IsSeries ? Loc.T("history.seriesDetail", f.Path) : Loc.T("history.movieDetail", Path.GetFileName(f.Path)), f.IsSeries));
        }

        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasLibraryContent));
        UpdateCurrentFavorite();
    }

    /// <summary>Promjena jezika: tekstovi koje kontroler slaže sam (popis, povijest, omiljeno, kartice) na novom jeziku.</summary>
    public void RefreshTexts()
    {
        if (_playlistTitleKey != null)
        {
            PlaylistTitle = Loc.T(_playlistTitleKey);
        }

        foreach (PlaylistItem item in Playlist)
        {
            item.RefreshTexts();
        }

        RefreshRecent();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Ctrl+D: film se dodaje kao datoteka, a epizoda kao cijela serija (mapa serije), pa se iz omiljenih
    /// nastavlja od epizode na kojoj si stao.
    /// </summary>
    public void ToggleFavorite()
    {
        if (_store is null || FavoriteTarget() is not { } target)
        {
            Message?.Invoke(Loc.T("notice.openMediaFirst"));
            return;
        }

        bool remove = Safe(() => _store.IsFavorite(target.Path));
        if (remove)
        {
            Safe(() => _store.RemoveFavorite(target.Path));
        }
        else
        {
            Safe(() => _store.AddFavorite(target.Path, target.Title, target.Kind));
        }

        RefreshFavorites();
        Message?.Invoke(remove ? Loc.T("notice.favoriteRemoved", target.Title) : Loc.T("notice.favoriteAdded", target.Title));
    }

    /// <summary>Što bi se dodalo u omiljene za trenutni video: (putanja, naslov, "movie" ili "series").</summary>
    public (string Path, string Title, string Kind)? FavoriteTarget()
    {
        if (_vm.CurrentPath is not { } path)
        {
            return null;
        }

        MediaInfo info = Current is { } c && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase) ? c.Info : MediaNameParser.Parse(path);
        if (info.IsEpisode && Path.GetDirectoryName(path) is { } folder)
        {
            string root = MediaNameParser.IsSeasonFolder(Path.GetFileName(folder)) && Path.GetDirectoryName(folder) is { } parent ? parent : folder;
            return (root, string.IsNullOrWhiteSpace(info.Title) ? Path.GetFileName(root) : info.Title, "series");
        }

        return (path, info.DisplayTitle, "movie");
    }

    private void RemoveFavorite(FavoriteItem item)
    {
        Safe(() => _store?.RemoveFavorite(item.Path));
        RefreshFavorites();
    }

    private void UpdateCurrentFavorite() =>
        IsCurrentFavorite = _store != null && FavoriteTarget() is { } t && Safe(() => _store.IsFavorite(t.Path));

    private void RemoveRecent(RecentItem item)
    {
        Safe(() => _store?.Remove(item.Path));
        RefreshRecent();
    }

    private void ToggleWatched(PlaylistItem item)
    {
        bool watched = !item.Watched;
        Safe(() => _store?.SetWatched(item.Path, watched));
        if (string.Equals(item.Path, _coveragePath, StringComparison.OrdinalIgnoreCase))
        {
            // Ručna oznaka vrijedi i za video koji se upravo gleda (nepogledano počinje brojati ispočetka).
            _coverageWasWatched = watched;
            if (!watched)
            {
                _coverage?.Clear();
            }
        }

        item.Apply(Safe(() => _store?.Get(item.Path)));
        Message?.Invoke(watched ? Loc.T("notice.markedWatched", item.Label) : Loc.T("notice.markedUnwatched", item.Label));
    }

    private void SetPlaylist(IReadOnlyList<string> paths, string title)
    {
        IReadOnlyDictionary<string, WatchEntry> entries =
            Safe(() => _store?.GetMany(paths)) ?? new Dictionary<string, WatchEntry>();
        Playlist.Clear();
        foreach (string path in paths)
        {
            var item = new PlaylistItem(path, MediaNameParser.Parse(path));
            item.Apply(entries.TryGetValue(path, out WatchEntry? e) ? e : null);
            Playlist.Add(item);
        }

        PlaylistTitle = title;
        OnPropertyChanged(nameof(HasPlaylist));
    }

    /// <summary>Gdje krenuti kad se otvori mapa: započeta epizoda (zadnja gledana) → prva nepogledana → prva.</summary>
    private string? PickStart(IReadOnlyList<string> paths)
    {
        IReadOnlyDictionary<string, WatchEntry> entries =
            Safe(() => _store?.GetMany(paths)) ?? new Dictionary<string, WatchEntry>();
        string? inProgress = entries.Values
            .Where(e => !e.Watched && e.Position >= MinResumeSeconds)
            .OrderByDescending(e => e.LastPlayed)
            .Select(e => paths.First(p => string.Equals(p, e.Path, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault();
        return inProgress
               ?? paths.FirstOrDefault(p => !(entries.TryGetValue(p, out WatchEntry? e) && e.Watched))
               ?? paths.FirstOrDefault();
    }

    /// <summary>
    /// Ostale epizode uz otvorenu: ako je datoteka u mapi sezone ("Season 01"), uzima cijelu seriju
    /// (sve sezone), da "sljedeća epizoda" prijeđe i u novu sezonu. Inače samo istu mapu.
    /// </summary>
    private static IReadOnlyList<string> EpisodeNeighbours(string file)
    {
        string? folder = Path.GetDirectoryName(file);
        string? seriesRoot = folder is null ? null : Path.GetDirectoryName(folder);
        if (folder != null && seriesRoot != null && MediaNameParser.IsSeasonFolder(Path.GetFileName(folder)))
        {
            string title = MediaNameParser.NormalizeTitle(MediaNameParser.Parse(file).Title);
            List<string> series = MediaScanner.ScanFolder(seriesRoot, maxDepth: 2, maxFiles: 1000).Files
                .Where(f => MediaNameParser.NormalizeTitle(MediaNameParser.Parse(f).Title) == title)
                .ToList();
            if (series.Contains(file, StringComparer.OrdinalIgnoreCase))
            {
                return series;
            }
        }

        return MediaScanner.SiblingVideos(file);
    }

    private PlaylistItem? Neighbour(int direction)
    {
        if (Current is null)
        {
            return null;
        }

        int index = Playlist.IndexOf(Current) + direction;
        return index >= 0 && index < Playlist.Count ? Playlist[index] : null;
    }

    private void OnMediaLoaded(string path)
    {
        if (_pendingResumePosition is double position &&
            string.Equals(_pendingResumePath, path, StringComparison.OrdinalIgnoreCase))
        {
            // Video je učitan pauziran: pokaži kadar na kojem si stao i pitaj (ili, uz "resume", samo nastavi).
            _vm.SeekAbsolute(position, exact: true);
            if (_pendingResumeAuto)
            {
                _vm.SetPaused(false);
                Message?.Invoke(Loc.T("notice.resumedAt", TimeFormat.Format(position, position >= 3600)));
            }
            else
            {
                ResumeOffer = position;
            }
        }

        _pendingResumeAuto = false;

        _pendingResumePath = null;
        _pendingResumePosition = null;

        MediaInfo info = Current is { } c && string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase)
            ? c.Info
            : MediaNameParser.Parse(path);
        Safe(() => _store?.RegisterPlay(path, info.Title, info.Season, info.Episode));
        StartTracking(path);
        UpdateCurrentFavorite();

        if (_store != null && File.Exists(path))
        {
            WatchStore store = _store;
            _ = Task.Run(() =>
            {
                try
                {
                    var file = new FileInfo(path);
                    store.SaveHash(path, file.Length, OpenSubtitlesHash.Compute(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn($"Hash nije izračunat: {ex.Message}");
                }
            });
        }
    }

    private void OnEndReached()
    {
        // Kraj videa ne znači da je pogledan (M6b): status dolazi iz stvarno odgledanog dijela.
        SaveCurrentPosition();

        // Ciklus 2: glazba ide odmah na sljedeću pjesmu, bez kartice i odbrojavanja (kao u svakom music playeru).
        if (Current is { } current && MediaScanner.IsAudio(current.Path) && Neighbour(+1) is { } nextSong)
        {
            PlayItem(nextSong);
            return;
        }

        if (Neighbour(+1) is { } next)
        {
            UpNext = next;
            UpNextSeconds = UpNextDelaySeconds;
            _upNextDeadline = Environment.TickCount64 + UpNextDelaySeconds * 1000L;
        }
    }

    private static T? Safe<T>(Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            Log.Warn($"Baza: {ex.Message}");
            return default;
        }
    }

    private static void Safe(Action action) => Safe<object?>(() =>
    {
        action();
        return null;
    });
}
