namespace JebaitedPlayer.Iptv;

public enum ChannelSort
{
    Original,
    NameAsc,
    NameDesc,
    Manual,
    Category,
    FavoritesFirst,
    Recent,
}

/// <summary>Korisnikove postavke kanala jednog izvora: favoriti, skriveno, ručni redoslijed, nedavno. Ključ je Id kanala.</summary>
public sealed class IptvUserData
{
    public List<string> Favorites { get; set; } = [];
    public List<string> HiddenChannels { get; set; } = [];
    public List<string> HiddenCategories { get; set; } = [];
    public List<string> ManualOrder { get; set; } = [];
    public List<string> Recent { get; set; } = [];

    public bool IsFavorite(string id) => Favorites.Contains(id);

    /// <summary>Dodaj/ukloni favorita bez duplikata. Vraća novo stanje.</summary>
    public bool ToggleFavorite(string id)
    {
        if (!Favorites.Remove(id))
        {
            Favorites.Add(id);
            return true;
        }

        return false;
    }

    public void MarkWatched(string id, int max = 30)
    {
        Recent.Remove(id);
        Recent.Insert(0, id);
        if (Recent.Count > max)
        {
            Recent.RemoveRange(max, Recent.Count - max);
        }
    }

    /// <summary>Premjesti kanal u ručnom redoslijedu (indeks unutar zadanog popisa ID-jeva).</summary>
    public void Move(IReadOnlyList<string> currentOrder, string id, int newIndex)
    {
        List<string> order = [.. currentOrder];
        order.Remove(id);
        order.Insert(Math.Clamp(newIndex, 0, order.Count), id);
        ManualOrder = order;
    }
}

public sealed record ChannelFilter(
    string? Search = null,
    string? Category = null,
    bool FavoritesOnly = false,
    bool RecentOnly = false,
    bool IncludeHidden = false,
    ChannelSort Sort = ChannelSort.Original);

public static class ChannelQuery
{
    public static IReadOnlyList<IptvChannel> Apply(IReadOnlyList<IptvChannel> all, IptvUserData user, ChannelFilter f)
    {
        IEnumerable<IptvChannel> q = all;
        if (!f.IncludeHidden)
        {
            var hc = new HashSet<string>(user.HiddenChannels);
            var hg = new HashSet<string>(user.HiddenCategories, StringComparer.OrdinalIgnoreCase);
            q = q.Where(c => !hc.Contains(c.Id) && !hg.Contains(c.Group));
        }

        if (!string.IsNullOrWhiteSpace(f.Category))
        {
            q = q.Where(c => c.Group.Equals(f.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (f.FavoritesOnly)
        {
            var fav = new HashSet<string>(user.Favorites);
            q = q.Where(c => fav.Contains(c.Id));
        }

        if (f.RecentOnly)
        {
            var rec = new HashSet<string>(user.Recent);
            q = q.Where(c => rec.Contains(c.Id));
        }

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            string[] words = f.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            q = q.Where(c => words.All(w =>
                c.Name.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                || c.Group.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                || (c.EpgId?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        IEnumerable<IptvChannel> sorted = f.Sort switch
        {
            ChannelSort.NameAsc => q.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            ChannelSort.NameDesc => q.OrderByDescending(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            ChannelSort.Category => q.OrderBy(c => c.Group, StringComparer.CurrentCultureIgnoreCase),
            ChannelSort.FavoritesFirst => q.OrderBy(c => user.IsFavorite(c.Id) ? 0 : 1),
            ChannelSort.Manual => OrderManual(q, user.ManualOrder),
            ChannelSort.Recent => q.OrderBy(c => { int i = user.Recent.IndexOf(c.Id); return i < 0 ? int.MaxValue : i; }),
            _ => q,
        };
        return sorted.ToList();
    }

    // Poznati kanali idu po ručnom redoslijedu, novi (nakon osvježavanja) na kraj u izvornom redu.
    private static IEnumerable<IptvChannel> OrderManual(IEnumerable<IptvChannel> q, List<string> order)
    {
        var pos = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++)
        {
            pos[order[i]] = i;
        }

        return q.OrderBy(c => pos.TryGetValue(c.Id, out int p) ? p : int.MaxValue);
    }

    /// <summary>Kategorije s brojem kanala, u redoslijedu prvog pojavljivanja.</summary>
    public static IReadOnlyList<(string Name, int Count)> Categories(IReadOnlyList<IptvChannel> all, IptvUserData user, bool includeHidden = false)
    {
        var hc = new HashSet<string>(user.HiddenChannels);
        var hg = new HashSet<string>(user.HiddenCategories, StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (IptvChannel c in all)
        {
            if (!includeHidden && (hc.Contains(c.Id) || hg.Contains(c.Group)))
            {
                continue;
            }

            if (!counts.TryGetValue(c.Group, out int n))
            {
                order.Add(c.Group);
            }

            counts[c.Group] = n + 1;
        }

        return order.Select(g => (g, counts[g])).ToList();
    }
}
