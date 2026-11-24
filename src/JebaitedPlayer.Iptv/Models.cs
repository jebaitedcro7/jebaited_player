namespace JebaitedPlayer.Iptv;

public enum IptvSourceKind
{
    Xtream,
    M3uUrl,
    M3uFile,
}

/// <summary>Jedan kanal. <see cref="Id"/> je stabilan ključ pružatelja (ne indeks), da favoriti i redoslijed prežive osvježavanje.</summary>
public sealed record IptvChannel(
    string Id,
    string Name,
    string StreamUrl,
    string Group,
    string? LogoUrl,
    string? EpgId,
    int Number,
    bool HasArchive);

public sealed record EpgProgram(string EpgId, DateTimeOffset Start, DateTimeOffset End, string Title, string? Description)
{
    public bool IsLiveAt(DateTimeOffset t) => t >= Start && t < End;

    /// <summary>Napredak 0..1 u trenutku t.</summary>
    public double ProgressAt(DateTimeOffset t)
    {
        double total = (End - Start).TotalSeconds;
        return total <= 0 ? 0 : Math.Clamp((t - Start).TotalSeconds / total, 0, 1);
    }
}

/// <summary>Rezultat učitavanja izvora. <see cref="Note"/> je neobavezna informacija za korisnika (npr. račun istječe).</summary>
public sealed record IptvCatalog(IReadOnlyList<IptvChannel> Channels, string? XmltvUrl, string? Note);

public enum IptvErrorKind
{
    Unreachable,
    Timeout,
    BadCredentials,
    Expired,
    InvalidResponse,
    Http,
    Cancelled,
}

/// <summary>Greška izvora. Poruka nikad ne sadrži lozinku ni URL s lozinkom.</summary>
public sealed class IptvException(IptvErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public IptvErrorKind Kind { get; } = kind;
}
