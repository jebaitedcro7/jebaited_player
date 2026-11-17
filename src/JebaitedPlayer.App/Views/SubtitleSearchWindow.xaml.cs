using System.Windows;
using System.Windows.Input;
using JebaitedPlayer.ViewModels;

namespace JebaitedPlayer.Views;

/// <summary>Prozor "Titlovi s interneta": rezultati svih izvora s ocjenom podudaranja i ručni odabir.</summary>
public partial class SubtitleSearchWindow : Window
{
    private readonly OnlineSubtitleController _online;
    private readonly Action _openSources;

    public SubtitleSearchWindow(OnlineSubtitleController online, Action openSources)
    {
        _online = online;
        _openSources = openSources;
        InitializeComponent();
        DataContext = online;
        WindowTheme.UseDarkTitleBar(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
        Loaded += async (_, _) =>
        {
            TitleBox.Focus();
            await _online.PrepareForWindowAsync();
        };
    }

    private void OnSourcesClick(object sender, RoutedEventArgs e) => _openSources();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
