namespace JebaitedPlayer.Torlink;

/// <summary>Postavke koje Torlink stvarno podržava (ništa više): mapa, istodobna, playlist, trackeri, dijeljenje, mreža.</summary>
public sealed record TorlinkOptions
{
    /// <summary>Prazno = zadana mapa (<see cref="TorlinkPaths.DefaultDownloadDir"/>).</summary>
    public string DownloadDir { get; init; } = string.Empty;

    /// <summary>Najviše istodobnih preuzimanja; 0 = bez ograničenja.</summary>
    public int MaxDownloads { get; init; } = 3;

    public bool Playlist { get; init; } = true;

    public IReadOnlyList<string> Trackers { get; init; } = [];

    /// <summary>Dijeljenje nakon preuzimanja. Zadano isključeno.</summary>
    public bool SeedAfter { get; init; }

    /// <summary>Koliko sati dijeliti kad je dijeljenje uključeno (0 = bez ograničenja).</summary>
    public int SeedHours { get; init; }

    /// <summary>Ime ili IPv4 mrežnog sučelja (npr. VPN); prazno = bilo koje.</summary>
    public string Interface { get; init; } = string.Empty;

    /// <summary>Fiksni port za peerove (1–65535); 0 = automatski.</summary>
    public int PeerPort { get; init; }

    public string EffectiveDownloadDir => string.IsNullOrWhiteSpace(DownloadDir) ? TorlinkPaths.DefaultDownloadDir : DownloadDir.Trim();
}
