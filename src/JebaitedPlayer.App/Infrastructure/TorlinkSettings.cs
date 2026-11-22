#if TORLINK
using JebaitedPlayer.Torlink;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Postavke Torlink modula (samo GitHub inačica). Zadano je sve isključeno: Torlink, dijeljenje (seeding). Sprema se u settings.json
/// uz ostale postavke (Spremi / Odbaci / Zadano rade kao i drugdje), a pri isključivanju Torlinka ostaju sačuvane.
/// </summary>
public sealed class TorlinkSettings
{
    public bool Enabled { get; set; }

    /// <summary>Korisnik je prihvatio upozorenje (IP vidljiva, odgovornost za sadržaj). Traži se prije prvog uključivanja.</summary>
    public bool ConsentAccepted { get; set; }

    /// <summary>Prazno = Downloads\torlink.</summary>
    public string DownloadDir { get; set; } = string.Empty;

    public int MaxDownloads { get; set; } = 3;

    public bool Playlist { get; set; } = true;

    public List<string> Trackers { get; set; } = [];

    /// <summary>Dijeljenje nakon preuzimanja: zadano ISKLJUČENO.</summary>
    public bool SeedAfter { get; set; }

    public int SeedHours { get; set; }

    public string Interface { get; set; } = string.Empty;

    public int PeerPort { get; set; }

    /// <summary>ISO 8601 (UTC) zadnje provjere ažuriranja i zadnjeg ažuriranja; prazno = nikad.</summary>
    public string LastUpdateCheck { get; set; } = string.Empty;

    public string LastUpdate { get; set; } = string.Empty;

    public TorlinkOptions ToOptions() => new()
    {
        DownloadDir = DownloadDir,
        MaxDownloads = Math.Clamp(MaxDownloads, 0, 20),
        Playlist = Playlist,
        Trackers = Trackers.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList(),
        SeedAfter = SeedAfter,
        SeedHours = Math.Clamp(SeedHours, 0, 24 * 365),
        Interface = Interface ?? string.Empty,
        PeerPort = PeerPort is >= 1 and <= 65535 ? PeerPort : 0,
    };

    /// <summary>Vrati opcije preuzimanja i mreže na zadano; "uključeno" i prihvat upozorenja ostaju kakvi jesu.</summary>
    public void ResetOptions()
    {
        var d = new TorlinkSettings();
        (DownloadDir, MaxDownloads, Playlist, SeedAfter, SeedHours, Interface, PeerPort) =
            (d.DownloadDir, d.MaxDownloads, d.Playlist, d.SeedAfter, d.SeedHours, d.Interface, d.PeerPort);
        Trackers = [];
    }
}
#endif
