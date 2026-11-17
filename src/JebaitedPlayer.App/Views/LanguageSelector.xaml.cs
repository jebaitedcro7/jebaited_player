using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace JebaitedPlayer.Views;

/// <summary>
/// Odabir jezika sučelja: tri glavna jezika kao pločice (naziv na tom jeziku), ostali u izborniku "Više jezika".
/// Odabir se primijeni odmah (<see cref="Loc.SetLanguage"/>); pamćenje u postavkama radi glavni prozor.
/// </summary>
public partial class LanguageSelector : UserControl
{
    private readonly List<RadioButton> _tiles = [];
    private bool _updating;

    public LanguageSelector()
    {
        InitializeComponent();
        string group = "lang-" + Guid.NewGuid().ToString("N");
        foreach (LanguageInfo language in Loc.Available.Where(l => l.IsPrimary))
        {
            string code = language.Code;
            var tile = new RadioButton
            {
                Style = (Style)FindResource("LanguageTile"),
                Content = language.Name,
                Tag = code,
                GroupName = group,
                ToolTip = language.EnglishName,
            };
            tile.Checked += (_, _) => Select(code);
            _tiles.Add(tile);
            PrimaryPanel.Children.Add(tile);
        }

        MoreButton.Visibility = Loc.Available.Any(l => !l.IsPrimary) ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
        Loaded += (_, _) =>
        {
            Loc.LanguageChanged -= Refresh;
            Loc.LanguageChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => Loc.LanguageChanged -= Refresh;
    }

    private void Select(string code)
    {
        if (!_updating)
        {
            Loc.SetLanguage(code);
        }
    }

    private void Refresh()
    {
        _updating = true;
        foreach (RadioButton tile in _tiles)
        {
            tile.IsChecked = (string)tile.Tag == Loc.Current;
        }

        LanguageInfo? other = Loc.Available.FirstOrDefault(l => !l.IsPrimary && l.Code == Loc.Current);
        MoreButton.Content = other is null ? $"{Loc.T("picker.more")} ▾" : $"{Loc.T("picker.more")}: {other.Name} ▾";
        _updating = false;
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        foreach (LanguageInfo language in Loc.Available.Where(l => !l.IsPrimary))
        {
            string code = language.Code;
            var item = new MenuItem { Header = language.Name, IsChecked = code == Loc.Current, ToolTip = language.EnglishName };
            item.Click += (_, _) => Select(code);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
