using System.IO;
using System.Text.Json;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Services.News;

public enum NewsCategory { Important = 0, Update = 1, New = 2, Tip = 3 }

public enum NewsStatus { Unread, Read, Dismissed }

/// <summary>Što gumb u detaljima obavijesti radi (izvršava glavni prozor).</summary>
public enum NewsAction
{
    None,
    OpenAppearanceSettings,
    OpenAudioSettings,
    OpenUpdates,
    OpenNotifications,
#if TORLINK
    OpenTorlink,
#endif
}

/// <summary>Jedna obavijest. Tekstovi su ključevi prijevoda (6 jezika), a ne tekst, pa nema mrežnog sadržaja.</summary>
public sealed record NewsItem(
    string Id,
    NewsCategory Category,
    string TitleKey,
    string BodyKey,
    string DetailKey,
    string ActionKey,
    NewsAction Action,
    string? Arg = null,
    DateTime? ExpiresUtc = null)
{
    public string Title => Arg is null ? Loc.T(TitleKey) : Loc.T(TitleKey, Arg);

    public string Body => Arg is null ? Loc.T(BodyKey) : Loc.T(BodyKey, Arg);

    public string Detail => Arg is null ? Loc.T(DetailKey) : Loc.T(DetailKey, Arg);

    public string ActionText => ActionKey.Length == 0 ? string.Empty : Loc.T(ActionKey);

    public string CategoryText => Loc.T("news.cat." + Category.ToString().ToLowerInvariant());
}

/// <summary>Stanje jedne obavijesti (sprema se u <c>notifications.json</c>, ne u settings.json).</summary>
public sealed class NewsState
{
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public NewsStatus Status { get; set; }

    /// <summary>U koliko je pokretanja playera kartica bila prikazana na Home ekranu.</summary>
    public int ShownCount { get; set; }
}

/// <summary>Pohrana stanja obavijesti (zamjenjiva u testovima).</summary>
public interface INewsStateStore
{
    Dictionary<string, NewsState> Load();

    void Save(Dictionary<string, NewsState> state);
}

/// <summary>Stanje u <c>%LOCALAPPDATA%\jebaited_player\notifications.json</c> (izvan postavki, pa Odbaci/Zadano ne dira pročitano).</summary>
public sealed class FileNewsStateStore(string path) : INewsStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static FileNewsStateStore Default => new(Path.Combine(AppPaths.DataDir, "notifications.json"));

    public Dictionary<string, NewsState> Load()
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<Dictionary<string, NewsState>>(File.ReadAllText(path)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Obavijesti: stanje se ne može pročitati, kreće ispočetka: {ex.Message}");
        }

        return [];
    }

    public void Save(Dictionary<string, NewsState> state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Obavijesti: stanje se ne može spremiti: {ex.Message}");
        }
    }
}

/// <summary>
/// Obavijesti na Home ekranu i u Postavkama. Izvori su samo provjereni: (1) popis ugrađen u player (tekstovi u svih 6 jezika) i
/// (2) obavijest o novoj verziji iz <see cref="UpdateService"/> (isti izvor kao nadogradnja). Nema vanjskog sadržaja.
/// </summary>
public sealed class NewsCenter
{
    /// <summary>Koliko se puta (pokretanja playera) ista kartica smije pojaviti na Home ekranu.</summary>
    public const int MaxShowings = 3;

    private readonly INewsStateStore _store;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, NewsState> _state;
    private readonly HashSet<string> _shownThisSession = [];
    private readonly List<NewsItem> _catalog;
    private NewsItem? _update;

    public NewsCenter(INewsStateStore store, IEnumerable<NewsItem>? catalog = null, Func<DateTime>? utcNow = null)
    {
        _store = store;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _catalog = [.. catalog ?? NewsCatalog.BuiltIn];
        _state = store.Load();
    }

    public event EventHandler? Changed;

    /// <summary>Sve obavijesti (i pročitane i odbačene), po važnosti: Important → Update → New → Tip.</summary>
    public IReadOnlyList<NewsItem> All => Items().ToList();

    public NewsStatus StatusOf(string id) => _state.TryGetValue(id, out NewsState? s) ? s.Status : NewsStatus.Unread;

    public int ShownCountOf(string id) => _state.TryGetValue(id, out NewsState? s) ? s.ShownCount : 0;

    public int UnreadCount => Items().Count(i => StatusOf(i.Id) == NewsStatus.Unread);

    /// <summary>
    /// Kartica koja se sada prikazuje: prva nepročitana, neodbačena, neistekla i prikazana manje od <see cref="MaxShowings"/> puta.
    /// Obavijest koja je već prikazana u ovom pokretanju ostaje prikazana (ne računa se ponovno).
    /// </summary>
    public NewsItem? ActiveCard => Items().FirstOrDefault(i =>
        StatusOf(i.Id) == NewsStatus.Unread && (_shownThisSession.Contains(i.Id) || ShownCountOf(i.Id) < MaxShowings));

    /// <summary>Kartica je stvarno prikazana na Home ekranu: broji se jednom po pokretanju playera.</summary>
    public void NoteShown(string id)
    {
        if (_shownThisSession.Add(id))
        {
            State(id).ShownCount++;
            Persist(raise: false);
        }
    }

    public void MarkRead(string id)
    {
        NewsState s = State(id);
        if (s.Status == NewsStatus.Unread)
        {
            s.Status = NewsStatus.Read;
            Persist();
        }
    }

    /// <summary>X na kartici: odbačeno, ne vraća se na Home, ali ostaje u popisu.</summary>
    public void Dismiss(string id)
    {
        NewsState s = State(id);
        if (s.Status != NewsStatus.Dismissed)
        {
            s.Status = NewsStatus.Dismissed;
            Persist();
        }
    }

    public void MarkAllRead()
    {
        foreach (NewsItem item in Items().Where(i => StatusOf(i.Id) == NewsStatus.Unread).ToList())
        {
            State(item.Id).Status = NewsStatus.Read;
        }

        Persist();
    }

    /// <summary>Verzija koja je preuzeta i čeka restart (iz <see cref="UpdateService"/>); null = nema.</summary>
    public void SetUpdateVersion(string? version)
    {
        NewsItem? next = string.IsNullOrWhiteSpace(version)
            ? null
            : new NewsItem("update-" + version, NewsCategory.Update, "news.update.title", "news.update.body", "news.update.detail",
                "news.update.action", NewsAction.OpenUpdates, version);
        if (next?.Id == _update?.Id)
        {
            return;
        }

        _update = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<NewsItem> Items()
    {
        DateTime now = _utcNow();
        IEnumerable<NewsItem> all = _update is null ? _catalog : _catalog.Prepend(_update);
        return all.Where(i => i.ExpiresUtc is null || i.ExpiresUtc > now)
            .Select((item, index) => (item, index))
            .OrderBy(t => (int)t.item.Category).ThenBy(t => t.index)
            .Select(t => t.item);
    }

    private NewsState State(string id)
    {
        if (!_state.TryGetValue(id, out NewsState? s))
        {
            _state[id] = s = new NewsState();
        }

        return s;
    }

    private void Persist(bool raise = true)
    {
        _store.Save(_state);
        if (raise)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>Ugrađene obavijesti (istinite o ovom izdanju; bez lažnih „ažuriranja”).</summary>
public static class NewsCatalog
{
    public static IReadOnlyList<NewsItem> BuiltIn { get; } =
    [
        new("notifications-intro", NewsCategory.New, "news.intro.title", "news.intro.body", "news.intro.detail",
            "news.intro.action", NewsAction.OpenNotifications),
        new("mini-designs", NewsCategory.New, "news.designs.title", "news.designs.body", "news.designs.detail",
            "news.designs.action", NewsAction.OpenAppearanceSettings),
#if TORLINK
        new("torlink-intro", NewsCategory.Tip, "torlink.news.title", "torlink.news.body", "torlink.news.detail",
            "torlink.news.action", NewsAction.OpenTorlink),
#endif
        new("tip-equalizer", NewsCategory.Tip, "news.eq.title", "news.eq.body", "news.eq.detail",
            "news.eq.action", NewsAction.OpenAudioSettings),
    ];
}
