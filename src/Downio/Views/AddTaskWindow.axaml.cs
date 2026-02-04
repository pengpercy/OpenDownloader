using Avalonia.Interactivity;
using Downio.ViewModels;

namespace Downio.Views;

public partial class AddTaskWindow : DialogWindow
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
