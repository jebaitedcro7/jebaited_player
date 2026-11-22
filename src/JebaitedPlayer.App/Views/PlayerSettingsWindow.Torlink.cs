#if TORLINK
using System.Windows.Controls;
using System.Windows.Data;
using JebaitedPlayer.Infrastructure;
using JebaitedPlayer.Services;
using JebaitedPlayer.Views.Torlink;

namespace JebaitedPlayer.Views;

/// <summary>
/// 2.2.14: kartica Postavke → Torlink (samo GitHub inačica). Umeće se ispred "Nadogradnje" u kodu (ne u XAML-u), pa Store izdanje nema ni trag;
/// indeksi ostalih kartica (enum <see cref="SettingsTab"/>) ostaju isti jer se preslikavaju ovdje.
/// </summary>
public partial class PlayerSettingsWindow
{
    private int _torlinkTabIndex = -1;
    private TorlinkSettingsPanel? _torlinkPanel;

    internal TorlinkSettingsPanel? TorlinkPanelForTests => _torlinkPanel;

    private void LoadTorlinkTab()
    {
        if (TorlinkService.Current is not { } svc)
        {
            return;
        }

        _torlinkPanel = new TorlinkSettingsPanel(_settings, svc, () =>
        {
            SaveNow();
            return true;
        });
        var item = new TabItem
        {
            Tag = "",
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = _torlinkPanel,
            },
        };
        item.SetResourceReference(StyleProperty, "SettingsTab");
        item.SetBinding(HeaderedContentControl.HeaderProperty, new Binding("[torlink.name]") { Source = LocSource.Instance, Mode = BindingMode.OneWay });
        _torlinkTabIndex = Tabs.Items.IndexOf(UpdatesTab);
        Tabs.Items.Insert(_torlinkTabIndex, item);
        Closed += (_, _) => _ = _torlinkPanel.ApplyOptionsAsync();
    }

    private int TorlinkTabIndexOf(SettingsTab tab)
    {
        if (_torlinkTabIndex < 0)
        {
            return (int)tab;
        }

        if (tab == SettingsTab.Torlink)
        {
            return _torlinkTabIndex;
        }

        return (int)tab >= _torlinkTabIndex ? (int)tab + 1 : (int)tab;
    }

    private SettingsTab TorlinkTabAt(int index)
    {
        if (_torlinkTabIndex < 0)
        {
            return (SettingsTab)index;
        }

        if (index == _torlinkTabIndex)
        {
            return SettingsTab.Torlink;
        }

        return (SettingsTab)(index > _torlinkTabIndex ? index - 1 : index);
    }
}
#endif
