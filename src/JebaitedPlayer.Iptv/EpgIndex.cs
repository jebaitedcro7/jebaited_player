namespace JebaitedPlayer.Iptv;

/// <summary>EPG po kanalu (EPG ID), sortiran po početku. Trenutni/sljedeći program i raspon za vremensku crtu.</summary>
public sealed class EpgIndex
{
    private readonly Dictionary<string, List<EpgProgram>> _byChannel = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LoadedAt { get; private set; }

    public int ProgramCount { get; private set; }

    public void Load(IEnumerable<EpgProgram> programs, DateTimeOffset now)
    {
        _byChannel.Clear();
        int n = 0;
        foreach (EpgProgram p in programs)
        {
            if (!_byChannel.TryGetValue(p.EpgId, out List<EpgProgram>? l))
            {
                _byChannel[p.EpgId] = l = [];
            }

            l.Add(p);
            n++;
        }

        foreach (List<EpgProgram> l in _byChannel.Values)
        {
            l.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        ProgramCount = n;
        LoadedAt = now;
    }

    /// <summary>Spoji dodatne programe (npr. kratki EPG jednog kanala) bez duplikata.</summary>
    public void Merge(string epgId, IEnumerable<EpgProgram> programs)
    {
        if (!_byChannel.TryGetValue(epgId, out List<EpgProgram>? l))
        {
            _byChannel[epgId] = l = [];
        }

        foreach (EpgProgram p in programs)
        {
            if (!l.Any(x => x.Start == p.Start && x.End == p.End))
            {
                l.Add(p);
                ProgramCount++;
            }
        }

        l.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    public bool HasData(string? epgId) => epgId is not null && _byChannel.ContainsKey(epgId);

    public EpgProgram? Now(string? epgId, DateTimeOffset t) =>
        epgId is not null && _byChannel.TryGetValue(epgId, out List<EpgProgram>? l) ? l.FirstOrDefault(p => p.IsLiveAt(t)) : null;

    public EpgProgram? Next(string? epgId, DateTimeOffset t) =>
        epgId is not null && _byChannel.TryGetValue(epgId, out List<EpgProgram>? l) ? l.FirstOrDefault(p => p.Start >= t) : null;

    public IReadOnlyList<EpgProgram> Range(string? epgId, DateTimeOffset from, DateTimeOffset to) =>
        epgId is not null && _byChannel.TryGetValue(epgId, out List<EpgProgram>? l)
            ? l.Where(p => p.End > from && p.Start < to).ToList()
            : [];

    /// <summary>Podaci postoje, ali su zastarjeli (nijedan program ne završava u budućnosti).</summary>
    public bool IsStale(DateTimeOffset t) => ProgramCount > 0 && _byChannel.Values.All(l => l[^1].End < t);
}
