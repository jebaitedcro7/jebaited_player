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
        if (files.Count > 0)
        {
            _openMerger.Add(files, DateTime.UtcNow);
            OpenFiles(files);
        }
        else if (_settings.MiniPlayer.WasOpen && !_options.IsDiagnostics)
        {
            // Zatvoren kao mini player: tako se i otvara, na zapamćenom mjestu.
            ShowMiniPlayer();
        }
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
