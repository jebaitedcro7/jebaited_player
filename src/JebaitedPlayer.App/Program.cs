using JebaitedPlayer.Infrastructure;
using Velopack;

namespace JebaitedPlayer;

/// <summary>
/// Ulazna točka. Prije prozora: jedna instanca (druga pokretanja predaju datoteke otvorenom playeru)
/// i naredbe bez sučelja (registracija u Windowsu).
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack: instalacija, nadogradnja i deinstalacija pokreću exe s posebnim argumentima; tada se ovdje
        // obavi registracija u Windowsu i proces završi. Preuzeta nadogradnja koja još nije instalirana primijeni se ovdje.
        // Inačica iz Microsoft Storea: instalaciju i nadogradnje radi Store, pa Velopack ne smije ništa raditi.
        if (!AppPackage.IsStore)
        {
            VelopackApp.Build()
                .OnAfterInstallFastCallback(v => RunHook($"Instalirana verzija {v}", InstallHook.Install))
                .OnAfterUpdateFastCallback(v => RunHook($"Nadograđeno na verziju {v}", InstallHook.Update))
                .OnBeforeUninstallFastCallback(v => RunHook($"Deinstalacija verzije {v}", InstallHook.Uninstall))
                .Run();
        }
        else if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JEBAITED_DATA_DIR")))
        {
            ImportGitHubData();
        }

        StartupOptions options;
        Exception? argsError = null;
        try
        {
            options = StartupOptions.Parse(args);
        }
        catch (Exception ex)
        {
            argsError = ex;
            options = new StartupOptions();
        }

        if (options.RegisterCommand is { } command)
        {
            if (AppPackage.IsStore)
            {
                // U paketu vrste datoteka pridružuje manifest; zapis u registry bi bio virtualiziran i beskoristan.
                return 0;
            }

            string root = options.DiagRegistryRoot ?? ShellIntegration.DefaultRoot;
            if (root != ShellIntegration.DefaultRoot && !root.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return RunRegistration(command, root);
        }

        // Dijagnostika i --new-window rade kao zasebni playeri (testovi ne smiju slati datoteke tvom otvorenom playeru),
        // osim kad test zada vlastito ime instance.
        string? instanceName = options.DiagInstanceName
                               ?? (options.IsDiagnostics || options.NewWindow ? null : SingleInstance.DefaultName);
        SingleInstance? instance = null;
        bool standalone = false;
        if (instanceName != null)
        {
            instance = SingleInstance.Acquire(instanceName, options.Files, TimeSpan.FromSeconds(8), out standalone);
            if (instance is null && !standalone)
            {
                // Datoteke su predane već otvorenom playeru.
                return 0;
            }
        }

        try
        {
            var app = new App(options, instance, argsError, standalone);
            app.InitializeComponent();
            return app.Run();
        }
        finally
        {
            instance?.Dispose();
        }
    }

    /// <summary>
    /// Instalacija: "Otvori pomoću" i popis u Zadanim aplikacijama za instalirani exe.
    /// Nadogradnja: osvježi registraciju samo ako je i prije bila za ovaj exe (korisnik ju je mogao ukloniti).
    /// Deinstalacija: ukloni registraciju ovog exe-a. Postavke, povijest i ključevi (%LOCALAPPDATA%\jebaited_player) ostaju.
    /// Prijenosna verzija nikad ne mijenja ništa u Windowsu.
    /// </summary>
    private static void RunHook(string message, InstallHook hook)
    {
        Log.Init();
        Log.Info(message);
        string? exe = Environment.ProcessPath;
        if (exe is null || IsPortable() || Environment.GetEnvironmentVariable("JEBAITED_NO_SHELL_REGISTRATION") == "1")
        {
            Log.Info("Registracija u Windowsu preskočena (prijenosna verzija).");
            return;
        }

        bool registeredHere = ShellIntegration.IsRegisteredFor(exe);
        switch (hook)
        {
            case InstallHook.Install:
            case InstallHook.Update when registeredHere:
                RunRegistration("register", ShellIntegration.DefaultRoot);
                break;
            case InstallHook.Uninstall when registeredHere:
                RunRegistration("unregister", ShellIntegration.DefaultRoot);
                break;
        }
    }

    /// <summary>Store inačica: jednom preuzmi postavke i povijest GitHub inačice (ako postoje). Greška ne smije spriječiti start.</summary>
    private static void ImportGitHubData()
    {
        try
        {
            int files = LegacyDataImport.Run(AppPaths.GitHubDataDir, AppPaths.DataDir);
            if (files > 0)
            {
                Log.Init();
                Log.Info($"Preuzeti podaci GitHub inačice ({files} datoteke) iz {AppPaths.GitHubDataDir}");
            }
        }
        catch (Exception ex)
        {
            Log.Init();
            Log.Warn($"Podaci GitHub inačice nisu preuzeti: {ex.Message}");
        }
    }

    private static bool IsPortable()
    {
        try
        {
            return new UpdateManager(new Velopack.Sources.SimpleFileSource(new DirectoryInfo(AppContext.BaseDirectory))).IsPortable;
        }
        catch (Exception)
        {
            // Velopack: prijenosna verzija ima oznaku ".portable" uz Update.exe (mapa iznad "current").
            string? root = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            return root != null && File.Exists(Path.Combine(root, ".portable"));
        }
    }

    private enum InstallHook
    {
        Install,
        Update,
        Uninstall,
    }

    private static int RunRegistration(string command, string root)
    {
        if (Log.FilePath is null)
        {
            Log.Init();
        }

        try
        {
            if (command == "register")
            {
                string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Putanja exe-a nije poznata.");
                Loc.SetLanguage(AppSettings.Load().Language);
                int types = ShellIntegration.Register(exe, root);
                Log.Info($"Registracija u Windowsu: {types} vrsta datoteka ({exe})");
            }
            else
            {
                ShellIntegration.Unregister(root);
                Log.Info("Registracija u Windowsu uklonjena.");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Registracija u Windowsu", ex);
            return 1;
        }
    }
}
