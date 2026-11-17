using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace JebaitedPlayer.Views;

/// <summary>
/// Stavka izbornika koja ne zatvara otvoreni podizbornik čim miš krene prema njemu.
/// <para>
/// WPF otvara i zatvara podizbornike nakon Windows postavke <c>MenuShowDelay</c>. Kad je ona 0 ms (neki korisnici je
/// tako postave), dijagonalni pokret od "Titlovi" prema stavci podizbornika prijeđe preko susjednih stavki ("Brzina",
/// "Omjer slike") i podizbornik titlova se odmah zatvori. Zato se ovdje:
/// </para>
/// <list type="bullet">
/// <item>odlazak miša s otvorene stavke (npr. "Titlovi") odgodi ~350 ms: ako miš u tom vremenu uđe u podizbornik, ništa se ne zatvara;</item>
/// <item>odabir susjedne stavke odgodi ~350 ms dok je kod susjeda otvoren podizbornik: kratak prolaz preko nje ništa ne mijenja,
/// a zadržavanje na njoj je odabere kao i inače.</item>
/// </list>
/// Windows postavka se ne mijenja.
/// </summary>
public class HoverIntentMenuItem : MenuItem
{
    private static readonly TimeSpan IntentDelay = TimeSpan.FromMilliseconds(350);

    private DispatcherTimer? _enterTimer;
    private DispatcherTimer? _leaveTimer;
    private MouseEventArgs? _pendingLeave;

    public HoverIntentMenuItem()
    {
        // Izgled kao obična stavka (tamni stil iz Themes/Controls.xaml vrijedi za tip MenuItem).
        SetResourceReference(StyleProperty, typeof(MenuItem));
    }

    protected override DependencyObject GetContainerForItemOverride() => new HoverIntentMenuItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is MenuItem or Separator;

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        // Povratak na stavku čiji je podizbornik otvoren: odgođeni odlazak se poništava.
        if (_leaveTimer is { IsEnabled: true })
        {
            _leaveTimer.Stop();
            _pendingLeave = null;
            return;
        }

        if (IsSubmenuOpen || OpenSibling() is null)
        {
            base.OnMouseEnter(e);
            return;
        }

        // Susjed ima otvoren podizbornik: pričekaj da se vidi namjera (miš ostaje ovdje ili ide u podizbornik).
        _enterTimer ??= new DispatcherTimer(IntentDelay, DispatcherPriority.Input, (_, _) => CommitEnter(), Dispatcher);
        _enterTimer.Stop();
        _enterTimer.Start();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        // WPF stavku odabire i pri pomicanju miša preko nje; dok traje odgoda, to se ne smije dogoditi.
        if (_enterTimer is { IsEnabled: true })
        {
            return;
        }

        if (!IsHighlighted && !IsSubmenuOpen && OpenSibling() is not null)
        {
            OnMouseEnter(e);
            return;
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_enterTimer is { IsEnabled: true })
        {
            // Samo prolaz preko stavke: ne mijenja se ništa (ni odabir ni otvoreni podizbornik susjeda).
            _enterTimer.Stop();
            return;
        }

        if (IsSubmenuOpen && HasItems)
        {
            // Odlazak s otvorene stavke: možda prema podizborniku. Zatvaranje (ono što WPF inače radi) tek nakon odgode.
            _pendingLeave = e;
            _leaveTimer ??= new DispatcherTimer(IntentDelay, DispatcherPriority.Input, (_, _) => CommitLeave(), Dispatcher);
            _leaveTimer.Stop();
            _leaveTimer.Start();
            return;
        }

        base.OnMouseLeave(e);
    }

    private void CommitEnter()
    {
        _enterTimer?.Stop();
        if (IsMouseOver)
        {
            base.OnMouseEnter(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        }
    }

    private void CommitLeave()
    {
        _leaveTimer?.Stop();
        MouseEventArgs? leave = _pendingLeave;
        _pendingLeave = null;
        if (leave != null && !IsMouseOver && !IsMouseOverSubmenu())
        {
            base.OnMouseLeave(leave);
        }
    }

    private bool IsMouseOverSubmenu() => GetTemplateChild("PART_Popup") is Popup { IsOpen: true, Child: { IsMouseOver: true } };

    /// <summary>Susjedna stavka (isti roditelj) s otvorenim podizbornikom.</summary>
    private MenuItem? OpenSibling()
    {
        if (ItemsControl.ItemsControlFromItemContainer(this) is not { } parent)
        {
            return null;
        }

        foreach (object item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is MenuItem { IsSubmenuOpen: true } open && !ReferenceEquals(open, this))
            {
                return open;
            }
        }

        return null;
    }
}
