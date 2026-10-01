using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace QbaswingMultiTask.Desktop;

public partial class App : Application
{
    // Initialize() lo genera el compilador de XAML de Avalonia. NO se escribe a
    // mano como "AvaloniaXamlLoader.Load(this)": si se hace, el compilador no
    // genera el codigo que asigna los campos x:Name y todos quedan a null.
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}