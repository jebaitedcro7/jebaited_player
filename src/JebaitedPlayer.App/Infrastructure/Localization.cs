using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using JebaitedPlayer.Localization;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// Izvor tekstova za XAML: indeksirano svojstvo <c>[ključ]</c>. Kad se jezik promijeni, javlja promjenu svih
/// indeksa ("Item[]"), pa se svi vezani tekstovi u otvorenim prozorima osvježe odmah, bez ponovnog pokretanja.
/// </summary>
public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource()
    {
        Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => Loc.T(key);
}

/// <summary>Pomoć za tekstove koje postavlja kod.</summary>
public static class LocUi
{
    /// <summary>Svojstvo vezano na tekst iz jezika (kao <c>{loc:T ključ}</c> u XAML-u): mijenja se zajedno s jezikom.</summary>
    public static void Bind(this DependencyObject target, DependencyProperty property, string key) =>
        BindingOperations.SetBinding(target, property,
            new Binding($"[{key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay });

    /// <summary>
    /// Dok je prozor otvoren, na promjenu jezika pozovi <paramref name="refresh"/> (tekstovi s brojevima ili stanjem,
    /// koje slaže kod). Pretplata se ukida kad se prozor zatvori.
    /// </summary>
    public static void OnLanguageChanged(this Window window, Action refresh)
    {
        Action handler = () => window.Dispatcher.BeginInvoke(refresh);
        Loc.LanguageChanged += handler;
        window.Closed += (_, _) => Loc.LanguageChanged -= handler;
    }
}

/// <summary>
/// Tekst sučelja u XAML-u: <c>Text="{loc:T home.openFile}"</c>. Vraća vezivanje na <see cref="LocSource"/>,
/// pa se tekst mijenja zajedno s jezikom.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
