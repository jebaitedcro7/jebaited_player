using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using JebaitedPlayer.Diagnostics;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Bilježi radnje u sučelju playera na jednom mjestu (class handleri): klik na gumb ili izbornik, otvaranje i zatvaranje
/// izbornika i prozora. Samo prozori ovog programa; nema hookova na razini Windowsa. Tekst kontrole se zapisuje samo ako je
/// kratak i ne izgleda kao naziv datoteke ili putanja.
/// </summary>
internal static partial class UiEvents
{
    private static bool _installed;

    [GeneratedRegex(@"\.[A-Za-z0-9]{2,4}$")]
    private static partial Regex LooksLikeFile();

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent, new RoutedEventHandler(OnClick), true);
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.ClickEvent, new RoutedEventHandler(OnClick), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(OnMenuOpened), true);
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler(OnMenuClosed), true);
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnWindowLoaded), true);
    }

    private static void OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe)
            {
                return;
            }

            string name = AutomationProperties.GetAutomationId(fe);
            if (string.IsNullOrEmpty(name))
            {
                name = fe.Name;
            }

            string? label = sender switch
            {
                MenuItem m => m.Header as string,
                ContentControl c => c.Content as string,
                _ => null,
            };
            label = Clean(label);
            DiagLog.Info(LogCat.Usage, "ui.click", "UiEvents", "Control clicked",
                ("control", string.IsNullOrEmpty(name) ? fe.GetType().Name : name), ("label", label),
                ("window", Window.GetWindow(fe)?.GetType().Name));
        }
        catch (Exception)
        {
            // Praćenje nikad ne smije smetati sučelju.
        }
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Replace("_", string.Empty).Trim();
        return text.Length > 30 || text.Contains('\\') || text.Contains('/') || LooksLikeFile().IsMatch(text) ? null : text;
    }

    private static void OnMenuOpened(object sender, RoutedEventArgs e) =>
        DiagLog.Info(LogCat.Ui, "ui.menu.open", "UiEvents", "Menu opened", ("menu", (sender as FrameworkElement)?.Name));

    private static void OnMenuClosed(object sender, RoutedEventArgs e) =>
        DiagLog.Debug(LogCat.Ui, "ui.menu.close", "UiEvents", "Menu closed", ("menu", (sender as FrameworkElement)?.Name));

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window w)
        {
            return;
        }

        string type = w.GetType().Name;
        DiagLog.Info(LogCat.Ui, "ui.window.open", "UiEvents", "Window opened",
            ("window", type), ("width", Math.Round(w.ActualWidth)), ("height", Math.Round(w.ActualHeight)), ("state", w.WindowState));
        w.Closed += (_, _) => DiagLog.Info(LogCat.Ui, "ui.window.close", "UiEvents", "Window closed", ("window", type));
    }
}
