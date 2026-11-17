using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.ViewModels;
using JebaitedPlayer.Views;

namespace JebaitedPlayer;

/// <summary>Popis epizoda, kartice (nastavak gledanja, sljedeća epizoda), otvaranje mapa i "Nastavi gledati".</summary>
public partial class MainWindow
{
    private CardOverlay? _cards;
    private bool _cardsVisible;

    private void InitializeLibraryUi()
    {
        _cards = new CardOverlay(_vm) { Owner = this };
        _ = _cards.Handle;

        // Epizode grupirane po seriji i sezoni.
        ICollectionView view = CollectionViewSource.GetDefaultView(_vm.Library.Playlist);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlaylistItem.Group)));
        PlaylistItems.ItemsSource = view;

        VideoArea.SizeChanged += (_, _) => UpdateOverlayBounds();

        // "Nastavi gledati" se učita odmah nakon prvog prikaza prozora, da se prozor otvori bez čekanja.
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!_vm.HasMedia)
            {
                _vm.Library.RefreshRecent();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task OpenLibraryAsync(IReadOnlyList<string> files, IReadOnlyList<string> folders)
    {
        try
        {
            await _vm.Library.OpenAsync(files, folders);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Otvaranje", ex);
            Notify($"Otvaranje nije uspjelo: {ex.Message}");
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LibraryController.IsPromptVisible):
                UpdateCards();
                break;
            case nameof(LibraryController.Current):
                ScrollPlaylistToCurrent();
                break;
            case nameof(LibraryController.HasPlaylist) when !_vm.Library.HasPlaylist:
                SetPlaylistPanel(false);
                break;
        }
    }

    private void UpdateCards()
    {
        if (_cards is null || _closing)
        {
            return;
        }

        bool show = _vm.Library.IsPromptVisible && _vm.HasMedia;
        if (show == _cardsVisible)
        {
            return;
        }

        _cardsVisible = show;
        if (show)
        {
            UpdateOverlayBounds();
            _cards.Show();
            RegisterActivity();
        }
        else
        {
            _cards.Hide();
        }
    }

    private void SetPlaylistPanel(bool visible)
    {
        if (visible && !_vm.HasMedia)
        {
            return;
        }

        PlaylistPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible)
        {
            Dispatcher.BeginInvoke(ScrollPlaylistToCurrent, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        RestoreKeyboardFocus();
    }

    private void ScrollPlaylistToCurrent()
    {
        if (PlaylistPanel.Visibility != Visibility.Visible || _vm.Library.Current is not { } current)
        {
            return;
        }

        foreach (FrameworkElement element in FindChildren<FrameworkElement>(PlaylistItems))
        {
            if (ReferenceEquals(element.DataContext, current) && element is System.Windows.Controls.Button)
            {
                element.BringIntoView();
                return;
            }
        }
    }

    private void ShowOpenFolderDialog()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Otvori mapu s filmovima ili serijom",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenFiles([dialog.FolderName]);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => ShowOpenFolderDialog();

    private void OnClosePlaylistClick(object sender, RoutedEventArgs e) => SetPlaylistPanel(false);

    private void OnToggleShortcutsClick(object sender, RoutedEventArgs e) =>
        _vm.ShowWelcomeShortcuts = !_vm.ShowWelcomeShortcuts;

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
