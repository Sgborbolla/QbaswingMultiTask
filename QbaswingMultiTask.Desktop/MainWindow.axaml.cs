using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using QbaswingMultiTask.App;
using QbaswingMultiTask.Desktop;
using QbaswingMultiTask.Devices;

namespace QbaswingMultiTask.Desktop;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext!;

    /// <summary>
    /// PLAN.md: "Refresco de dispositivos cada 1 s (no 1 ms: evita robar CPU al
    /// motor)". Es lo que hace que el programa lea solo los dispositivos: al
    /// enchufar una memoria aparece su rectangulo sin tocar nada, y al
    /// desconectarla desaparece.
    /// </summary>
    private readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();

        DataContext = new MainViewModel();

        // Tema guardado (§2.7): se aplica al arrancar y al pulsar Guardar.
        AplicarTema(SettingsStore.Actual.Tema);
        Vm.Ajustes.Guardado += s => AplicarTema(s.Tema);

        _reloj.Tick += (_, _) => Refrescar();
        _reloj.Start();

        KeyDown += OnAtajo;
        Closed += (_, _) => _reloj.Stop();
    }

    /// <summary>Cambia entre claro y oscuro en caliente (Avalonia ThemeVariant).</summary>
    private void AplicarTema(string tema) =>
        RequestedThemeVariant = string.Equals(tema, "claro", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

    private void Refrescar()
    {
        if (DataContext is not MainViewModel vm) return;
        if (!vm.PuedeRefrescar) return;   // no se escanea mientras copia
        vm.RefrescarDispositivos();
    }

    // ---------- rectangulos: clic para marcar ----------

    private void OnDiscoPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: DeviceViewModel d }) return;

        // Doble clic: mini-explorador con el contenido de la unidad.
        if (e.ClickCount >= 2)
        {
            d.CargarContenido();
            Vm.Destacado = d;
            Vm.StatusLine = $"{d.Label}: {d.Contenido.Count} elementos en la raiz";
            return;
        }

        Vm.Marcar(d, !d.Marked);
        Vm.StatusLine = d.Marked
            ? $"{d.Label} marcado como destino ({Vm.Marcados} en total)"
            : $"{d.Label} quitado de los destinos";
    }

    // ---------- botonera del encabezado ----------

    private void OnLectorPrimero(object? sender, RoutedEventArgs e)
    {
        // "Lector primero": el destino mas probable va marcado y en primer lugar.
        var lector = Vm.Devices.FirstOrDefault(d => d.Info.Kind == QbaswingMultiTask.Devices.DeviceKind.Optical)
                     ?? Vm.Devices.FirstOrDefault(d => d.Info.IsExternal)
                     ?? Vm.Devices.FirstOrDefault();
        if (lector is null) { Vm.StatusLine = "no hay ninguna unidad a la que copiar"; return; }

        Vm.MarcarTodos(false);
        Vm.Marcar(lector, true);
        Vm.StatusLine = $"lector primero: {lector.Label} es el destino principal";
    }

    private void OnTodo(object? sender, RoutedEventArgs e)
    {
        var marcar = Vm.Marcados < Vm.Devices.Count;
        Vm.MarcarTodos(marcar);
        Vm.StatusLine = marcar ? $"todos los destinos marcados ({Vm.Marcados})" : "destinos desmarcados";
    }

    private void OnExternos(object? sender, RoutedEventArgs e)
    {
        Vm.MarcarExternos();
        Vm.StatusLine = $"solo los externos marcados ({Vm.Marcados})";
    }

    private void OnInsertar(object? sender, RoutedEventArgs e)
    {
        Refrescar();
        Vm.StatusLine = $"{Vm.ResumenDispositivos}. Los que se enchufen aparecen solos.";
    }

    // ---------- expulsar / formatear (F2) ----------

    private async void OnExpulsar(object? sender, RoutedEventArgs e)
    {
        var d = Vm.Devices.FirstOrDefault(x => x.Marked) ?? Vm.Destacado;
        if (d is null) { Vm.StatusLine = "marca o destaca una unidad para expulsarla"; return; }
        await ExpulsarAsync(d);
    }

    private async void OnMenuExpulsar(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DeviceViewModel d }) await ExpulsarAsync(d);
    }

    private async Task ExpulsarAsync(DeviceViewModel d)
    {
        Vm.StatusLine = $"expulsando {d.Label}...";
        var r = await DeviceActions.ExpulsarAsync(d.Info);
        Vm.StatusLine = r.Ok
            ? $"{d.Label} expulsada con seguridad"
            : $"no se pudo expulsar {d.Label}: {r.Mensaje}";
        if (r.Ok) Refrescar();
    }

    private void OnMenuMarcar(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DeviceViewModel d }) Vm.Marcar(d, !d.Marked);
    }

    private void OnMenuContenido(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: DeviceViewModel d }) return;
        d.CargarContenido();
        Vm.Destacado = d;
        Vm.StatusLine = $"{d.Label}: {d.Contenido.Count} elementos en la raiz.";
    }

    private async void OnMenuFormatear(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: DeviceViewModel d }) return;
        if (!await ConfirmarFormatoAsync(d)) { Vm.StatusLine = "formateo cancelado"; return; }

        Vm.StatusLine = $"formateando {d.Label}...";
        var r = await DeviceActions.FormatearAsync(d.Info, "NTFS", d.Info.Label);
        Vm.StatusLine = r.Ok
            ? $"{d.Label} formateada en NTFS"
            : $"no se pudo formatear {d.Label}: {r.Mensaje}";
        Refrescar();
    }

    /// <summary>
    /// Confirmacion de formateo: una ventana pequena que exige escribir BORRAR.
    /// PLAN.md F2 dice "formatear (confirmado)": no puede borrar sin querer.
    /// </summary>
    private async Task<bool> ConfirmarFormatoAsync(DeviceViewModel d)
    {
        var ventana = new Window
        {
            Title = "Formatear " + d.Label,
            Width = 430, Height = 215,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var caja = new TextBox { Watermark = "BORRAR", Width = 320 };
        var aviso = new TextBlock
        {
            Text = $"Se borrara TODO el contenido de {d.Label} ({d.Info.Root}).\n" +
                   "Escribe BORRAR para confirmar.",
            TextWrapping = TextWrapping.Wrap
        };
        var si = new Button { Content = "Formatear", IsEnabled = false };
        var no = new Button { Content = "Cancelar" };
        var resultado = false;

        caja.TextChanged += (_, _) => si.IsEnabled =
            string.Equals(caja.Text?.Trim(), "BORRAR", StringComparison.OrdinalIgnoreCase);
        si.Click += (_, _) => { resultado = true; ventana.Close(); };
        no.Click += (_, _) => ventana.Close();

        var botones = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        botones.Children.Add(si);
        botones.Children.Add(no);

        var pila = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12 };
        pila.Children.Add(aviso);
        pila.Children.Add(caja);
        pila.Children.Add(botones);
        ventana.Content = pila;

        await ventana.ShowDialog(this);
        return resultado;
    }

    private void OnFiltro(object? sender, RoutedEventArgs e) =>
        Vm.StatusLine = Vm.TextoFechas == "__ → __"
            ? "sin filtro de fecha: se copia todo"
            : $"filtro de fecha: {Vm.TextoFechas}";

    // ---------- franja de abajo ----------
    // F1 ya existe: estos botones llaman al motor de copia de verdad, con hash,
    // fechas y atributos. Lo que aun no esta (pausa, cola de reintentos) lo dice.

    private async void OnCopiar(object? sender, RoutedEventArgs e) =>
        await Vm.CopiarAsync(mover: false, espejo: false);

    private async void OnVerificar(object? sender, RoutedEventArgs e) =>
        await Vm.VerificarAsync();

    private async void OnEspejo(object? sender, RoutedEventArgs e) =>
        await Vm.CopiarAsync(mover: false, espejo: true);

    private async void OnReintentar(object? sender, RoutedEventArgs e) =>
        await Vm.ReintentarFallidosAsync();

    private void OnPausa(object? sender, RoutedEventArgs e) =>
        Vm.StatusLine = "pausa: guardar y retomar la copia se implementa en F5";

    private void OnParar(object? sender, RoutedEventArgs e)
    {
        Vm.Parar();
        Vm.StatusLine = Vm.EnMarcha ? "parando la copia..." : "no hay ninguna copia en marcha";
    }

    // ---------- atajos ----------

    private void OnAtajo(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers == KeyModifiers.Control;

        // Pestana Sincronizador (§2.2): las teclas del M2.
        // Ctrl+A alinear · Ctrl+S revisar · Ctrl+U/E/T desalinear · Entrar aplicar.
        if (Pestanas.SelectedIndex == 1 && Vm.Ajustes.TeclasM2)
        {
            switch (e.Key)
            {
                case Key.A:
                case Key.S:
                    if (ctrl) { Vm.Sync.Revisar(); e.Handled = true; return; }
                    break;
                case Key.U:
                case Key.E:
                case Key.T:
                    if (ctrl) { Vm.Sync.DesalinearTodo(); e.Handled = true; return; }
                    break;
                case Key.Enter:
                    _ = Vm.Sync.AplicarAsync();
                    e.Handled = true;
                    return;
            }
        }

        if (ctrl && e.Key == Key.R) { Refrescar(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.A) { OnTodo(this, new RoutedEventArgs()); e.Handled = true; }
    }
}