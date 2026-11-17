using JebaitedPlayer.Library;

namespace JebaitedPlayer.Online;

/// <summary>
/// Bodovanje: koliko titl odgovara ovom videu. Naziv filma sam po sebi nije dovoljan; gleda se otisak datoteke,
/// epizoda, godina, release grupa, izvor, rezolucija, fps i kvaliteta prijevoda.
/// </summary>
public static class SubtitleScorer
{
    /// <summary>Boduje sve kandidate, izbacuje one koji sigurno ne odgovaraju (druga epizoda) i slaže ih.</summary>
    public static List<SubtitleCandidate> ScoreAndSort(IEnumerable<SubtitleCandidate> candidates, SubtitleQuery query)
    {
        var kept = new List<SubtitleCandidate>();
        foreach (SubtitleCandidate c in candidates)
        {
            if (Score(c, query))
            {
                kept.Add(c);
            }
        }

        return kept
            .OrderBy(c => query.LanguagePriority(c.Language))
            .ThenByDescending(c => c.Score)
            .ThenByDescending(c => c.Downloads)
            .ToList();
    }

    /// <summary>Vraća false ako titl sigurno ne odgovara (npr. druga epizoda).</summary>
    public static bool Score(SubtitleCandidate c, SubtitleQuery query)
    {
        c.Reasons.Clear();
        double score = 0;
        MediaInfo video = query.Info;
        MediaInfo release = MediaNameParser.Parse((c.DisplayName ?? "x") + ".mkv");

        if (c.HashMatch)
        {
            score += 100;
            c.Reasons.Add(Loc.T("score.hash"));
        }

        // Epizoda: druga epizoda se odbacuje.
        bool episodeMatch = false;
        if (query.IsEpisode)
        {
            int? season = c.Season ?? release.Season;
            int? episode = c.Episode ?? release.Episode;
            if ((episode.HasValue && episode != query.Episode) ||
                (season.HasValue && query.Season.HasValue && season != query.Season))
            {
                return false;
            }

            if (episode == query.Episode)
            {
                episodeMatch = true;
                score += 25;
                c.Reasons.Add(Loc.T("score.sameEpisode"));
            }
        }
        else
        {
            int? year = c.Year ?? release.Year;
            if (year.HasValue && query.Year.HasValue)
            {
                if (Math.Abs(year.Value - query.Year.Value) <= 1)
                {
                    score += 10;
                    c.Reasons.Add(Loc.T("score.sameYear"));
                }
                else
                {
                    score -= 40;
                    c.Reasons.Add(Loc.T("score.otherYear", year));
                }
            }
        }

        // Naslov: kod pretrage po nazivu izvori znaju vratiti druge filmove.
        string? candidateTitle = c.Title ?? (release.Title.Length > 0 ? release.Title : null);
        if (candidateTitle != null && !c.HashMatch)
        {
            string a = MediaNameParser.NormalizeTitle(candidateTitle);
            string b = MediaNameParser.NormalizeTitle(query.Title);
            if (a.Length > 0 && b.Length > 0 && !a.Contains(b) && !b.Contains(a))
            {
                score -= 30;
                c.Reasons.Add(Loc.T("score.otherTitle"));
            }
        }

        // Naziv izdanja može otkriti drugu seriju iako metapodaci izvora tvrde drukčije (OpenSubtitles automatski uvoz:
        // titlovi s nazivom izdanja za drugu seriju ili njezin nastavak upisani su pod traženu seriju i epizodu).
        bool otherShow = false;
        if (!c.HashMatch && OtherTitleInReleaseName(release, query.Title) is { } other)
        {
            otherShow = true;
            score -= 60;
            c.Reasons.Add(Loc.T("score.releaseOtherTitle", other));
        }

        // Podaci izdanja (iz naziva releasea titla i naziva video datoteke).
        IEnumerable<MediaInfo> releases = new[] { release }
            .Concat(c.Releases.Select(r => MediaNameParser.Parse(r + ".mkv")));
        bool groupMatch = video.ReleaseGroup != null && releases.Any(r =>
            string.Equals(r.ReleaseGroup, video.ReleaseGroup, StringComparison.OrdinalIgnoreCase) ||
            ContainsWord(c.DisplayName, video.ReleaseGroup));
        bool sourceMatch = video.Source != null && releases.Any(r => r.Source == video.Source);
        bool resolutionMatch = video.Resolution != null && releases.Any(r => r.Resolution == video.Resolution);

        if (groupMatch)
        {
            score += 20;
            c.Reasons.Add(Loc.T("score.sameGroup", video.ReleaseGroup));
        }

        if (sourceMatch)
        {
            score += 10;
            c.Reasons.Add(Loc.T("score.sameSource", video.Source));
        }
        else if (video.Source != null && release.Source != null)
        {
            score -= 5;
            c.Reasons.Add(Loc.T("score.otherSource", release.Source));
        }

        if (resolutionMatch)
        {
            score += 5;
        }

        if (c.FrameRate is double fps && query.FrameRate is double videoFps)
        {
            if (Math.Abs(fps - videoFps) < 0.05)
            {
                score += 5;
            }
            else
            {
                score -= 10;
                c.Reasons.Add(Loc.T("score.otherFps", fps));
            }
        }

        if (c.MachineTranslated)
        {
            score -= 40;
            c.Reasons.Add(Loc.T("score.machine"));
        }

        if (c.HearingImpaired)
        {
            score -= 3;
            c.Reasons.Add(Loc.T("score.hearingImpaired"));
        }

        score += Math.Min(5, Math.Log10(c.Downloads + 1));
        if (c.Trusted)
        {
            score += 2;
        }

        c.Score = Math.Round(score, 1);
        c.Confidence = otherShow ? MatchConfidence.Unsure
            : c.HashMatch || (groupMatch && sourceMatch && (!query.IsEpisode || episodeMatch)) ? MatchConfidence.Sure
            : score >= 30 ? MatchConfidence.Likely
            : MatchConfidence.Unsure;
        return true;
    }

    // Dodaci naslovu koji ne znače drugu seriju (zemlja, godina, članci).
    private static readonly HashSet<string> NeutralTitleWords = ["us", "uk", "au", "nz", "ca", "the", "a", "and", "of"];

    /// <summary>
    /// Naslov iz scenskog naziva izdanja (s epizodom ili godinom) ako je za drugi film ili seriju, inače null.
    /// Drugi naslov: nijedan ne sadrži drugi ("Daylight" / "Night Watch") ili ima dodatnu riječ ("Night Watch Sequel").
    /// </summary>
    internal static string? OtherTitleInReleaseName(MediaInfo release, string queryTitle)
    {
        if (release.Title.Length == 0 || (release.Episode is null && release.Year is null))
        {
            return null;
        }

        // "1x05 - The Long Night": oznaka epizode na početku, ispred nema naslova serije (to je naslov epizode).
        if (System.Text.RegularExpressions.Regex.IsMatch(release.Title, @"(?i)(^|\W)(s\d{1,2}\s*e\d{1,3}|\d{1,2}x\d{1,3})(\W|$)"))
        {
            return null;
        }

        string rel = MediaNameParser.NormalizeTitle(release.Title), q = MediaNameParser.NormalizeTitle(queryTitle);
        if (rel.Length == 0 || q.Length == 0 || rel == q)
        {
            return null;
        }

        if (!rel.Contains(q) && !q.Contains(rel))
        {
            return release.Title;
        }

        static IEnumerable<string> Words(string s) => s.ToLowerInvariant()
            .Split([' ', '.', '-', '_', ':', '\'', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        var queryWords = Words(queryTitle).ToHashSet();
        bool extra = Words(release.Title).Any(w => !queryWords.Contains(w) && !NeutralTitleWords.Contains(w) && !(w.Length == 4 && w.All(char.IsDigit)));
        return extra ? release.Title : null;
    }

    /// <summary>
    /// Automatski se učitava samo "isto izdanje" u najvažnijem jeziku za koji postoje titlovi.
    /// Ako u tom jeziku nema sigurnog podudaranja, korisnik dobiva popis (ne učitavamo moguće krive titlove).
    /// </summary>
    public static AutoDecision Decide(IReadOnlyList<SubtitleCandidate> sorted, SubtitleQuery query)
    {
        foreach (string language in query.Languages)
        {
            SubtitleCandidate? best = sorted.Where(c => c.Language == language).MaxBy(c => c.Score);
            if (best is null)
            {
                continue;
            }

            return best.Confidence == MatchConfidence.Sure
                ? new AutoDecision(best, best, language)
                : new AutoDecision(null, best, language);
        }

        return new AutoDecision(null, null, null);
    }

    private static bool ContainsWord(string? text, string? word)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word))
        {
            return false;
        }

        string[] tokens = text.Split(['.', ' ', '-', '_', '[', ']', '(', ')'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => string.Equals(t, word, StringComparison.OrdinalIgnoreCase));
    }
}
