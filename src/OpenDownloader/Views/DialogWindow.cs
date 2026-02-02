using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenDownloader.Helpers;

namespace OpenDownloader.Views;

public class DialogWindow : Window
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        WindowDragHelper.TryBeginMoveDrag(this, e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var defaultButton = this.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.IsDefault);

            if (defaultButton != null)
            {
                defaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        }
    }
}

