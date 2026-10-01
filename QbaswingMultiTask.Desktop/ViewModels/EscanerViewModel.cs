using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Avalonia.Threading;
using QbaswingMultiTask.Devices;
using QbaswingMultiTask.Index;
using QbaswingMultiTask.Report;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>Una unidad con su casilla para elegir que se indexa (§2.3).</summary>
public sealed class DiscoMarca : Vm
{
    public DiscoMarca(DeviceInfo info) { Info = info; Marcar = info.IsReady && !info.IsSystem; }
    public DeviceInfo Info { get; }
    public string Letra => Info.DriveLetter ?? "·";
    public string Texto => Info.Display;

    private bool _marcar;
    public bool Marcar { get => _marcar; set => Set(ref _marcar, value); }
}

/// <summary>Un grupo de duplicados para la tabla (§2.3).</summary>
public sealed class DuplicadoFila
{
    public string Nombre { get; init; } = "";
    public int Copias { get; init; }
    public string TamTexto { get; init; } = "";
    public string DesperdicioTexto { get; init; } = "";
    public string Ubicaciones { get; init; } = "";
}

/// <summary>Un fichero que esta en A y no en B (§2.3).</summary>
public sealed class FaltanteFila
{
    public string Relativo { get; init; } = "";
    public string TamTexto { get; init; } = "";
    public string Parecido { get; init; } = "";
}

/// <summary>Un resultado de busqueda (§2.3).</summary>
public sealed class ResultadoFila
{
    public string Nombre { get; init; } = "";
    public string Carpeta { get; init; } = "";
    public string TamTexto { get; init; } = "";
}

/// <summary>
/// Pestana Escaner (PLAN.md §2.3): indice real del disco, duplicados por
/// contenido (hash), ficheros que faltan entre dos carpetas, busqueda por
/// comodin y exportacion. Se apoya en IndexStore (JSONL, ver PROGRESO §desvios).
/// </summary>
public sealed class EscanerViewModel : Vm
{
    private IndexStore _index = new();
    private CancellationTokenSource? _cts;

    public EscanerViewModel()
    {
        Discos = new ObservableCollection<DiscoMarca>();
        Duplicados = new ObservableCollection<DuplicadoFila>();
        Faltantes = new ObservableCollection<FaltanteFila>();
        Resultados = new ObservableCollection<ResultadoFila>();

        RefrescarDiscosCommand = new RelayCommand(CargarDiscos);
        MarcarTodoCommand = new RelayCommand(MarcarTodo);
        EscanearCommand = new RelayCommand(() => _ = EscanearAsync(), () => !Ocupado);
        DuplicadosCommand = new RelayCommand(() => _ = DuplicadosAsync(), () => !Ocupado);
        FaltantesCommand = new RelayCommand(BuscarFaltantes, () => !Ocupado);
        BuscarCommand = new RelayCommand(Buscar, () => !Ocupado);
        CsvCommand = new RelayCommand(GuardarCsv, () => Resultados.Count > 0 || Duplicados.Count > 0);
        PdfCommand = new RelayCommand(GuardarPdf, () => Resultados.Count > 0 || Duplicados.Count > 0);
        LimpiarCommand = new RelayCommand(Limpiar);

        CargarDiscos();
        try { _index.Cargar(); } catch { }
        Estado = _index.Cuantas > 0
            ? $"indice guardado: {Human.Numero(_index.Cuantas)} fichero(s)"
            : "sin indice todavia: marca unidades y pulsa Escanear";
    }

    public ObservableCollection<DiscoMarca> Discos { get; }
    public ObservableCollection<DuplicadoFila> Duplicados { get; }
    public ObservableCollection<FaltanteFila> Faltantes { get; }
    public ObservableCollection<ResultadoFila> Resultados { get; }

    public RelayCommand RefrescarDiscosCommand { get; }
    public RelayCommand MarcarTodoCommand { get; }
    public RelayCommand EscanearCommand { get; }
    public RelayCommand DuplicadosCommand { get; }
    public RelayCommand FaltantesCommand { get; }
    public RelayCommand BuscarCommand { get; }
    public RelayCommand CsvCommand { get; }
    public RelayCommand PdfCommand { get; }
    public RelayCommand LimpiarCommand { get; }

    private double _progreso;
    public double Progreso { get => _progreso; set => Set(ref _progreso, value); }

    private string _estado = "Listo.";
    public string Estado { get => _estado; set => Set(ref _estado, value); }

    private string _actual = "";
    public string Actual { get => _actual; set => Set(ref _actual, value); }

    private int _indiceFicheros;
    public int IndiceFicheros
    {
        get => _indiceFicheros;
        set { if (Set(ref _indiceFicheros, value)) Raise(nameof(IndiceTexto)); }
    }
    public string IndiceTexto => $"{Human.Numero(IndiceFicheros)} ficheros en el indice";

    private bool _ocupado;
    public bool Ocupado
    {
        get => _ocupado;
        private set
        {
            if (!Set(ref _ocupado, value)) return;
            EscanearCommand.Avisar();
            DuplicadosCommand.Avisar();
            FaltantesCommand.Avisar();
            BuscarCommand.Avisar();
        }
    }

    private string _busqueda = "";
    public string Busqueda { get => _busqueda; set => Set(ref _busqueda, value); }

    private string _carpetaA = "";
    public string CarpetaA { get => _carpetaA; set => Set(ref _carpetaA, value); }

    private string _carpetaB = "";
    public string CarpetaB { get => _carpetaB; set => Set(ref _carpetaB, value); }

    private string _resumen = "";
    public string Resumen { get => _resumen; set => Set(ref _resumen, value); }

    private void CargarDiscos()
    {
        Discos.Clear();
        foreach (var d in new DeviceManager().Scan()) Discos.Add(new DiscoMarca(d));
        Estado = $"{Discos.Count} dispositivo(s)";
    }

    private void MarcarTodo()
    {
        var todos = Discos.All(d => d.Marcar);
        foreach (var d in Discos) d.Marcar = !todos && d.Info.IsReady;
        Estado = todos ? "ninguna unidad marcada" : "todas las unidades marcadas";
    }

    private async System.Threading.Tasks.Task EscanearAsync()
    {
        var raices = Discos.Where(d => d.Marcar && d.Info.IsReady).Select(d => d.Info.Root).ToList();
        if (raices.Count == 0) { Estado = "marca al menos una unidad"; return; }

        Ocupado = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var progreso = new Progress<double>(p => Progreso = p);
        var actual = new Progress<string>(n => Actual = n);
        try
        {
            Estado = "recorriendo e indexando...";
            var total = await System.Threading.Tasks.Task.Run(() =>
            {
                var idx = new IndexStore();
                idx.Cargar();
                var n = idx.Indexar(raices, progreso, actual, token);
                idx.Guardar();
                _index = idx;
                return n;
            }, token);

            IndiceFicheros = total;
            Estado = $"indice al dia: {Human.Numero(total)} fichero(s)";
            Actual = "";
        }
        catch (OperationCanceledException) { Estado = "escaneo cancelado"; }
        catch (Exception ex) { Estado = "el escaneo fallo: " + ex.Message; }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            Ocupado = false;
        }
    }

    private async System.Threading.Tasks.Task DuplicadosAsync()
    {
        if (_index.Cuantas == 0) { Estado = "primero hay que indexar"; return; }
        Ocupado = true;
        try
        {
            Estado = "buscando duplicados por contenido...";
            var progreso = new Progress<double>(p => Progreso = p);
            var grupos = await System.Threading.Tasks.Task.Run(() => _index.Duplicados(progreso));

            Duplicados.Clear();
            Faltantes.Clear();
            Resultados.Clear();
            foreach (var g in grupos)
                Duplicados.Add(new DuplicadoFila
                {
                    Nombre = g.Nombre,
                    Copias = g.Ubicaciones.Count,
                    TamTexto = Human.Size(g.Length),
                    DesperdicioTexto = Human.Size(g.Desperdicio),
                    Ubicaciones = string.Join("  ·  ", g.Ubicaciones)
                });

            var desperdicio = grupos.Sum(g => g.Desperdicio);
            Resumen = grupos.Count == 0
                ? "no hay duplicados"
                : $"{Human.Numero(grupos.Count)} grupo(s) · se recuperarian {Human.Size(desperdicio)}";
            Estado = Resumen;
        }
        catch (Exception ex) { Estado = "no se pudieron buscar duplicados: " + ex.Message; }
        finally { Ocupado = false; }
    }

    private void BuscarFaltantes()
    {
        if (!Directory.Exists(CarpetaA) || !Directory.Exists(CarpetaB))
        {
            Estado = "indica dos carpetas que existan (A y B)";
            return;
        }

        try
        {
            Duplicados.Clear();
            Resultados.Clear();
            Faltantes.Clear();
            var faltan = _index.Faltantes(CarpetaA, CarpetaB);
            foreach (var m in faltan.Where(x => !x.EsCarpeta).Take(2000))
                Faltantes.Add(new FaltanteFila
                {
                    Relativo = m.Relativo,
                    TamTexto = Human.Size(m.Length),
                    Parecido = m.Parecido is { Length: > 0 } p ? "se parece a " + Path.GetFileName(p) : ""
                });

            var bytes = faltan.Sum(m => m.Length);
            Resumen = $"{Human.Numero(faltan.Count(x => !x.EsCarpeta))} fichero(s) faltan · {Human.Size(bytes)}";
            Estado = Resumen;
        }
        catch (Exception ex) { Estado = "no se pudo comparar: " + ex.Message; }
    }

    private void Buscar()
    {
        if (_index.Cuantas == 0) { Estado = "primero hay que indexar"; return; }
        try
        {
            Duplicados.Clear();
            Faltantes.Clear();
            Resultados.Clear();
            var hallados = _index.Buscar(Busqueda);
            foreach (var e in hallados)
                Resultados.Add(new ResultadoFila
                {
                    Nombre = Path.GetFileName(e.Path),
                    Carpeta = Path.GetDirectoryName(e.Path) ?? "",
                    TamTexto = Human.Size(e.Length)
                });

            Resumen = $"{Human.Numero(hallados.Count)} resultado(s) para \"{Busqueda}\"";
            Estado = Resumen;
        }
        catch (Exception ex) { Estado = "la busqueda fallo: " + ex.Message; }
    }

    private void Limpiar()
    {
        Duplicados.Clear();
        Faltantes.Clear();
        Resultados.Clear();
        Resumen = "";
        Estado = "listo";
    }

    private string RutaInforme(string ext) => Path.Combine(
        AppPaths.DataDir, $"escaner-{DateTime.Now:yyyyMMdd-HHmmss}.{ext}");

    private void GuardarCsv()
    {
        try
        {
            var sb = new StringBuilder();
            if (Duplicados.Count > 0)
            {
                sb.AppendLine("grupo;copias;tamano;desperdicio;ubicaciones");
                foreach (var d in Duplicados)
                    sb.Append(Csv(d.Nombre)).Append(';').Append(d.Copias).Append(';')
                      .Append(d.TamTexto).Append(';').Append(d.DesperdicioTexto).Append(';')
                      .Append(Csv(d.Ubicaciones)).AppendLine();
            }
            if (Faltantes.Count > 0)
            {
                sb.AppendLine("falta;tamano;parecido");
                foreach (var f in Faltantes)
                    sb.Append(Csv(f.Relativo)).Append(';').Append(f.TamTexto).Append(';')
                      .Append(Csv(f.Parecido)).AppendLine();
            }
            if (Resultados.Count > 0)
            {
                sb.AppendLine("fichero;carpeta;tamano");
                foreach (var r in Resultados)
                    sb.Append(Csv(r.Nombre)).Append(';').Append(Csv(r.Carpeta)).Append(';')
                      .Append(r.TamTexto).AppendLine();
            }

            var ruta = RutaInforme("csv");
            File.WriteAllText(ruta, sb.ToString(), Encoding.UTF8);
            Estado = "CSV guardado en " + ruta;
        }
        catch (Exception ex) { Estado = "no se pudo guardar el CSV: " + ex.Message; }
    }

    private void GuardarPdf()
    {
        try
        {
            var lineas = new List<string> { $"Informe del escaner · {DateTime.Now:dd/MM/yyyy HH:mm}", "" };
            if (Duplicados.Count > 0)
            {
                lineas.Add("DUPLICADOS");
                foreach (var d in Duplicados)
                {
                    lineas.Add($"{d.Nombre}  ({d.Copias} copias, {d.TamTexto}, recupera {d.DesperdicioTexto})");
                    lineas.Add("   " + d.Ubicaciones);
                }
                lineas.Add("");
            }
            if (Faltantes.Count > 0)
            {
                lineas.Add("FALTAN EN B");
                lineas.AddRange(Faltantes.Take(500).Select(f => $"{f.Relativo}  {f.TamTexto}  {f.Parecido}"));
                lineas.Add("");
            }
            if (Resultados.Count > 0)
            {
                lineas.Add("BUSQUEDA");
                lineas.AddRange(Resultados.Take(500).Select(r => $"{r.Nombre}  {r.TamTexto}  {r.Carpeta}"));
            }

            var ruta = RutaInforme("pdf");
            SimplePdf.Guardar(ruta, "QbaswingMultiTask · informe del escaner", lineas);
            Estado = "PDF guardado en " + ruta;
        }
        catch (Exception ex) { Estado = "no se pudo guardar el PDF: " + ex.Message; }
    }

    private static string Csv(string t)
    {
        t ??= "";
        return t.Contains(';') || t.Contains('"') ? '"' + t.Replace("\"", "\"\"") + '"' : t;
    }
}
