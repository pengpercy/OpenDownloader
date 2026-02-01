using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenDownloader.ViewModels;

namespace OpenDownloader.Views;

public partial class AddTaskWindow : Window
{
    private readonly MainWindowViewModel? _viewModel;

    public AddTaskWindow()
    {
        InitializeComponent();
    }

    public AddTaskWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        // Execute the view model command
        if (_viewModel != null && _viewModel.StartDownloadCommand.CanExecute(null))
        {
            _viewModel.StartDownloadCommand.Execute(null);
            Close();
        }
    }
}
