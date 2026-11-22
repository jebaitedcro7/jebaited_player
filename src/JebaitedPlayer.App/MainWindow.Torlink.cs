#if TORLINK
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Torlink;
using JebaitedPlayer.Views;
using JebaitedPlayer.Views.Torlink;

namespace JebaitedPlayer;

/// <summary>
/// 2.2.14: Torlink (samo GitHub inačica). Gumb "Pretraži online" uz "Otvori datoteku" i "Otvori mapu" otvara zasebnu Torlink
/// početnu stranicu u istom prostoru kao početni ekran; gumb Home (Esc, Alt+←) vraća na naš početni ekran. Preuzimanja se
/// nastavljaju dok si na Homeu (čip dolje desno). Isključen Torlink = nema gumba, čipa, procesa ni mreže.
/// Sav kod je u ovoj datoteci i u Views\Torlink; u Store izdanju se ne gradi (TorlinkEnabled=false).
/// </summary>
public partial class MainWindow
{
    private TorlinkService? _torlink;
    private TorlinkPage? _torlinkPage;
    private bool _torlinkView;
    private Button? _torlinkHomeButton;
    private Button? _torlinkChip;
    private DispatcherTimer? _torlinkTimer;
    private bool _torlinkTickRunning;

    internal bool IsTorlinkView => _torlinkView;

    internal TorlinkService? TorlinkForTests => _torlink;

    internal TorlinkPage? TorlinkPageForTests => _torlinkPage;

    internal Button? TorlinkHomeButtonForTests => _torlinkHomeButton;

    private void InitializeTorlink()
    {
        _torlink = TorlinkService.Create(_settings);
        BuildTorlinkHomeUi();
        _torlink.Changed += () => Dispatcher.BeginInvoke(UpdateTorlinkUi);
        _torlinkTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = TorlinkTickAsync(), Dispatcher);
        UpdateTorlinkUi();

        // Nedovršena preuzimanja se nastavljaju sama (Torlink ih obnavlja iz svog stanja); inače proces ne radi.
        // InitializeTorlink se zove iz OnLoaded, pa se ne čeka događaj Loaded (već je prošao).
        _ = StartPendingTorlinkAsync();
    }

    /// <summary>Živa veza na tekst iz Languages/*.json (kao loc:T u XAML-u); ključ ostaje doslovan niz da ga provjera ključeva vidi.</summary>
    internal static Binding LocBinding(string key) => new($"[{key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };

    private async Task StartPendingTorlinkAsync()
    {
        try
        {
            bool started = await _torlink!.StartIfWorkPendingAsync();
            Log.Info($"Torlink: nedovršena preuzimanja pri pokretanju playera: {(started ? "proces pokrenut" : "nema posla ili je isključen")}.");
        }
        catch (Exception ex)
        {
            Log.Error("Torlink: pokretanje nedovršenih preuzimanja", ex);
        }
    }

    private void BuildTorlinkHomeUi()
    {
        var icon = new TextBlock { Text = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        var label = new TextBlock { Margin = new Thickness(10, 0, 0, 0), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        label.SetBinding(TextBlock.TextProperty, LocBinding("torlink.homeButton"));
        _torlinkHomeButton = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } },
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(18, 10, 18, 10),
            Visibility = Visibility.Collapsed,
        };
        _torlinkHomeButton.SetResourceReference(StyleProperty, "SecondaryButton");
        _torlinkHomeButton.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty,
            LocBinding("torlink.homeButton"));
        _torlinkHomeButton.Click += (_, _) => OpenTorlink();
        HomeOpenButtons.Children.Add(_torlinkHomeButton);

        // LinkButton prikazuje Content kao tekst (TemplateBinding na TextBlock.Text): sadržaj mora biti niz, ne element.
        _torlinkChip = new Button { Content = string.Empty, FontSize = 12, Visibility = Visibility.Collapsed, Padding = new Thickness(10, 4, 10, 4) };
        _torlinkChip.SetResourceReference(StyleProperty, "LinkButton");
        _torlinkChip.HorizontalAlignment = HorizontalAlignment.Right;
        _torlinkChip.VerticalAlignment = VerticalAlignment.Bottom;
        _torlinkChip.Margin = new Thickness(0, 0, 96, 10);
        Grid.SetRowSpan(_torlinkChip, 2);
        Panel.SetZIndex(_torlinkChip, 5);
        _torlinkChip.SetBinding(ToolTipProperty, LocBinding("torlink.chipTooltip"));
        _torlinkChip.Click += (_, _) => OpenTorlink("active");
        WelcomePanel.Children.Add(_torlinkChip);
    }

    /// <summary>Gumb, čip i stranica prema trenutnom stanju (uključeno/isključeno, preuzimanja).</summary>
    private void UpdateTorlinkUi()
    {
        if (_torlink is null || _torlinkHomeButton is null || _torlinkChip is null)
        {
            return;
        }

        bool available = _torlink.IsAvailable;
        _torlinkHomeButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;

        DownloadSnapshot snap = _torlink.Host.Snapshot;
        bool chip = available && snap.DownloadingCount > 0;
        _torlinkChip.Visibility = chip ? Visibility.Visible : Visibility.Collapsed;
        if (chip)
        {
            _torlinkChip.Content = $"⬇ {snap.DownloadingCount} · {DownloadSnapshot.FormatSpeed(snap.TotalSpeed)}";
        }

        if (!available && _torlinkView)
        {
            CloseTorlink();
        }
    }

    /// <summary>Otvori Torlink stranicu (po potrebi na kartici preuzimanja); ako Torlink nije uključen, vodi u Postavke → Torlink.</summary>
    internal void OpenTorlink(string? downloadsTab = null)
    {
        if (_torlink is null || !_torlink.IsAvailable)
        {
            ShowPlayerSettings(SettingsTab.Torlink);
            return;
        }

        if (_vm.HasMedia && !_homeView)
        {
            return; // na stranicu se dolazi s početnog ekrana, ne preko videa
        }

        EnsureTorlinkPage();
        _torlinkView = true;
        UpdatePanels();
        _torlinkPage!.Open(downloadsTab);
        Log.Info("Torlink: stranica otvorena.");
    }

    internal void CloseTorlink()
    {
        if (!_torlinkView)
        {
            return;
        }

        _torlinkView = false;
        _torlinkPage?.Close();
        UpdatePanels();
        RestoreKeyboardFocus();
        Log.Info("Torlink: povratak na Home.");
    }

    private void ToggleTorlink()
    {
        if (_torlinkView)
        {
            CloseTorlink();
        }
        else
        {
            OpenTorlink();
        }
    }

    private void EnsureTorlinkPage()
    {
        if (_torlinkPage is not null)
        {
            return;
        }

        _torlinkPage = new TorlinkPage(_torlink!, CloseTorlink, () => ShowPlayerSettings(SettingsTab.Torlink), PlayFromTorlink)
        {
            Visibility = Visibility.Collapsed,
        };
        VideoArea.Children.Add(_torlinkPage);
    }

    /// <summary>Reproduciraj završeno preuzimanje: vrati se na Home i otvori datoteku ili mapu u playeru.</summary>
    private void PlayFromTorlink(string path)
    {
        CloseTorlink();
        OpenFiles([path]);
    }

    /// <summary>Iz UpdatePanels: stranica zamjenjuje početni ekran dok je otvorena.</summary>
    private void UpdateTorlinkPanel()
    {
        if (_torlinkPage is null)
        {
            return;
        }

        bool welcome = WelcomePanel.Visibility == Visibility.Visible;
        if (_torlinkView && welcome)
        {
            WelcomePanel.Visibility = Visibility.Collapsed;
            _torlinkPage.Visibility = Visibility.Visible;
        }
        else
        {
            _torlinkPage.Visibility = Visibility.Collapsed;
            if (_torlinkView)
            {
                // Otvoren je film ili glazba preko stranice (npr. povlačenjem datoteke): stranica se zatvara.
                _torlinkView = false;
                _torlinkPage.Close();
            }
        }
    }

    private async Task TorlinkTickAsync()
    {
        if (_torlink is null || !_torlink.Settings.Enabled || _torlinkTickRunning || _closing)
        {
            return;
        }

        _torlinkTickRunning = true;
        try
        {
            await _torlink.Host.TickAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Torlink: osvježavanje", ex);
        }
        finally
        {
            _torlinkTickRunning = false;
        }
    }

    /// <summary>Zatvaranje playera s aktivnim preuzimanjima: pitanje "Ostani" / "Zatvori player" (preuzimanja se nastavljaju pri sljedećem pokretanju).</summary>
    private bool ConfirmTorlinkClose()
    {
        if (_torlink is null || _options.IsDiagnostics || !_torlink.IsAvailable || _torlink.Host.Snapshot.DownloadingCount == 0)
        {
            return true;
        }

        MessageBoxResult r = MessageBox.Show(this, Loc.T("torlink.closeDownloadsText", _torlink.Host.Snapshot.DownloadingCount),
            Loc.T("torlink.closeDownloadsTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        return r == MessageBoxResult.Yes;
    }

    private async Task ShutdownTorlinkAsync()
    {
        _torlinkTimer?.Stop();
        if (_torlink is null)
        {
            return;
        }

        try
        {
            await _torlink.Host.ShutdownAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Torlink: gašenje", ex);
        }

        _torlink.Dispose();
    }
}
#endif
