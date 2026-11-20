using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Online;
using JebaitedPlayer.Services;

namespace JebaitedPlayer.Views;

/// <summary>
/// Ciklus 5, V8 (S1): Spotify daljinski. Vodič za vlastiti Client ID, prijava (PKCE u pregledniku), trenutna pjesma
/// s omotom, upravljanje, playliste, spremljene pjesme, pretraga i izbor uređaja (Spotify Connect).
/// </summary>
public partial class SpotifyWindow : Window
{
    private readonly SpotifyRemote _remote;
    private string? _coverUrl;
    private bool _seeking;
    private bool _updatingVolume;
    private bool _editingClientId;

    internal SpotifyWindow(SpotifyRemote remote)
    {
        _remote = remote;
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
        RedirectBox.Text = SpotifyRemote.RedirectUri;
        PlaylistsTab.Checked += async (_, _) => await ShowListAsync("playlists");
        SavedTab.Checked += async (_, _) => await ShowListAsync("saved");
        SearchTab.Checked += (_, _) =>
        {
            SearchRow.Visibility = Visibility.Visible;
            ItemList.ItemsSource = null;
            SearchBox.Focus();
        };
        ProgressSlider.PreviewMouseLeftButtonDown += (_, _) => _seeking = true;
        ProgressSlider.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            _seeking = false;
            await _remote.SeekAsync((long)ProgressSlider.Value);
        };
        VolumeSlider.ValueChanged += async (_, e) =>
        {
            if (!_updatingVolume && VolumeSlider.IsMouseCaptureWithin)
            {
                await _remote.VolumeAsync((int)e.NewValue);
            }
        };

        _remote.Changed += OnRemoteChanged;
        _remote.AddConsumer();
        Closed += (_, _) =>
        {
            _remote.Changed -= OnRemoteChanged;
            _remote.RemoveConsumer();
        };
        Loaded += async (_, _) =>
        {
            Render();
            if (_remote.IsSignedIn)
            {
                PlaylistsTab.IsChecked = true;
                await _remote.RefreshStateAsync();
            }
        };
    }

    /// <summary>Dijagnostika: stavke na popisu.</summary>
    internal IReadOnlyList<SpotifyItem> Items => ItemList.ItemsSource as IReadOnlyList<SpotifyItem> ?? [];

    private void OnRemoteChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Render);

    internal void Render()
    {
        bool setup = !_remote.HasClientId || _editingClientId;
        SetupPanel.Visibility = setup ? Visibility.Visible : Visibility.Collapsed;
        AccountPanel.Visibility = setup ? Visibility.Collapsed : Visibility.Visible;
        bool signedIn = _remote.IsSignedIn && !setup;
        NowPanel.Visibility = BrowseHeader.Visibility = ListBorder.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        if (!signedIn)
        {
            SearchRow.Visibility = Visibility.Collapsed;
        }

        SignInButton.Visibility = _remote.IsSignedIn ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = _remote.IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountText.Text = _remote.IsSignedIn ? Loc.T("spotify.signedIn") : Loc.T("spotify.notSignedIn");
        StatusText.Text = _remote.Status;

        SpotifyPlayback? s = _remote.State;
        TrackText.Text = s is { Track.Length: > 0 } ? s.Track : Loc.T("spotify.nothingPlaying");
        ArtistText.Text = s?.Artists ?? string.Empty;
        DeviceText.Text = s?.DeviceName is { Length: > 0 } device ? Loc.T("spotify.playingOn", device) : string.Empty;
        PlayPauseButton.Content = s is { IsPlaying: true } ? "" : "";
        OpenInSpotifyButton.Visibility = s?.TrackUrl is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        long duration = Math.Max(1, s?.DurationMs ?? 1);
        ProgressSlider.Maximum = duration;
        if (!_seeking)
        {
            ProgressSlider.Value = Math.Min(duration, s?.ProgressMs ?? 0);
        }

        TimeText.Text = TimeFormat.Format((s?.ProgressMs ?? 0) / 1000.0, false);
        DurationText.Text = TimeFormat.Format((s?.DurationMs ?? 0) / 1000.0, false);
        if (s?.Volume is int volume && !VolumeSlider.IsMouseCaptureWithin)
        {
            _updatingVolume = true;
            VolumeSlider.Value = volume;
            _updatingVolume = false;
        }

        if (s?.ImageUrl != _coverUrl)
        {
            _coverUrl = s?.ImageUrl;
            CoverImage.Source = _coverUrl is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps
                ? new BitmapImage(uri) { CacheOption = BitmapCacheOption.OnLoad }
                : null;
        }
    }

    // --- Postavljanje i prijava ----------------------------------------------------------------------------------------

    private void OnOpenDashboard(object sender, RoutedEventArgs e) => OpenUrl(SpotifyRemote.DeveloperDashboard);

    private void OnCopyRedirect(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SpotifyRemote.RedirectUri);
            StatusText.Text = Loc.T("spotify.copied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            RedirectBox.SelectAll();
        }
    }

    private void OnSaveClientId(object sender, RoutedEventArgs e) => SaveClientId(ClientIdBox.Text);

    internal void SaveClientId(string clientId)
    {
        string id = clientId.Trim();
        if (id.Length < 16 || id.Any(c => !char.IsLetterOrDigit(c)))
        {
            StatusText.Text = Loc.T("spotify.badClientId");
            return;
        }

        _editingClientId = false;
        _remote.SetClientId(id);
        Render();
    }

    private void OnChangeClientId(object sender, RoutedEventArgs e)
    {
        _editingClientId = true;
        ClientIdBox.Text = string.Empty;
        Render();
    }

    private async void OnSignIn(object sender, RoutedEventArgs e)
    {
        SignInButton.IsEnabled = false;
        try
        {
            if (await _remote.SignInAsync())
            {
                PlaylistsTab.IsChecked = true;
                await ShowListAsync("playlists");
            }
        }
        finally
        {
            SignInButton.IsEnabled = true;
            Render();
        }
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _remote.SignOut();
        ItemList.ItemsSource = null;
        Render();
    }

    // --- Upravljanje --------------------------------------------------------------------------------------------------

    private async void OnPlayPause(object sender, RoutedEventArgs e) => await _remote.TogglePlayAsync();

    private async void OnNext(object sender, RoutedEventArgs e) => await _remote.NextAsync();

    private async void OnPrevious(object sender, RoutedEventArgs e) => await _remote.PreviousAsync();

    private async void OnPlayItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SpotifyItem item })
        {
            await _remote.PlayAsync(item);
        }
    }

    private void OnOpenInSpotify(object sender, RoutedEventArgs e)
    {
        if (_remote.State?.TrackUrl is { } url)
        {
            OpenUrl(url);
        }
    }

    private async void OnDevices(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<SpotifyDevice> devices = await _remote.DevicesAsync();
        var menu = new ContextMenu { PlacementTarget = DeviceButton, Placement = PlacementMode.Bottom };
        if (devices.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Loc.T("spotify.noDevices"), IsEnabled = false });
        }

        foreach (SpotifyDevice device in devices)
        {
            var item = new MenuItem { Header = $"{device.Name} ({device.Type})", IsChecked = device.IsActive };
            item.Click += async (_, _) => await _remote.TransferAsync(device);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    // --- Popisi --------------------------------------------------------------------------------------------------------

    internal async Task ShowListAsync(string view)
    {
        SearchRow.Visibility = view == "search" ? Visibility.Visible : Visibility.Collapsed;
        ItemList.ItemsSource = view switch
        {
            "playlists" => await _remote.PlaylistsAsync(),
            "saved" => await _remote.SavedTracksAsync(),
            _ => ItemList.ItemsSource,
        };
        StatusText.Text = _remote.Status;
    }

    private async void OnSearch(object sender, RoutedEventArgs e) => await SearchAsync(SearchBox.Text);

    private async void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchAsync(SearchBox.Text);
        }
    }

    internal async Task SearchAsync(string query)
    {
        if (query.Trim().Length == 0)
        {
            return;
        }

        IReadOnlyList<SpotifyItem> found = await _remote.SearchAsync(query.Trim());
        ItemList.ItemsSource = found;
        StatusText.Text = found.Count == 0 && _remote.Status.Length == 0 ? Loc.T("spotify.nothingFound") : _remote.Status;
    }

    private static void OpenUrl(string url)
    {
        if (url.StartsWith("https://", StringComparison.Ordinal))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}
