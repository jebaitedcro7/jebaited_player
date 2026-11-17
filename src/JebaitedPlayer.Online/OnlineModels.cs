using System.Text.Json.Serialization;
using JebaitedPlayer.Library;

namespace JebaitedPlayer.Online;

/// <summary>Što tražimo: video, podaci iz naziva, otisak datoteke i jezici po prioritetu (hr, sr, bs, en…).</summary>
public sealed record SubtitleQuery(
    string VideoPath,
    MediaInfo Info,
    string? MovieHash,
    long FileSize,
    double? FrameRate,
    IReadOnlyList<string> Languages)
{
    /// <summary>Naslov koji se šalje izvorima (korisnik ga može ispraviti u prozoru pretrage).</summary>
    public string Title { get; init; } = Info.Title;

    public int? Year { get; init; } = Info.Year;

    public int? Season { get; init; } = Info.Season;

    public int? Episode { get; init; } = Info.Episode;

    public bool IsEpisode => Episode.HasValue;

    public int LanguagePriority(string? language)
    {
        int index = language is null ? -1 : Languages.ToList().IndexOf(language);
        return index < 0 ? int.MaxValue : index;
    }
}

public enum MatchConfidence
{
    /// <summary>Nije jasno odgovara li titl ovom izdanju.</summary>
    Unsure,

    /// <summary>Vjerojatno odgovara (isti film/epizoda, slični podaci izdanja).</summary>
    Likely,

    /// <summary>Isto izdanje (hash ili ista release grupa i izvor).</summary>
    Sure,
}

/// <summary>Jedan titl pronađen na internetu.</summary>
public sealed class SubtitleCandidate
{
    public required string Provider { get; init; }

    /// <summary>Oznaka koja je potrebna za preuzimanje (npr. file_id ili putanja).</summary>
    public required string DownloadReference { get; init; }

    /// <summary>Kratka oznaka jezika (hr, sr, bs, en…).</summary>
    public required string Language { get; init; }

    public string? ReleaseName { get; init; }
    public IReadOnlyList<string> Releases { get; init; } = [];
    public string? FileName { get; init; }
    public bool HashMatch { get; init; }
    public bool HearingImpaired { get; init; }
    public bool MachineTranslated { get; init; }
    public double? FrameRate { get; init; }
    public int Downloads { get; init; }
    public bool Trusted { get; init; }
    public string? Uploader { get; init; }
    public int? Season { get; init; }
    public int? Episode { get; init; }
    public int? Year { get; init; }
    public string? Title { get; init; }
    public string? PageUrl { get; init; }

    [JsonIgnore]
    public double Score { get; set; }

    [JsonIgnore]
    public MatchConfidence Confidence { get; set; }

    [JsonIgnore]
    public List<string> Reasons { get; } = [];

    /// <summary>Naziv za prikaz: release, inače naziv datoteke.</summary>
    [JsonIgnore]
    public string DisplayName => ReleaseName ?? Releases.FirstOrDefault() ?? FileName ?? Title ?? DownloadReference;
}

/// <summary>Preuzeta datoteka titlova (već raspakirana iz ZIP-a).</summary>
public sealed record DownloadedSubtitle(string FileName, byte[] Content);

public sealed record ProviderResult(string Provider, bool Ok, int Count, string? Error, bool FromCache);

public sealed record SearchOutcome(IReadOnlyList<SubtitleCandidate> Candidates, IReadOnlyList<ProviderResult> Providers);

/// <summary>Odluka nakon pretrage: učitaj automatski, ponudi popis ili ništa nije pronađeno.</summary>
public sealed record AutoDecision(SubtitleCandidate? AutoLoad, SubtitleCandidate? Best, string? Language);

public interface ISubtitleProvider
{
    string Name { get; }

    /// <summary>Uključen i ima potrebne podatke (ključ, račun).</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(SubtitleQuery query, CancellationToken ct);

    Task<DownloadedSubtitle> DownloadAsync(SubtitleCandidate candidate, SubtitleQuery query, CancellationToken ct);
}

/// <summary>Greška izvora s porukom razumljivom korisniku (bez ključeva i lozinki).</summary>
public sealed class ProviderException(string message, Exception? inner = null) : Exception(message, inner);
