using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace JebaitedPlayer.Infrastructure;

/// <summary>
/// 2.1.9: kotačić miša mijenja glasnoću bilo gdje na prozoru playera. Jedina iznimka su popisi koji se stvarno mogu
/// listati (popis pjesama, biblioteka): tamo kotačić i dalje listaju.
/// </summary>
internal static class WheelHelper
{
    /// <summary>Je li izvor događaja unutar ScrollViewera kojem ima što listati.</summary>
    internal static bool IsInsideScrollable(object? source)
    {
        DependencyObject? node = source as DependencyObject;
        while (node != null)
        {
            if (node is ScrollViewer { ScrollableHeight: > 0 })
            {
                return true;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }
}
