using Avalonia.Interactivity;
using OpenDownloader.Models;

namespace OpenDownloader.Views;

public partial class TaskDetailsWindow : DialogWindow
{
    public TaskDetailsWindow()
    {
        InitializeComponent();
    }

    public TaskDetailsWindow(DownloadTask task) : this()
    {
        DataContext = task;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
