using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JebaitedPlayer.Infrastructure;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.0: kartica Prečaci. Svaka radnja ima do dvije tipke; klik na tipku pa pritisak nove kombinacije (Esc odustaje,
/// Delete briše). Ako kombinaciju ima druga radnja, poruka to kaže, a ponovni pritisak iste kombinacije potvrđuje zamjenu.
/// Promjena vrijedi odmah (isti AppSettings kao glavni prozor); Spremi / Odbaci / Zadano kao i ostale kartice.
/// </summary>
public partial class PlayerSettingsWindow
{
    private Button? _captureButton;
    private string? _captureId;
    private int _captureSlot;
    private KeyChord _pendingConflict;

    private void LoadShortcuts()
    {
        BuildShortcutRows();
        PreviewKeyDown += OnShortcutCaptureKey;
        Tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == Tabs)
            {
                CancelShortcutCapture();
            }
        };
    }

    private void BuildShortcutRows()
    {
        _captureButton = null;
        _captureId = null;
        ShortcutsList.Children.Clear();
        foreach (string category in Shortcuts.Categories)
        {
            ShortcutsList.Children.Add(new TextBlock
            {
                Text = Shortcuts.CategoryName(category),
                Style = (Style)FindResource("SectionHeader"),
                Margin = new Thickness(0, 18, 0, 4),
            });
            foreach (ShortcutAction action in Shortcuts.Actions.Where(a => a.Category == category))
            {
                ShortcutsList.Children.Add(ShortcutRow(action));
            }
        }
    }

    private Grid ShortcutRow(ShortcutAction action)
    {
        bool custom = !Shortcuts.IsDefault(_settings, action.Id);
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Tag = action.Id };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var name = new TextBlock
        {
            Text = Shortcuts.Name(action),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = custom ? FontWeights.SemiBold : FontWeights.Normal,
        };
        grid.Children.Add(name);

        KeyChord[] keys = Shortcuts.Get(_settings, action.Id);
        for (int slot = 0; slot < Shortcuts.Slots; slot++)
        {
            var button = new Button
            {
                Style = (Style)FindResource("SecondaryButton"),
                Content = keys[slot].IsEmpty ? "—" : keys[slot].Display(),
                Margin = new Thickness(6, 0, 0, 0),
                Tag = (action.Id, slot),
                ToolTip = Loc.T("settings.shortcutChange"),
            };
            button.Click += OnShortcutButton;
            Grid.SetColumn(button, slot + 1);
            grid.Children.Add(button);
        }

        if (custom)
        {
            var reset = new Button
            {
                Style = (Style)FindResource("LinkButton"),
                Content = Loc.T("settings.shortcutDefault"),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = action.Id,
            };
            reset.Click += (_, _) => ResetShortcut(action.Id);
            Grid.SetColumn(reset, 3);
            grid.Children.Add(reset);
        }

        return grid;
    }

    private void OnShortcutButton(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ValueTuple<string, int> tag } button)
        {
            StartShortcutCapture(button, tag.Item1, tag.Item2);
        }
    }

    /// <summary>Gumb tipke čeka novu kombinaciju (dijagnostika ga zove izravno).</summary>
    internal void StartShortcutCapture(Button button, string id, int slot)
    {
        CancelShortcutCapture();
        _captureButton = button;
        _captureId = id;
        _captureSlot = slot;
        _pendingConflict = KeyChord.None;
        button.Content = Loc.T("settings.shortcutPress");
        ShortcutsStatus.Text = Loc.T("settings.shortcutPressHint");
    }

    private void CancelShortcutCapture()
    {
        if (_captureButton is { } button && _captureId is { } id)
        {
            KeyChord chord = Shortcuts.Get(_settings, id)[_captureSlot];
            button.Content = chord.IsEmpty ? "—" : chord.Display();
        }

        _captureButton = null;
        _captureId = null;
        _pendingConflict = KeyChord.None;
    }

    private void OnShortcutCaptureKey(object sender, KeyEventArgs e)
    {
        if (_captureButton is null)
        {
            return;
        }

        // Dok se snima, nijedna tipka ne radi ništa drugo (ni Razmak ni Enter na gumbu).
        e.Handled = true;
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        ProcessShortcutKey(key, Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift));
    }

    /// <summary>Obrada pritisnute kombinacije dok gumb čeka tipku; vraća poruku (dijagnostika je provjerava).</summary>
    internal string ProcessShortcutKey(Key key, ModifierKeys mods)
    {
        if (_captureId is not { } id || KeyChord.IsModifierKey(key))
        {
            return ShortcutsStatus.Text;
        }

        if (key == Key.Escape && mods == ModifierKeys.None)
        {
            CancelShortcutCapture();
            ShortcutsStatus.Text = string.Empty;
            return ShortcutsStatus.Text;
        }

        var chord = key == Key.Delete && mods == ModifierKeys.None ? KeyChord.None : new KeyChord(key, mods);
        if (!chord.IsEmpty && Shortcuts.IsReserved(chord))
        {
            ShortcutsStatus.Text = Loc.T("settings.shortcutReserved", chord.Display());
            return ShortcutsStatus.Text;
        }

        string? owner = Shortcuts.Find(_settings, chord);
        if (owner is not null && owner != id && chord != _pendingConflict)
        {
            // Prvi pritisak: samo upozorenje; isti pritisak još jednom = zamjena.
            _pendingConflict = chord;
            ShortcutsStatus.Text = Loc.T("settings.shortcutConflict", chord.Display(), Shortcuts.Name(Shortcuts.Action(owner)!));
            return ShortcutsStatus.Text;
        }

        string actionName = Shortcuts.Name(Shortcuts.Action(id)!);
        string? taken = Shortcuts.Assign(_settings, id, _captureSlot, chord);
        ShortcutsStatus.Text = chord.IsEmpty
            ? Loc.T("settings.shortcutCleared", actionName)
            : taken is null
                ? Loc.T("settings.shortcutSet", chord.Display(), actionName)
                : Loc.T("settings.shortcutMoved", chord.Display(), actionName, Shortcuts.Name(Shortcuts.Action(taken)!));
        AfterShortcutChange();
        return ShortcutsStatus.Text;
    }

    private void ResetShortcut(string id)
    {
        CancelShortcutCapture();
        IReadOnlyList<string> taken = Shortcuts.Reset(_settings, id);
        string actionName = Shortcuts.Name(Shortcuts.Action(id)!);
        ShortcutsStatus.Text = taken.Count == 0
            ? Loc.T("settings.shortcutResetDone", actionName)
            : Loc.T("settings.shortcutResetMoved", actionName, string.Join(", ", taken.Select(t => Shortcuts.Name(Shortcuts.Action(t)!))));
        AfterShortcutChange();
    }

    /// <summary>Dijagnostika: vrati jednu radnju na zadano (isti put kao poveznica „Zadano” u retku).</summary>
    internal void ResetShortcutForTests(string id) => ResetShortcut(id);

    /// <summary>Dijagnostika: gumb tipke za radnju i mjesto.</summary>
    internal Button? ShortcutButton(string id, int slot) => ShortcutsList.Children.OfType<Grid>()
        .FirstOrDefault(g => g.Tag as string == id)?.Children.OfType<Button>()
        .FirstOrDefault(b => b.Tag is ValueTuple<string, int> t && t.Item2 == slot);

    private void AfterShortcutChange()
    {
        double offset = ShortcutsScroll.VerticalOffset;
        BuildShortcutRows();
        ShortcutsScroll.ScrollToVerticalOffset(offset);
        _vm.RefreshShortcuts();
        UpdateSaveState();
    }
}
