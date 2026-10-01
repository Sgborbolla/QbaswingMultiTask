using Avalonia.Controls;
using Avalonia.Interactivity;
using QbaswingMultiTask.Devices;

namespace QbaswingMultiTask.Desktop.Views;

public partial class TabSincronizador : UserControl
{
    public TabSincronizador()
    {
        InitializeComponent();
    }

    private void OnDiscoIzq(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceInfo d } && DataContext is SincronizadorViewModel vm)
            vm.IzquierdaDe(d);
    }

    private void OnDiscoDer(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceInfo d } && DataContext is SincronizadorViewModel vm)
            vm.DerechaDe(d);
    }
}
