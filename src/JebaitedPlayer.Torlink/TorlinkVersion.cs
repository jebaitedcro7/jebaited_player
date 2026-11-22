namespace JebaitedPlayer.Torlink;

/// <summary>Usporedba verzija oblika x.y.z (sufiksi -rc, +build se odbacuju, kao u samom Torlinku) uz našu reviziju paketa.</summary>
public readonly record struct TorlinkVersion(int Major, int Minor, int Patch, int Revision) : IComparable<TorlinkVersion>
{
    public static bool TryParse(string? text, int revision, out TorlinkVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        string[] parts = core.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        int[] n = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out n[i]) || n[i] < 0)
            {
                return false;
            }
        }

        version = new TorlinkVersion(n[0], n[1], n[2], revision);
        return true;
    }

    public int CompareTo(TorlinkVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        return c != 0 ? c : Revision.CompareTo(other.Revision);
    }

    public static bool operator <(TorlinkVersion a, TorlinkVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(TorlinkVersion a, TorlinkVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(TorlinkVersion a, TorlinkVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TorlinkVersion a, TorlinkVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Revision > 1 ? $" r{Revision}" : string.Empty);
}
