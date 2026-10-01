using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace QbaswingMultiTask.Desktop;

/// <summary>Base de los view models: notifica cambios a la vista.</summary>
public abstract class Vm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? p = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? p = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(p); return true;
    }
}

/// <summary>Comando minimo, sin dependencias de Avalonia.</summary>
public sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _ejecutar;
    private readonly Func<bool>? _puede;

    public RelayCommand(Action ejecutar, Func<bool>? puede = null)
    {
        _ejecutar = ejecutar;
        _puede = puede;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _puede?.Invoke() ?? true;
    public void Execute(object? parameter) => _ejecutar();
    public void Avisar() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}