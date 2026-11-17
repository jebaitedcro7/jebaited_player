using System.Windows;
using System.Windows.Threading;
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

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Neobrađena greška", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Neobrađena greška u zadatku", args.Exception);
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

        Log.Info($"Sučelje: {(softwareUi ? "iscrtavanje bez GPU-a" : $"GPU iscrtavanje (WPF razina {System.Windows.Media.RenderCapability.Tier >> 16})")}");

        // Tema: dijagnostika je može zadati bez mijenjanja tvojih postavki.
        string theme = options.DiagTheme ?? settings.Theme;
        if (theme == "light")
        {
            JebaitedPlayer.Views.WindowTheme.Apply(theme);
        }

        var window = new MainWindow(settings, options);
        MainWindow = window;
        if (_instance != null)
        {
            window.AttachInstance(_instance);
        }

        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Neobrađena greška u sučelju", e.Exception);
        MessageBox.Show(
            $"Dogodila se neočekivana greška:\n{e.Exception.Message}\n\nDetalji su zapisani u log:\n{Log.FilePath}",
            "jebaited_player", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
