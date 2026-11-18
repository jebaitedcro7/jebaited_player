using Microsoft.Data.Sqlite;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Store inačica pri prvom pokretanju jednom preuzme postavke i povijest gledanja GitHub inačice s istog računala,
/// da prelazak na Store ne izgubi ništa. Izvor se nikad ne mijenja. Predmemorije (titlovi, govor, shaderi) se ne kopiraju,
/// izrade se ponovno.
/// </summary>
public static class LegacyDataImport
{
    /// <summary>Preuzmi iz <paramref name="fromDir"/> u <paramref name="toDir"/> ako cilj još nema postavki. Vraća broj datoteka (0 = ništa).</summary>
    public static int Run(string fromDir, string toDir)
    {
        string fromSettings = Path.Combine(fromDir, "settings.json");
        string toSettings = Path.Combine(toDir, "settings.json");
        if (string.Equals(Path.GetFullPath(fromDir), Path.GetFullPath(toDir), StringComparison.OrdinalIgnoreCase)
            || File.Exists(toSettings) || !File.Exists(fromSettings))
        {
            return 0;
        }

        Directory.CreateDirectory(toDir);
        int items = 0;

        // Baza preko SQLite backupa: dosljedna kopija i kad dio zapisa još stoji u -wal datoteci.
        string fromDb = Path.Combine(fromDir, "library.db");
        if (File.Exists(fromDb))
        {
            string toDb = Path.Combine(toDir, "library.db");
            using (SqliteConnection source = OpenSource(fromDb))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = toDb, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
            }

            items++;
        }

        // Postavke zadnje: njihovo postojanje znači da je preuzimanje gotovo (prekinuto preuzimanje se ponovi).
        File.Copy(fromSettings, toSettings + ".tmp", overwrite: true);
        File.Move(toSettings + ".tmp", toSettings, overwrite: true);
        items++;
        return items;
    }

    /// <summary>
    /// Izvor samo za čitanje: zatvaranje zadnje veze za pisanje prebacilo bi -wal u bazu GitHub inačice (checkpoint),
    /// a izvor se ne smije mijenjati ni na razini datoteke. Ako čitanje nije moguće (npr. nema -shm), obična veza.
    /// </summary>
    private static SqliteConnection OpenSource(string path)
    {
        var readOnly = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        try
        {
            readOnly.Open();
            return readOnly;
        }
        catch (SqliteException)
        {
            readOnly.Dispose();
            var readWrite = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            readWrite.Open();
            return readWrite;
        }
    }
}
