using System.Windows;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Iptv;

namespace JebaitedPlayer.Views;

/// <summary>Postavke → IPTV: prekidač modula. Isključen modul nema stavku u izborniku i ne pokreće nikakve zadatke.</summary>
public partial class PlayerSettingsWindow
{
    private bool _iptvLoading;

    private void LoadIptv()
    {
        _iptvLoading = true;
        try
        {
            IptvEnableBox.IsChecked = new IptvStore(AppPaths.DataDir).LoadSettings().Enabled;
        }
        finally
        {
            _iptvLoading = false;
        }
    }

    private void OnIptvEnableChanged(object sender, RoutedEventArgs e)
    {
        if (_iptvLoading)
        {
            return;
        }

        bool on = IptvEnableBox.IsChecked == true;
        var store = new IptvStore(AppPaths.DataDir);
        IptvSettings s = store.LoadSettings();
        s.Enabled = on;
        store.SaveSettings(s);
        if (!on && Application.Current.MainWindow is MainWindow { IptvPanelRef: { } _ } m0)
        {
            m0.HideIptv();
        }
    }

    private void OnIptvOpen(object sender, RoutedEventArgs e)
    {
        if (IptvEnableBox.IsChecked == true && Application.Current.MainWindow is MainWindow m)
        {
            m.ShowIptv(this);
        }
    }
}
