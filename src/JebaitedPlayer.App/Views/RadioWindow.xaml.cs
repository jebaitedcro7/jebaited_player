using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Library;
using JebaitedPlayer.Online;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Jedan redak u popisu postaja.</summary>
public sealed class RadioRow(string url, string name, string detail) : ObservableObject
{
    private bool _isFavorite;
    private bool _isPlaying;

    /// <summary>Ova postaja upravo svira (zelena, ikona zvučnika).</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetField(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayGlyph));
            }
        }
    }

    public string PlayGlyph => IsPlaying ? "\uE767" : "\uE768";

    public string Url { get; } = url;
    public string Name { get; } = name;
    public string Detail { get; } = detail;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (SetField(ref _isFavorite, value))
            {
                OnPropertyChanged(nameof(StarGlyph));
            }
        }
    }

    public string StarGlyph => IsFavorite ? "" : "";
}

/// <summary>Stanje reprodukcije za prozor Radio (koja datoteka, učitava li se, pozicija, greška).</summary>
internal readonly record struct RadioPlayback(string? Path, bool Loading, double Position, string Error);

/// <summary>
/// Radio (ciklus 3, V3): pretraga postaja preko Radio Browsera po nazivu, žanru i državi, omiljene postaje u library.db
/// i ručni URL (http/https; m3u i pls otvara mpv). Pokrenuta postaja ide u mini player s glazbenim equalizerom.
/// </summary>
public partial class RadioWindow : Window
{
    private static readonly string[] FallbackTags =
        ["pop", "rock", "jazz", "classical", "electronic", "dance", "news", "talk", "hiphop", "chillout", "lounge", "oldies", "80s", "90s", "folk", "metal"];

    private readonly Action<string, string> _play;
    private readonly WatchStore? _store;
    private readonly RadioBrowserClient _client = new($"jebaited_player/{AppPaths.Version} (+https://github.com/jebaitedcro7/jebaited_player)");
    private readonly ObservableCollection<RadioRow> _results = [];
    private readonly ObservableCollection<RadioRow> _favorites = [];
    private CancellationTokenSource? _search;
    private IReadOnlyList<string>? _tags;
    private IReadOnlyList<RadioCountry>? _countries;
    private string? _tag;
    private RadioCountry? _country;
    private readonly Func<RadioPlayback> _playback;
    private readonly System.Windows.Threading.DispatcherTimer _watch;
    private RadioRow? _started;
    private long _startedAt;

    /// <summary>Tekst stanja (dijagnostika).</summary>
    internal string Status => StatusText.Text;

    /// <param name="play">Pokreni stream (URL, naziv postaje).</param>
    internal RadioWindow(Action<string, string> play, WatchStore? store, Func<RadioPlayback> playback)
    {
        _play = play;
        _store = store;
        _playback = playback;
        InitializeComponent();
        _watch = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(500),
            System.Windows.Threading.DispatcherPriority.Background, (_, _) => CheckPlayback(), Dispatcher);
        _watch.Stop();
        WindowTheme.UseDarkTitleBar(this);
        ResultsTab.Checked += (_, _) => ShowList();
        FavoritesTab.Checked += (_, _) => ShowList();
        Closed += (_, _) =>
        {
            _watch.Stop();
            _search?.Cancel();
            _client.Dispose();
        };
        this.OnLanguageChanged(UpdateTexts);
        LoadFavorites();
        UpdateTexts();
        ShowList();
        StatusText.Text = Loc.T("radio.intro");
    }

    internal int ResultCount => _results.Count;

    /// <summary>Dijagnostika: polje za pretragu (provjera da se upisani tekst vidi).</summary>
    internal System.Windows.Controls.TextBox SearchTextBox => SearchBox;

    internal IReadOnlyList<RadioRow> Results => _results;

    internal IReadOnlyList<RadioRow> FavoriteRows => _favorites;

    private void UpdateTexts()
    {
        GenreButton.Content = _tag ?? Loc.T("radio.allGenres");
        CountryButton.Content = _country?.Name ?? Loc.T("radio.allCountries");
    }

    private void ShowList() => StationList.ItemsSource = FavoritesTab.IsChecked == true ? _favorites : _results;

    private void LoadFavorites()
    {
        _favorites.Clear();
        foreach (RadioFavorite f in Safe(() => _store?.RadioFavorites()) ?? [])
        {
            _favorites.Add(new RadioRow(f.Url, f.Name, f.Detail ?? string.Empty) { IsFavorite = true });
        }

        HashSet<string> urls = _favorites.Select(f => f.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RadioRow r in _results)
        {
            r.IsFavorite = urls.Contains(r.Url);
        }
    }

    // --- Pretraga ------------------------------------------------------------------------------------------------

    private async void OnSearch(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text, _tag, _country?.Code);

    /// <summary>Pretraga (gumb, dijagnostika). Vraća broj nađenih postaja.</summary>
    internal async Task<int> SearchAsync(string? name, string? tag, string? countryCode)
    {
        _search?.Cancel();
        var cts = new CancellationTokenSource();
        _search = cts;
        ResultsTab.IsChecked = true;
        StatusText.Text = Loc.T("radio.searching");
        try
        {
            IReadOnlyList<RadioStation> stations = await _client.SearchAsync(name, tag, countryCode, 60, cts.Token);
            HashSet<string> favorites = _favorites.Select(f => f.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _results.Clear();
            foreach (RadioStation s in stations)
            {
                _results.Add(new RadioRow(s.Url, s.Name, Describe(s)) { IsFavorite = favorites.Contains(s.Url) });
            }

            StatusText.Text = _results.Count == 0 ? Loc.T("radio.nothingFound") : Loc.T("radio.found", _results.Count);
            Log.Info($"Radio: pretraga, {_results.Count} postaja.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusText.Text = Loc.T("radio.offline");
            Log.Warn($"Radio Browser nije dostupan: {ex.Message}");
        }

        return _results.Count;
    }

    private static string Describe(RadioStation s)
    {
        var parts = new List<string>();
        if (s.Country.Length > 0)
        {
            parts.Add(s.Country);
        }

        if (s.Codec.Length > 0 || s.Bitrate > 0)
        {
            parts.Add($"{s.Codec}{(s.Bitrate > 0 ? $" {s.Bitrate} kbps" : string.Empty)}".Trim());
        }

        if (s.Tags.Length > 0)
        {
            parts.Add(string.Join(", ", s.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(3)));
        }

        return string.Join(" · ", parts);
    }

    private async void OnGenreClick(object sender, RoutedEventArgs e)
    {
        _tags ??= await LoadAsync(c => _client.TagsAsync(c)) ?? FallbackTags;
        var choices = new List<(string, bool, Action)> { (Loc.T("radio.allGenres"), _tag is null, () => SetTag(null)) };
        choices.AddRange(_tags.Select(t => (t, t == _tag, (Action)(() => SetTag(t)))));
        ShowChoices(GenreButton, choices);
    }

    private async void OnCountryClick(object sender, RoutedEventArgs e)
    {
        _countries ??= await LoadAsync(c => _client.CountriesAsync(c));
        var choices = new List<(string, bool, Action)> { (Loc.T("radio.allCountries"), _country is null, () => SetCountry(null)) };
        choices.AddRange((_countries ?? []).Take(80).Select(c => ($"{c.Name} ({c.Stations})", c.Code == _country?.Code, (Action)(() => SetCountry(c)))));
        ShowChoices(CountryButton, choices);
    }

    private void SetTag(string? tag)
    {
        _tag = tag;
        UpdateTexts();
        _ = SearchAsync(SearchBox.Text, _tag, _country?.Code);
    }

    private void SetCountry(RadioCountry? country)
    {
        _country = country;
        UpdateTexts();
        _ = SearchAsync(SearchBox.Text, _tag, _country?.Code);
    }

    private async Task<T?> LoadAsync<T>(Func<CancellationToken, Task<T>> load) where T : class
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            return await load(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            StatusText.Text = Loc.T("radio.offline");
            return null;
        }
    }

    // --- Reprodukcija i favoriti ----------------------------------------------------------------------------------

    private void OnPlayRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RadioRow row })
        {
            Play(row);
        }
    }

    internal void Play(RadioRow row)
    {
        _started = row;
        _startedAt = Environment.TickCount64;
        MarkPlaying(null);
        StatusText.Text = Loc.T("radio.connecting", row.Name);
        _play(row.Url, row.Name);
        _watch.Start();
    }

    /// <summary>
    /// Nakon klika: „Povezujem…” dok stream ne krene, zatim „Svira…” i zelena postaja; ako ne krene u 15 s ili javi grešku,
    /// poruka da postaja ne radi (mnoge postaje u imeniku povremeno ne rade).
    /// </summary>
    private void CheckPlayback()
    {
        if (_started is not { } row)
        {
            _watch.Stop();
            return;
        }

        RadioPlayback s = _playback();
        if (!string.Equals(s.Path, row.Url, StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.TickCount64 - _startedAt > 3000)
            {
                MarkPlaying(null);
                _started = null;
            }

            return;
        }

        if (!s.Loading && s.Position > 0.3 && s.Error.Length == 0)
        {
            MarkPlaying(row);
            StatusText.Text = Loc.T("radio.playing", row.Name);
        }
        else if (s.Error.Length > 0 || Environment.TickCount64 - _startedAt > 15000)
        {
            MarkPlaying(null);
            StatusText.Text = Loc.T("radio.failed", row.Name);
            _started = null;
            Log.Warn("Radio: postaja nije krenula.");
        }
    }

    private void MarkPlaying(RadioRow? row)
    {
        foreach (RadioRow r in _results.Concat(_favorites))
        {
            r.IsPlaying = row is not null && string.Equals(r.Url, row.Url, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnPlayUrl(object sender, RoutedEventArgs e) => PlayUrl(UrlBox.Text);

    /// <summary>Ručni URL (gumb, dijagnostika). Neispravan URL se ne pokreće.</summary>
    internal bool PlayUrl(string? text)
    {
        string url = text?.Trim() ?? string.Empty;
        if (!RadioBrowserClient.IsStreamUrl(url))
        {
            StatusText.Text = Loc.T("radio.badUrl");
            return false;
        }

        Play(new RadioRow(url, Uri.TryCreate(url, UriKind.Absolute, out Uri? u) ? u.Host : url, string.Empty));
        return true;
    }

    private void OnStarRow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RadioRow row })
        {
            ToggleFavorite(row);
        }
    }

    internal void ToggleFavorite(RadioRow row)
    {
        if (row.IsFavorite)
        {
            Safe(() =>
            {
                _store?.RemoveRadioFavorite(row.Url);
                return true;
            });
        }
        else
        {
            Safe(() =>
            {
                _store?.AddRadioFavorite(row.Url, row.Name, row.Detail);
                return true;
            });
        }

        LoadFavorites();
        row.IsFavorite = _favorites.Any(f => string.Equals(f.Url, row.Url, StringComparison.OrdinalIgnoreCase));
    }

    private static void ShowChoices(Button button, IEnumerable<(string Text, bool IsChecked, Action Choose)> choices)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MaxHeight = 420 };
        foreach (var (text, isChecked, choose) in choices)
        {
            var item = new MenuItem { Header = text.Replace("_", "__", StringComparison.Ordinal), IsChecked = isChecked };
            item.Click += (_, _) => choose();
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private static T? Safe<T>(Func<T?> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            Log.Warn($"Radio favoriti: {ex.Message}");
            return default;
        }
    }
}
