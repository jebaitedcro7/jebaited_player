using System.Text.Json;

namespace JebaitedPlayer.Torlink;

public enum SearchFailure
{
    InvalidQuery,
    NoRuntime,
    Timeout,
    BadOutput,
}

public sealed class TorlinkSearchException : Exception
{
    public TorlinkSearchException(SearchFailure failure, string? detail = null) : base(failure + (detail is null ? string.Empty : ": " + detail))
    {
        Failure = failure;
    }

    public SearchFailure Failure { get; }
}

/// <summary>Pretraga = jednokratan proces <c>torlnk search "..." --category ...</c> (nema HTTP endpointa za pretragu). Otkaziva se ubijanjem procesa.</summary>
public sealed class SearchRunner
{
    public const int MaxQueryLength = 200;
    public static readonly string[] Categories = ["games", "movies", "tv", "anime"];

    private readonly TorlinkPaths _paths;
    private readonly IProcessLauncher _launcher;

    public SearchRunner(TorlinkPaths paths, IProcessLauncher launcher)
    {
        _paths = paths;
        _launcher = launcher;
    }

    /// <summary>Očisti upit: bez kontrolnih znakova, najviše 200 znakova, bez vodećih crtica (ne smije se pročitati kao zastavica).</summary>
    public static string? NormalizeQuery(string? query)
    {
        if (query is null)
        {
            return null;
        }

        string q = new(query.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        q = string.Join(' ', q.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimStart('-').Trim();
        if (q.Length == 0)
        {
            return null;
        }

        return q.Length > MaxQueryLength ? q[..MaxQueryLength] : q;
    }

    /// <param name="category">null/"all" = svi izvori, inače jedan od <see cref="Categories"/>.</param>
    public async Task<SearchResultSet> SearchAsync(string query, string? category, TorlinkOptions options, int timeoutSeconds = 20, CancellationToken ct = default)
    {
        string? q = NormalizeQuery(query);
        if (q is null)
        {
            throw new TorlinkSearchException(SearchFailure.InvalidQuery);
        }

        if (!File.Exists(_paths.NodeExe) || !File.Exists(_paths.CliScript))
        {
            throw new TorlinkSearchException(SearchFailure.NoRuntime);
        }

        string? cat = category is not null && Categories.Contains(category, StringComparer.OrdinalIgnoreCase) ? category.ToLowerInvariant() : null;
        var psi = TorlinkCommand.StartInfo(_paths, TorlinkCommand.SearchArgs(_paths, options, q, cat, timeoutSeconds), TorlinkCommand.Environment(_paths, options, null));
        ProcessResult r = await Task.Run(() => _launcher.RunAsync(psi, TimeSpan.FromSeconds(timeoutSeconds + 10), ct), ct).ConfigureAwait(false);
        if (r.TimedOut)
        {
            throw new TorlinkSearchException(SearchFailure.Timeout);
        }

        try
        {
            // Izlaz je jedan JSON dokument; kod 1 znači da su svi izvori pali, ali dokument i dalje opisuje svaki izvor.
            return SearchResultSet.Parse(r.Stdout);
        }
        catch (JsonException ex)
        {
            throw new TorlinkSearchException(SearchFailure.BadOutput, ex.Message);
        }
    }
}
