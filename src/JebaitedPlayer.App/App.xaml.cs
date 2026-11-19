using System.Windows;
using System.Windows.Threading;
using JebaitedPlayer.Diagnostics;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer;

public partial class App : Application
{
    private readonly StartupOptions _options;
    private readonly SingleInstance? _instance;
    private readonly Exception? _argsError;
    private readonly bool _standalone;

    internal App(StartupOptions options, SingleInstance? instance, Exception? argsError, bool standalone)
    {
        _options = options;
        _instance = instance;
        _argsError = argsError;
        _standalone = standalone;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Init();
        Log.StartSession();
        UiEvents.Install();
        SessionEnding += (_, args) =>
        {
            DiagLog.Info(LogCat.App, "session.windows_ending", "App", "Windows is ending the session", ("reason", args.ReasonSessionEnding));
        };

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            DiagLog.Critical(LogCat.Stability, "crash.unhandled", "AppDomain", "Unhandled exception (process is terminating)",
                args.ExceptionObject as Exception, ("terminating", args.IsTerminating));
            if (args.IsTerminating)
            {
                Log.EndSessionCrashed();
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagLog.Error(LogCat.Stability, "task.unobserved", "TaskScheduler", "Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        if (_argsError != null)
        {
            Log.Error("Neispravni argumenti", _argsError);
        }

        if (_standalone)
        {
            Log.Warn("Otvoreni player ne odgovara, ovaj se pokreće zasebno.");
        }

        StartupOptions options = _options;
        AppSettings settings = AppSettings.Load();

        // Sučelje (prozori, izbornici) iscrtava se bez GPU-a: na nekim upravljačkim programima WPF-ovo GPU iscrtavanje
        // ostavlja bijele prozore, a za ovako jednostavno sučelje razlika u potrošnji je zanemariva (mjereno u M5).
        // Video uvijek ide preko GPU-a (mpv). --gpu-ui ili postavka UiGpuRendering vraćaju GPU iscrtavanje sučelja.
        bool softwareUi = options.SoftwareRender || !(options.GpuUi || settings.UiGpuRendering);
        if (softwareUi)
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        Log.Info($"Inačica: {(AppPackage.IsStore ? "Microsoft Store (MSIX paket)" : "GitHub")}");
        Log.Info($"Sučelje: {(softwareUi ? "iscrtavanje bez GPU-a" : $"GPU iscrtavanje (WPF razina {System.Windows.Media.RenderCapability.Tier >> 16})")}");

        // Tema: dijagnostika je može zadati bez mijenjanja tvojih postavki.
        string theme = options.DiagTheme ?? settings.Theme;
        if (theme == "light")
        {
            JebaitedPlayer.Views.WindowTheme.Apply(theme);
        }

        // Jezik: dijagnostika ga zadaje bez mijenjanja postavki (zadano hrvatski, za postojeće provjere u skriptama);
        // inače zapamćeni jezik ili, prvi put, prozor za odabir (bez odabira ostaje engleski).
        if (options.IsDiagnostics || options.DiagLanguage != null)
        {
            Loc.SetLanguage(options.DiagLanguage ?? "hr");
        }
        else if (!string.IsNullOrWhiteSpace(settings.Language))
        {
            Loc.SetLanguage(settings.Language);
        }
        else
        {
            ChooseLanguage(settings);
        }

        Log.Info($"Jezik sučelja: {Loc.Current}");

        var window = new MainWindow(settings, options);
        MainWindow = window;
        if (_instance != null)
        {
            window.AttachInstance(_instance);
        }

        window.Show();
        if (options.DiagLogInject)
        {
            DiagLog.Info(LogCat.Ui, "test.info", "App", "Controlled test event (INFO)");
            DiagLog.Warn(LogCat.Subtitle, "test.warning", "App", "Controlled test event (WARNING)");
            DiagLog.Error(LogCat.Video, "test.error", "App", "Controlled test event (ERROR)", new InvalidOperationException("controlled test exception"));
            DiagLog.Critical(LogCat.Stability, "test.critical", "App", "Controlled test event (CRITICAL)");
        }
    }

    /// <summary>
    /// Prvo pokretanje (Velopack ga radi odmah nakon instalacije) ili prvo nakon nadogradnje s verzije bez jezika:
    /// prozor za odabir jezika. Nova instalacija dobiva i zadane jezike titlova prema odabranom jeziku.
    /// </summary>
    private void ChooseLanguage(AppSettings settings)
    {
        // Prvi prozor bi postao glavni i njegovo zatvaranje bi ugasilo aplikaciju.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        new JebaitedPlayer.Views.LanguagePickerWindow().ShowDialog();
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        settings.Language = Loc.Current;
        if (settings.IsNew)
        {
            settings.SubtitleLanguages = AppSettings.DefaultSubtitleLanguages(Loc.Current);
        }

        settings.Save();
        Log.Info($"Jezik odabran pri prvom pokretanju: {Loc.Current} (titlovi: {string.Join(", ", settings.SubtitleLanguages)})");
        ShellIntegration.RefreshTextsInBackground();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Log.EndSession();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagLog.Error(LogCat.Ui, "ui.exception", "Dispatcher", "Unhandled UI exception", e.Exception);
        MessageBox.Show(
            Loc.T("error.unexpected", e.Exception.Message, Log.FilePath),
            "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
