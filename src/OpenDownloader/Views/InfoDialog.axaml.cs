using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenDownloader.Views;

public partial class InfoDialog : Window
{
    public InfoDialog()
    {
        InitializeComponent();
    }

    public InfoDialog(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

