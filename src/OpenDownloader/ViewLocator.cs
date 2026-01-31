using Avalonia.Controls;
using Avalonia.Controls.Templates;
using OpenDownloader.ViewModels;

namespace OpenDownloader;

public partial class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        // Use the generated AutoBuild method
        var control = AutoBuild(param);
        
        if (control != null)
        {
            return control;
        }

        return new TextBlock { Text = "Not Found: " + param.GetType().Name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
