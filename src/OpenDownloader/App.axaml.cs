using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System;
using System.Linq;
using Avalonia.Markup.Xaml;
using OpenDownloader.Services;
using OpenDownloader.ViewModels;
using OpenDownloader.Views;

namespace OpenDownloader;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            var viewModel = new MainWindowViewModel();
            DataContext = viewModel;

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            
            desktop.MainWindow = mainWindow;

            mainWindow.Closing += (_, _) =>
            {
                _ = viewModel.ShutdownServicesAsync();
            };

            desktop.Exit += (_, _) =>
            {
                _ = viewModel.ShutdownServicesAsync();
            };

            var updateChecked = false;
            mainWindow.Opened += async (_, _) =>
            {
                if (updateChecked) return;
                updateChecked = true;

                var currentVersion = AppVersionProvider.GetCurrentVersion();
                var updateService = new UpdateService();
                var release = await updateService.CheckForUpdatesAsync(currentVersion);
                if (release is null) return;

                var dialog = new UpdateWindow(release);
                await dialog.ShowDialog(mainWindow);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection-based validation is optional")]
    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ToggleMainWindow();
        }
    }
}
