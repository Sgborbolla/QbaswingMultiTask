using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using QbaswingMultiTask.Report;
using QbaswingMultiTask.Stats;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>Una operacion en la tabla de §2.4.</summary>
public sealed class StatFila
{
    public string Cuando { get; init; } = "";
    public string Operacion { get; init; } = "";
    public string Origen { get; init; } = "";
    public string Dispositivo { get; init; } = "";
    public string Ficheros { get; init; } = "";
    public string Tam { get; init; } = "";
    public string VelMedia { get; init; } = "";
    public string VelReal { get; init; } = "";
    public string Errores { get; init; } = "";
}

/// <summary>Una barra de la grafica vectorial del historico (nada de imagenes).</summary>
public sealed class BarraDia
{
    public string Etiqueta { get; init; } = "";
    public string Valor { get; init; } = "";
    public double Altura { get; init; }
}

/// <summary>
/// Pestana Estadisticas (PLAN.md §2.4). Los datos son de verdad: salen del motor
/// (ficheros, bytes, segundos, velocidad media y real, errores). Antes eran
/// falsos ("files:0, avg=real, wait=0"); aqui se graban al terminar cada copia y
/// se pueden filtrar por periodo y dispositivo, exportar a CSV y a PDF.
/// </summary>
public sealed class EstadisticasViewModel : Vm
{
    public const string Todos = "todos";

    public EstadisticasViewModel()
    {
        Filas = new ObservableCollection<StatFila>();
        Grafica = new ObservableCollection<BarraDia>();
        Dispositivos = new ObservableCollection<string> { Todos };

        RefrescarCommand = new RelayCommand(Refrescar);
        CsvCommand = new RelayCommand(ExportarCsv);
        PdfCommand = new RelayCommand(GuardarPdf);
        LimpiarCommand = new RelayCommand(Limpiar);

        Refrescar();
    }

    public ObservableCollection<StatFila> Filas { get; }
    public ObservableCollection<BarraDia> Grafica { get; }
    public ObservableCollection<string> Dispositivos { get; }

    public RelayCommand RefrescarCommand { get; }
    public RelayCommand CsvCommand { get; }
    public RelayCommand PdfCommand { get; }
    public RelayCommand LimpiarCommand { get; }

    private DateTime? _desde;
    public DateTime? Desde { get => _desde; set { if (Set(ref _desde, value)) Refrescar(); } }

    private DateTime? _hasta;
    public DateTime? Hasta { get => _hasta; set { if (Set(ref _hasta, value)) Refrescar(); } }

    private string _dispositivo = Todos;
    public string DispositivoSeleccionado
    {
        get => _dispositivo;
        set { if (Set(ref _dispositivo, value ?? Todos)) Refrescar(); }
    }

    // --- tarjetas ---
    private long _operaciones;
    public long Operaciones { get => _operaciones; set { if (Set(ref _operaciones, value)) Raise(nameof(OperacionesTexto)); } }
    public string OperacionesTexto => Human.Numero(Operaciones);

    private long _ficheros;
    public long Ficheros { get => _ficheros; set { if (Set(ref _ficheros, value)) Raise(nameof(FicherosTexto)); } }
    public string FicherosTexto => Human.Numero(Ficheros);

    private long _bytes;
    public long Bytes { get => _bytes; set { if (Set(ref _bytes, value)) Raise(nameof(BytesTexto)); } }
    public string BytesTexto => Human.Size(Bytes);

    private double _velReal;
    public double VelReal { get => _velReal; set { if (Set(ref _velReal, value)) Raise(nameof(VelRealTexto)); } }
    public string VelRealTexto => Human.Size((long)VelReal) + "/s";

    private double _velMedia;
    public double VelMedia { get => _velMedia; set { if (Set(ref _velMedia, value)) Raise(nameof(VelMediaTexto)); } }
    public string VelMediaTexto => Human.Size((long)VelMedia) + "/s";

    private long _errores;
    public long Errores { get => _errores; set { if (Set(ref _errores, value)) Raise(nameof(ErroresTexto)); } }
    public string ErroresTexto => Human.Numero(Errores);

    private string _periodo = "";
    public string PeriodoTexto { get => _periodo; set => Set(ref _periodo, value); }

    private string _aviso = "todavia no hay operaciones registradas";
    public string Aviso { get => _aviso; set => Set(ref _aviso, value); }

    /// <summary>Relee las estadisticas del disco y recalcula las tarjetas y la grafica.</summary>
    public void Refrescar()
    {
        var desde = Desde;
        var hasta = Hasta is { } h ? h.Date.AddDays(1).AddSeconds(-1) : (DateTime?)null;
        var disp = DispositivoSeleccionado == Todos ? null : DispositivoSeleccionado;

        var res = StatsStore.Resumir(desde, hasta, disp);
        Operaciones = res.Operaciones;
        Ficheros = res.Ficheros;
        Bytes = res.Bytes;
        VelReal = res.VelReal;
        VelMedia = res.VelMedia;
        Errores = res.Errores;

        PeriodoTexto = $"{(desde?.ToString("dd/MM/yyyy") ?? "inicio")} → " +
                       $"{(hasta?.ToString("dd/MM/yyyy") ?? "hoy")} · {disp ?? "todos los dispositivos"}";

        var registros = StatsStore.Leer(desde, hasta, disp)
            .OrderByDescending(r => r.Cuando).Take(500).ToList();

        Filas.Clear();
        foreach (var r in registros)
            Filas.Add(new StatFila
            {
                Cuando = r.Cuando.ToString("dd/MM HH:mm"),
                Operacion = r.Operacion,
                Origen = r.Origen,
                Dispositivo = r.Dispositivo,
                Ficheros = Human.Numero(r.Ficheros),
                Tam = Human.Size(r.Bytes),
                VelMedia = Human.Size((long)r.VelMedia) + "/s",
                VelReal = Human.Size((long)r.VelReal) + "/s",
                Errores = Human.Numero(r.Errores)
            });

        // Grafica vectorial: una barra por dia, alto proporcional a los bytes.
        var porDia = StatsStore.PorDia(desde, hasta, disp).TakeLast(21).ToList();
        var maxAncho = 1.5 * 1024 * 1024 * 1024;              // minimo para que no salga plano
        var max = Math.Max(maxAncho, porDia.Count == 0 ? 0 : porDia.Max(p => p.Bytes));
        Grafica.Clear();
        foreach (var p in porDia)
            Grafica.Add(new BarraDia
            {
                Etiqueta = p.Dia.ToString("dd/MM"),
                Valor = Human.Size(p.Bytes),
                Altura = Math.Max(2, p.Bytes * 120.0 / max)
            });

        // Rellena la lista de dispositivos vistos, para poder filtrar.
        foreach (var d in StatsStore.Leer().Select(r => r.Dispositivo)
                     .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
            if (!Dispositivos.Contains(d)) Dispositivos.Add(d);

        Aviso = registros.Count == 0
            ? "todavia no hay operaciones en este periodo"
            : $"{Human.Numero(registros.Count)} operacion(es) en el periodo";
    }

    private void ExportarCsv()
    {
        try
        {
            var csv = StatsStore.ExportarCsv(Desde,
                Hasta is { } h ? h.Date.AddDays(1).AddSeconds(-1) : null,
                DispositivoSeleccionado == Todos ? null : DispositivoSeleccionado);
            var ruta = Path.Combine(AppPaths.StatsDir, $"estadisticas-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            File.WriteAllText(ruta, csv, System.Text.Encoding.UTF8);
            Aviso = "CSV guardado en " + ruta;
        }
        catch (Exception ex) { Aviso = "no se pudo exportar: " + ex.Message; }
    }

    private void GuardarPdf()
    {
        try
        {
            var lineas = new List<string>
            {
                "Resumen por periodo",
                PeriodoTexto,
                "",
                $"operaciones: {OperacionesTexto}",
                $"ficheros:    {FicherosTexto}",
                $"bytes:       {BytesTexto}",
                $"vel. media:  {VelMediaTexto}",
                $"vel. real:   {VelRealTexto}",
                $"errores:     {ErroresTexto}",
                "",
                "Ultimas operaciones"
            };
            lineas.AddRange(Filas.Take(200).Select(f =>
                $"{f.Cuando}  {f.Operacion}  {f.Dispositivo}  {f.Ficheros} fich  {f.Tam}  real {f.VelReal}"));

            var ruta = Path.Combine(AppPaths.StatsDir, $"estadisticas-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
            SimplePdf.Guardar(ruta, "QbaswingMultiTask · estadisticas", lineas);
            Aviso = "PDF guardado en " + ruta;
        }
        catch (Exception ex) { Aviso = "no se pudo exportar el PDF: " + ex.Message; }
    }

    private void Limpiar()
    {
        try
        {
            var ruta = Path.Combine(AppPaths.StatsDir, "estadisticas.jsonl");
            if (File.Exists(ruta)) File.Delete(ruta);
            Refrescar();
            Aviso = "historico borrado";
        }
        catch (Exception ex) { Aviso = "no se pudo borrar el historico: " + ex.Message; }
    }
}
