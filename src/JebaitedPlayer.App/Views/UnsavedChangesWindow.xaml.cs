using System.Windows;

namespace JebaitedPlayer.Views;

/// <summary>M3b: odgovor na pitanje o nespremljenim promjenama postavki.</summary>
internal enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// Ciklus 4, M3b: pitanje pri zatvaranju postavki s nespremljenim promjenama. Spremi (Enter), Odbaci promjene ili
/// Odustani (Esc, ×), uz tekstove na jeziku sučelja (a ne Da / Ne / Odustani iz Windowsa).
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    internal UnsavedChangesWindow()
    {
        InitializeComponent();
        WindowTheme.UseDarkTitleBar(this);
    }

    internal UnsavedChoice Choice { get; private set; } = UnsavedChoice.Cancel;

    /// <summary>Pitaj (modalno, iznad prozora postavki).</summary>
    internal static UnsavedChoice Ask(Window owner)
    {
        var dialog = new UnsavedChangesWindow { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private void OnSave(object sender, RoutedEventArgs e) => Finish(UnsavedChoice.Save);

    private void OnDiscard(object sender, RoutedEventArgs e) => Finish(UnsavedChoice.Discard);

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(UnsavedChoice.Cancel);

    private void Finish(UnsavedChoice choice)
    {
        Choice = choice;
        Close();
    }
}
