using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenDownloader.Models;

namespace OpenDownloader.Views;

public partial class TaskDetailsWindow : Window
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