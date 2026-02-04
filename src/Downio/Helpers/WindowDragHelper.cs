using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Downio.Helpers;

public static class WindowDragHelper
{
    public static void TryBeginMoveDrag(Window window, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(window);
        if (!point.Properties.IsLeftButtonPressed) return;

        var control = e.Source as Control;
        while (control != null)
        {
            if (control is Button
                || control is TextBox
                || control is CheckBox
                || control is ToggleSwitch
                || control is ComboBox
                || control is Slider
                || control is ScrollBar
                || control is ScrollViewer
                || control is MenuItem
                || control is ListBox
                || control is ListBoxItem)
            {
                return;
            }

            control = control.GetVisualParent() as Control;
        }

        window.BeginMoveDrag(e);
    }
}
