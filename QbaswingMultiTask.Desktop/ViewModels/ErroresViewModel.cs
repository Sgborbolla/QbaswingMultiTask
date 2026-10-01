using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using QbaswingMultiTask.Engine;
using QbaswingMultiTask.Report;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>Una fila de la pestaña Errores (PLAN.md §2.5).</summary>
public sealed class ErrorFila
{
    public string Hora { get; init; } = "";
    public string Fichero { get; init; } = "";
    public string Origen { get; init; } = "";
    public string Destino { get; init; } = "";
    public string Motivo { get; init; } = "";
    public bool Reintentable { get; init; }
    public string DestinoTexto => Destino.Length > 0 ? Destino : "—";
}

/// <summary>
/// Pestana Errores (PLAN.md §2.5): el centro de errores. Lista lo que fallo con
/// su causa, deja reintentar solo lo fallido, descartar, y guardar el registro
/// en CSV y PDF. Los errores se acumulan durante la sesion para no perder el
/// historial cuando se lanza otra copia.
/// </summary>
public sealed class ErroresViewModel : Vm
{
    private readonly MainViewModel _main;
    private readonly HashSet<string> _vistos = new(StringComparer.Ordinal);

    public ErroresViewModel(MainViewModel main)
    {
        _main = main;
        ReintentarCommand = new RelayCommand(() => _ = ReintentarAsync(), () => PuedeReintentar);
        DescartarCommand = new RelayCommand(Descartar, () => Filas.Count > 0);
        CsvCommand = new RelayCommand(GuardarCsv, () => Filas.Count > 0);
        PdfCommand = new RelayCommand(GuardarPdf, () => Filas.Count > 0);
        LimpiarCommand = new RelayCommand(Limpiar, () => Filas.Count > 0);
    }

    public ObservableCollection<ErrorFila> Filas { get; } = new();

    public RelayCommand ReintentarCommand { get; }
    public RelayCommand DescartarCommand { get; }
    public RelayCommand CsvCommand { get; }
    public RelayCommand PdfCommand { get; }
    public RelayCommand LimpiarCommand { get; }

    /// <summary>Reevalua si los botones deben estar activos.</summary>
    private void Notificar()
    {
        ReintentarCommand.Avisar();
        DescartarCommand.Avisar();
        CsvCommand.Avisar();
        PdfCommand.Avisar();
        LimpiarCommand.Avisar();
    }

    public bool HayErrores => Filas.Count > 0;

    private bool _reintentando;
    public bool Reintentando
    {
        get => _reintentando;
        private set { if (Set(ref _reintentando, value)) { Raise(nameof(PuedeReintentar)); Notificar(); } }
    }

    /// <summary>Se puede reintentar si hay algo fallido y no hay otra copia en marcha.</summary>
    public bool PuedeReintentar => Filas.Count > 0 && !Reintentando && !_main.EnMarcha;

    public string Resumen => Filas.Count == 0
        ? "sin errores: todo lo ultimo se copio bien"
        : $"{Filas.Count} problema(s) · reintentables {Filas.Count(f => f.Reintentable)}";

    private string _aviso = "";
    public string Aviso { get => _aviso; set => Set(ref _aviso, value); }

    /// <summary>Vuelca los errores del ultimo resultado, sin repetir los ya listados.</summary>
    public void Refrescar()
    {
        var res = _main.UltimoResultado;
        if (res is not null)
        {
            foreach (var e in res.Errors)
            {
                var clave = $"{e.Hora:O}|{e.Fichero}|{e.Destino}|{e.Motivo}";
                if (!_vistos.Add(clave)) continue;
                Filas.Add(new ErrorFila
                {
                    Hora = e.Hora.ToString("HH:mm:ss"),
                    Fichero = e.Fichero,
                    Origen = e.Origen,
                    Destino = e.Destino,
                    Motivo = e.Motivo,
                    Reintentable = e.Reintentable
                });
            }
        }

        Raise(nameof(HayErrores));
        Raise(nameof(Resumen));
        Raise(nameof(PuedeReintentar));
        Notificar();
    }

    private async System.Threading.Tasks.Task ReintentarAsync()
    {
        if (Reintentando) return;
        Reintentando = true;
        Aviso = "reintentando lo que fallo...";
        try
        {
            await _main.ReintentarFallidosAsync();
            Refrescar();
            Aviso = Filas.Count == 0
                ? "reintento correcto: ya no queda nada pendiente"
                : $"siguen pendientes {Filas.Count} fichero(s)";
        }
        catch (Exception ex)
        {
            Aviso = "el reintento fallo: " + CopyEngine.Motivo(ex);
        }
        finally
        {
            Reintentando = false;
        }
    }

    private void Descartar()
    {
        Filas.Clear();
        Raise(nameof(HayErrores));
        Raise(nameof(Resumen));
        Raise(nameof(PuedeReintentar));
        Notificar();
        Aviso = "lista de errores descartada (los ficheros no se tocan)";
    }

    private void Limpiar()
    {
        _vistos.Clear();
        Descartar();
    }

    private string RutaRegistro(string ext) => Path.Combine(
        AppPaths.LogsDir, $"errores-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");

    private void GuardarCsv()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("hora;fichero;origen;destino;motivo;reintentable");
            foreach (var f in Filas)
                sb.Append(f.Hora).Append(';')
                  .Append(Csv(f.Fichero)).Append(';')
                  .Append(Csv(f.Origen)).Append(';')
                  .Append(Csv(f.Destino)).Append(';')
                  .Append(Csv(f.Motivo)).Append(';')
                  .Append(f.Reintentable ? "si" : "no").AppendLine();

            var ruta = RutaRegistro("csv");
            File.WriteAllText(ruta, sb.ToString(), Encoding.UTF8);
            Aviso = "registro guardado en " + ruta;
        }
        catch (Exception ex) { Aviso = "no se pudo guardar el CSV: " + ex.Message; }
    }

    private void GuardarPdf()
    {
        try
        {
            var lineas = new List<string>
            {
                $"Errores al {DateTime.Now:dd/MM/yyyy HH:mm}",
                $"Total: {Filas.Count} · reintentables: {Filas.Count(f => f.Reintentable)}",
                ""
            };
            foreach (var f in Filas)
            {
                lineas.Add($"{f.Hora}  {f.Fichero}");
                lineas.Add($"     destino: {f.DestinoTexto}");
                lineas.Add($"     motivo: {f.Motivo}");
            }

            var ruta = RutaRegistro("pdf");
            SimplePdf.Guardar(ruta, "QbaswingMultiTask · registro de errores", lineas);
            Aviso = "PDF guardado en " + ruta;
        }
        catch (Exception ex) { Aviso = "no se pudo guardar el PDF: " + ex.Message; }
    }

    private static string Csv(string t)
    {
        t ??= "";
        return t.Contains(';') || t.Contains('"') ? '"' + t.Replace("\"", "\"\"") + '"' : t;
    }
}
