using System.Globalization;
using Microsoft.Data.Sqlite;

namespace JebaitedPlayer.Library;

public sealed record WatchEntry(
    string Path,
    string? Title,
    int? Season,
    int? Episode,
    double Duration,
    double Position,
    bool Watched,
    DateTime? LastPlayed,
    int PlayCount,
    string? Hash)
{
    public double Progress => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;
}

/// <summary>Zapamćeni titlovi i sinkronizacija za jedan video.</summary>
/// <param name="Choice">"none" (bez titlova), "external" (vanjska datoteka) ili "embedded" (ugrađeni).</param>
/// <param name="TimeMap">Korekcija po dijelovima (titlovi za drugo izdanje), zapis iz SubtitleTimeMap; null = nema.</param>
public sealed record SubtitleState(string Choice, string? ExternalPath, long? EmbeddedId, string? Language, double DelayMs, double Speed,
    string? TimeMap = null);

/// <summary>Omiljeni film (putanja datoteke) ili serija (putanja mape serije).</summary>
/// <param name="Kind">"movie" ili "series".</param>
public sealed record Favorite(string Path, string Title, string Kind, DateTime Added)
{
    public bool IsSeries => Kind == "series";
}

/// <summary>Zapamćena audio traka za jedan video (mpv ID i jezik, za slučaj da se redoslijed traka promijeni).</summary>
public sealed record AudioState(long TrackId, string? Language);

/// <summary>
/// Lokalna baza (SQLite) za povijest gledanja, pozicije i titlove po videu.
/// Sve metode su sigurne za pozive iz više dretvi (jedna veza, zaključavanje).
/// </summary>
public sealed class WatchStore : IDisposable
{
    private const string SelectMedia =
        "SELECT path, title, season, episode, duration, position, watched, last_played, play_count, hash FROM media";

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    public WatchStore(string databasePath)
    {
        string full = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        _db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _db.Open();
        Execute("PRAGMA journal_mode=WAL;");
        Execute("PRAGMA synchronous=NORMAL;");
        Migrate();
    }

    public WatchEntry? Get(string path)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command($"{SelectMedia} WHERE path = $path", ("$path", path));
            using SqliteDataReader r = cmd.ExecuteReader();
            return r.Read() ? ReadEntry(r) : null;
        }
    }

    public IReadOnlyDictionary<string, WatchEntry> GetMany(IEnumerable<string> paths)
    {
        var result = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (Get(path) is { } entry)
            {
                result[path] = entry;
            }
        }

        return result;
    }

    /// <summary>Video je pokrenut: zapamti naslov i vrijeme, povećaj broj gledanja.</summary>
    public void RegisterPlay(string path, string? title, int? season, int? episode)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                """
                INSERT INTO media (path, title, season, episode, last_played, play_count)
                VALUES ($path, $title, $season, $episode, $now, 1)
                ON CONFLICT(path) DO UPDATE SET
                    title = excluded.title, season = excluded.season, episode = excluded.episode,
                    last_played = excluded.last_played, play_count = media.play_count + 1
                """,
                ("$path", path), ("$title", title), ("$season", season), ("$episode", episode), ("$now", Now()));
            cmd.ExecuteNonQuery();
        }
    }

    public void SavePosition(string path, double position, double duration, bool watched)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                """
                INSERT INTO media (path, duration, position, watched, last_played)
                VALUES ($path, $duration, $position, $watched, $now)
                ON CONFLICT(path) DO UPDATE SET
                    duration = excluded.duration, position = excluded.position,
                    watched = excluded.watched, last_played = excluded.last_played
                """,
                ("$path", path), ("$duration", duration), ("$position", position), ("$watched", watched ? 1 : 0), ("$now", Now()));
            cmd.ExecuteNonQuery();
        }
    }

    public void SetWatched(string path, bool watched)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                """
                INSERT INTO media (path, watched, position) VALUES ($path, $watched, 0)
                ON CONFLICT(path) DO UPDATE SET watched = excluded.watched, position = 0
                """,
                ("$path", path), ("$watched", watched ? 1 : 0));
            cmd.ExecuteNonQuery();
        }
    }

    public void SaveHash(string path, long fileSize, string hash)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                """
                INSERT INTO media (path, file_size, hash) VALUES ($path, $size, $hash)
                ON CONFLICT(path) DO UPDATE SET file_size = excluded.file_size, hash = excluded.hash
                """,
                ("$path", path), ("$size", fileSize), ("$hash", hash));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Zadnje gledano, najnovije prvo.</summary>
    public IReadOnlyList<WatchEntry> Recent(int count)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                $"{SelectMedia} WHERE last_played IS NOT NULL ORDER BY last_played DESC LIMIT $count", ("$count", count));
            using SqliteDataReader r = cmd.ExecuteReader();
            var list = new List<WatchEntry>();
            while (r.Read())
            {
                list.Add(ReadEntry(r));
            }

            return list;
        }
    }

    /// <summary>Uklanja video iz povijesti (ne briše datoteku).</summary>
    public void Remove(string path)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command("UPDATE media SET last_played = NULL, position = 0 WHERE path = $path", ("$path", path));
            cmd.ExecuteNonQuery();
        }
    }

    public SubtitleState? GetSubtitleState(string videoPath)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                "SELECT choice, external_path, embedded_id, language, delay_ms, speed, time_map FROM subtitle_state WHERE video_path = $path",
                ("$path", videoPath));
            using SqliteDataReader r = cmd.ExecuteReader();
            if (!r.Read())
            {
                return null;
            }

            return new SubtitleState(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetInt64(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetDouble(4),
                r.GetDouble(5),
                r.IsDBNull(6) ? null : r.GetString(6));
        }
    }

    public void SaveSubtitleState(string videoPath, SubtitleState state)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                """
                INSERT OR REPLACE INTO subtitle_state (video_path, choice, external_path, embedded_id, language, delay_ms, speed, time_map, updated)
                VALUES ($path, $choice, $external, $embedded, $language, $delay, $speed, $map, $now)
                """,
                ("$path", videoPath), ("$choice", state.Choice), ("$external", state.ExternalPath), ("$embedded", state.EmbeddedId),
                ("$language", state.Language), ("$delay", state.DelayMs), ("$speed", state.Speed), ("$map", state.TimeMap), ("$now", Now()));
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Favorite> Favorites()
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command("SELECT path, title, kind, added FROM favorites ORDER BY added DESC");
            using SqliteDataReader r = cmd.ExecuteReader();
            var list = new List<Favorite>();
            while (r.Read())
            {
                list.Add(new Favorite(r.GetString(0), r.GetString(1), r.GetString(2),
                    DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
            }

            return list;
        }
    }

    public bool IsFavorite(string path)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command("SELECT 1 FROM favorites WHERE path = $path", ("$path", path));
            return cmd.ExecuteScalar() != null;
        }
    }

    public void AddFavorite(string path, string title, string kind)
    {
        if (kind is not ("movie" or "series"))
        {
            throw new ArgumentException("Vrsta mora biti 'movie' ili 'series'.", nameof(kind));
        }

        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                "INSERT OR REPLACE INTO favorites (path, title, kind, added) VALUES ($path, $title, $kind, $now)",
                ("$path", path), ("$title", title), ("$kind", kind), ("$now", Now()));
            cmd.ExecuteNonQuery();
        }
    }

    public void RemoveFavorite(string path)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command("DELETE FROM favorites WHERE path = $path", ("$path", path));
            cmd.ExecuteNonQuery();
        }
    }

    public AudioState? GetAudioState(string videoPath)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command("SELECT track_id, language FROM audio_state WHERE video_path = $path", ("$path", videoPath));
            using SqliteDataReader r = cmd.ExecuteReader();
            return r.Read() ? new AudioState(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1)) : null;
        }
    }

    public void SaveAudioState(string videoPath, AudioState state)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(
                "INSERT OR REPLACE INTO audio_state (video_path, track_id, language, updated) VALUES ($path, $id, $language, $now)",
                ("$path", videoPath), ("$id", state.TrackId), ("$language", state.Language), ("$now", Now()));
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _db.Dispose();
        }
    }

    private void Migrate()
    {
        Execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT)");
        Execute(
            """
            CREATE TABLE IF NOT EXISTS media (
                path TEXT PRIMARY KEY COLLATE NOCASE,
                title TEXT, season INTEGER, episode INTEGER,
                duration REAL NOT NULL DEFAULT 0, position REAL NOT NULL DEFAULT 0,
                watched INTEGER NOT NULL DEFAULT 0, last_played TEXT, play_count INTEGER NOT NULL DEFAULT 0,
                file_size INTEGER, hash TEXT)
            """);
        Execute("CREATE INDEX IF NOT EXISTS ix_media_last_played ON media(last_played)");
        Execute(
            """
            CREATE TABLE IF NOT EXISTS subtitle_state (
                video_path TEXT PRIMARY KEY COLLATE NOCASE,
                choice TEXT NOT NULL, external_path TEXT, embedded_id INTEGER, language TEXT,
                delay_ms REAL NOT NULL DEFAULT 0, speed REAL NOT NULL DEFAULT 1, updated TEXT)
            """);
        // Verzija 2 (M6): omiljeni filmovi i serije, zapamćena audio traka po videu.
        Execute(
            """
            CREATE TABLE IF NOT EXISTS favorites (
                path TEXT PRIMARY KEY COLLATE NOCASE,
                title TEXT NOT NULL, kind TEXT NOT NULL, added TEXT NOT NULL)
            """);
        Execute(
            """
            CREATE TABLE IF NOT EXISTS audio_state (
                video_path TEXT PRIMARY KEY COLLATE NOCASE,
                track_id INTEGER NOT NULL, language TEXT, updated TEXT)
            """);
        // Verzija 3: korekcija titlova po dijelovima (titlovi za drugo izdanje). Postojeća baza dobije novi stupac.
        if (!ColumnExists("subtitle_state", "time_map"))
        {
            Execute("ALTER TABLE subtitle_state ADD COLUMN time_map TEXT");
        }

        Execute("INSERT OR REPLACE INTO meta (key, value) VALUES ('schema_version', '3')");
    }

    private bool ColumnExists(string table, string column)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command($"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $column", ("$column", column));
            return cmd.ExecuteScalar() != null;
        }
    }

    private void Execute(string sql)
    {
        lock (_gate)
        {
            using SqliteCommand cmd = Command(sql);
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return cmd;
    }

    private static WatchEntry ReadEntry(SqliteDataReader r) => new(
        r.GetString(0),
        r.IsDBNull(1) ? null : r.GetString(1),
        r.IsDBNull(2) ? null : r.GetInt32(2),
        r.IsDBNull(3) ? null : r.GetInt32(3),
        r.GetDouble(4),
        r.GetDouble(5),
        r.GetInt64(6) != 0,
        r.IsDBNull(7) ? null : DateTime.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        r.GetInt32(8),
        r.IsDBNull(9) ? null : r.GetString(9));

    private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
}
