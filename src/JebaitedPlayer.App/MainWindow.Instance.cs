using System.Windows;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer;

/// <summary>
/// Datoteke iz drugih pokretanja (dvoklik na video dok je player otvoren, "Otvori pomoću", više označenih datoteka):
/// otvaraju se u ovom prozoru, a prozor dolazi u prvi plan.
/// </summary>
public partial class MainWindow
{
    /// <summary>Datoteke koje stignu unutar ovog razmaka spajaju se u jedan popis (Explorer pokreće player po datoteci).</summary>
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1.5);

    private readonly OpenRequestMerger _openMerger = new(MergeWindow);
    private readonly List<string> _filesBeforeRender = [];
    private SingleInstance? _instance;
    private DispatcherTimer? _externalOpenTimer;
    private IReadOnlyList<string> _externalOpenFiles = [];
    private bool _rendered;

    /// <summary>Glavna instanca: primaj datoteke iz drugih pokretanja.</summary>
    internal void AttachInstance(SingleInstance instance)
    {
        _instance = instance;
        instance.SetHandler(files => Dispatcher.BeginInvoke(() => OnExternalOpen(files)));
    }

    /// <summary>Prve datoteke: iz naredbenog retka i one koje su stigle dok se prozor otvarao.</summary>
    private void OpenStartupFiles()
    {
        _rendered = true;
        var files = _options.Files.Concat(_filesBeforeRender).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _filesBeforeRender.Clear();
        try
        {
            if (files.Count > 0)
            {
                if (_hiddenStart)
                {
                    ShowMiniPlayer(); // glazba iz naredbenog retka: odmah mini, glavni prozor se nikad ne vidi
                }

                _openMerger.Add(files, DateTime.UtcNow);
                OpenFiles(files);
            }
            else if (_hiddenStart || (_settings.MiniPlayer.WasOpen && !_options.IsDiagnostics))
            {
                // Zatvoren kao mini player: tako se i otvara, na zapamćenom mjestu.
                ShowMiniPlayer();
            }
        }
        finally
        {
            FinishHiddenStart();
        }
    }

    // --- 2.1.7: pokretanje otvara samo jedan prikaz -------------------------------------------------------------------

    private bool _hiddenStart;
    private Point _hiddenStartLocation;
    private WindowState _hiddenStartState = WindowState.Normal;

    /// <summary>
    /// Prije prvog prikaza: ako se treba otvoriti mini player (zadnji prikaz je bio mini ili je zadana samo glazba),
    /// glavni prozor se ne smije ni načas pojaviti. Prije se prvo prikazao veliki prozor, pa tek onda mali.
    /// Glavni prozor se zato prikaže izvan zaslona i bez gumba na taskbaru (mpv i dalje dobiva svoj prozor), a mini ga zatim sakrije.
    /// </summary>
    internal void PrepareStartup()
    {
        if (_options.IsDiagnostics)
        {
            return;
        }

        bool mini = _options.Files.Count == 0
            ? _settings.MiniPlayer.WasOpen
            : _settings.MiniPlayer.AutoSwitch && _options.Files.All(f => File.Exists(f) && JebaitedPlayer.Library.MediaScanner.IsAudio(f));
        if (!mini)
        {
            return;
        }

        _hiddenStart = true;
        _hiddenStartLocation = new Point(Left, Top);
        // WPF ne dopušta Show() uz ShowActivated=false i maksimiziran prozor (InvalidOperationException, player se tada
        // pokrene bez ijednog prozora). Stanje se vraća u FinishHiddenStart.
        _hiddenStartState = WindowState;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = -32000;
        Top = -32000;
        Log.Info("Pokretanje: otvara se mini player (glavni prozor se ne prikazuje).");
    }

    /// <summary>Mini player je preuzeo prikaz (ili nije uspio): glavni prozor dobiva natrag svoje mjesto i gumb na taskbaru.</summary>
    private void FinishHiddenStart()
    {
        if (!_hiddenStart)
        {
            return;
        }

        _hiddenStart = false;
        ShowActivated = true;
        Rect work = SystemParameters.WorkArea;
        Left = double.IsNaN(_hiddenStartLocation.X) ? work.Left + Math.Max(0, (work.Width - Width) / 2) : _hiddenStartLocation.X;
        Top = double.IsNaN(_hiddenStartLocation.Y) ? work.Top + Math.Max(0, (work.Height - Height) / 2) : _hiddenStartLocation.Y;
        ShowInTaskbar = true;
        if (WindowState != _hiddenStartState)
        {
            WindowState = _hiddenStartState;
        }
    }

    /// <summary>Minimiziraj na taskbar (2.1.7): zasebno od mini playera; glazba i radio sviraju dalje.</summary>
    internal void MinimizePlayer()
    {
        if (IsMiniPlayerActive)
        {
            _mini!.WindowState = WindowState.Minimized;
        }
        else
        {
            WindowState = WindowState.Minimized;
        }

        Log.Info("Player minimiziran (reprodukcija se ne prekida).");
    }

    private void OnExternalOpen(IReadOnlyList<string> files)
    {
        if (_closing)
        {
            return;
        }

        Log.Info(files.Count == 0
            ? "Ponovno pokretanje: player je već otvoren."
            : $"Iz drugog pokretanja: {files.Count} {(files.Count == 1 ? "datoteka" : "datoteka/mapa")}");
        _diag?.ExternalOpens.Add(files.Select(Path.GetFileName).ToArray()!);
        BringToFront();
        if (files.Count == 0)
        {
            return;
        }

        if (!_rendered)
        {
            _filesBeforeRender.AddRange(files);
            return;
        }

        // Kratka odgoda: datoteke iz istog odabira u Exploreru stižu u nekoliko stotina milisekundi.
        _externalOpenFiles = _openMerger.Add(files, DateTime.UtcNow);
        _externalOpenTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            _externalOpenTimer!.Stop();
            OpenFiles(_externalOpenFiles);
        }, Dispatcher);
        _externalOpenTimer.Stop();
        _externalOpenTimer.Start();
    }

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            SystemCommands.RestoreWindow(this);
        }

        // Drugo pokretanje je dopustilo prvi plan (AllowSetForegroundWindow); inače Windows samo zatreperi ikonu.
        Activate();
    }

    /// <summary>Pri zatvaranju: player pokrenut u tom trenutku otvara se kao novi, ne šalje datoteke ovom.</summary>
    private void ReleaseInstance()
    {
        _externalOpenTimer?.Stop();
        _instance?.Release();
        _instance = null;
    }
}
