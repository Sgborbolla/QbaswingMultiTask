using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using QbaswingMultiTask.Devices;
using QbaswingMultiTask.Engine;
using QbaswingMultiTask.Profiles;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>Modo de estructura de destino. PLAN.md 2.1: ●HDD ○FLASH.</summary>
public enum ModoEstructura
{
    /// <summary>Automatico: se decide por el tipo de unidad (§3, F2).</summary>
    Auto,
    /// <summary>Disco duro: pocas carpetas, anchas.</summary>
    Hdd,
    /// <summary>Memoria: carpetas separadas por tipo.</summary>
    Flash
}

/// <summary>Estado de la ventana principal (pestaña Copiar/Mover).</summary>
public sealed class MainViewModel : Vm
{
    public MainViewModel()
    {
        Devices = new ObservableCollection<DeviceViewModel>();
        Explorador = new FolderNode("Este equipo", "", raiz: true);
        Explorador.Cargar();
        RefrescarCommand = new RelayCommand(() => RefrescarDispositivos());
        ReloadDevices();

        // Las seis pestanas restantes (§2), cada una con su propio view model.
        Sync = new SincronizadorViewModel();
        Escaner = new EscanerViewModel();
        Stats = new EstadisticasViewModel();
        Errores = new ErroresViewModel(this);
        Perfiles = new PerfilesViewModel(this);
        Ajustes = new AjustesViewModel();
    }

    // ---------- pestanas (§2) ----------
    public SincronizadorViewModel Sync { get; }
    public EscanerViewModel Escaner { get; }
    public EstadisticasViewModel Stats { get; }
    public ErroresViewModel Errores { get; }
    public PerfilesViewModel Perfiles { get; }
    public AjustesViewModel Ajustes { get; }

    // ---------- coleccion de rectangulos ----------

    public ObservableCollection<DeviceViewModel> Devices { get; }
    public RelayCommand RefrescarCommand { get; }

    /// <summary>Arbol de la izquierda: raiz "Este equipo" con los discos dentro (§2.1).</summary>
    public FolderNode Explorador { get; }

    // ---------- origen ----------

    private FolderNode? _origen;
    /// <summary>Carpeta de origen elegida en el explorador. Sin ella no hay copia.</summary>
    public FolderNode? Origen
    {
        get => _origen;
        set
        {
            if (!Set(ref _origen, value)) return;
            Raise(nameof(OrigenTexto));
            Raise(nameof(HayOrigen));
            Raise(nameof(ResumenCopiar));
        }
    }

    public string OrigenTexto => Origen?.Path.Length > 0 ? Origen.Path : "(elige una carpeta en la izquierda)";

    public bool HayOrigen => Origen is { Path.Length: > 0 };

    // ---------- estructura y fechas (§2.1) ----------

    private ModoEstructura _estructura = ModoEstructura.Auto;

    /// <summary>Modo de estructura. "auto" decide por el tipo de unidad (§3).</summary>
    public ModoEstructura Estructura
    {
        get => _estructura;
        set
        {
            if (!Set(ref _estructura, value)) return;
            Raise(nameof(EsAuto)); Raise(nameof(EsHdd)); Raise(nameof(EsFlash));
            Raise(nameof(EstructuraTexto));
        }
    }

    // Los tres botones redondos de §2.1. Cada uno escribe el modo y avisa a los
    // otros dos, para que solo uno quede marcado.
    public bool EsAuto { get => Estructura == ModoEstructura.Auto; set { if (value) Estructura = ModoEstructura.Auto; } }
    public bool EsHdd { get => Estructura == ModoEstructura.Hdd; set { if (value) Estructura = ModoEstructura.Hdd; } }
    public bool EsFlash { get => Estructura == ModoEstructura.Flash; set { if (value) Estructura = ModoEstructura.Flash; } }

    public string EstructuraTexto => Estructura switch
    {
        ModoEstructura.Hdd => "HDD: pocas carpetas, anchas",
        ModoEstructura.Flash => "FLASH: carpetas por tipo",
        _ => "auto: segun el tipo de unidad"
    };

    private DateTime? _fechaDesde;
    public DateTime? FechaDesde
    {
        get => _fechaDesde;
        set { if (Set(ref _fechaDesde, value)) Raise(nameof(TextoFechas)); }
    }

    private DateTime? _fechaHasta;
    public DateTime? FechaHasta
    {
        get => _fechaHasta;
        set { if (Set(ref _fechaHasta, value)) Raise(nameof(TextoFechas)); }
    }

    public string TextoFechas => $"{FechaDesde?.ToString("dd/MM/yyyy") ?? "__"} → {FechaHasta?.ToString("dd/MM/yyyy") ?? "__"}";

    // ---------- seleccion de destinos ----------

    public DeviceViewModel? Destacado { get; set; }

    public int Marcados => Devices.Count(d => d.Marked);

    /// <summary>"E: PENDRA · F: PAQUE", como en la franja de abajo de §2.1.</summary>
    public string DestinosText
    {
        get
        {
            var m = Devices.Where(d => d.Marked).ToList();
            return m.Count == 0 ? "ninguno" : string.Join("  ·  ", m.Select(d => d.Label));
        }
    }

    public void Marcar(DeviceViewModel d, bool marcado)
    {
        d.Marked = marcado;
        Raise(nameof(Marcados));
        Raise(nameof(DestinosText));
        Raise(nameof(ResumenCopiar));
    }

    public void MarcarTodos(bool marcado)
    {
        foreach (var d in Devices) d.Marked = marcado;
        Raise(nameof(Marcados));
        Raise(nameof(DestinosText));
        Raise(nameof(ResumenCopiar));
    }

    /// <summary>Marca solo las unidades externas: es lo habitual al copiar un paquete.</summary>
    public void MarcarExternos()
    {
        foreach (var d in Devices) d.Marked = d.Info.IsExternal;
        Raise(nameof(Marcados));
        Raise(nameof(DestinosText));
        Raise(nameof(ResumenCopiar));
    }

    // ---------- barra general ----------

    private double _generalPercent;
    /// <summary>Barra general: la suma de todas las marcadas (§2.1).</summary>
    public double GeneralPercent
    {
        get => _generalPercent;
        set { if (Set(ref _generalPercent, Math.Clamp(value, 0, 100))) Raise(nameof(GeneralText)); }
    }

    private long _ficheros;
    public long Ficheros
    {
        get => _ficheros;
        set { if (Set(ref _ficheros, Math.Max(0, value))) Raise(nameof(GeneralText)); }
    }

    private long _bytes;
    public long Bytes
    {
        get => _bytes;
        set { if (Set(ref _bytes, Math.Max(0, value))) Raise(nameof(GeneralText)); }
    }

    private double _restante;
    public double Restante
    {
        get => _restante;
        set => Set(ref _restante, Math.Max(0, value));
    }

    /// <summary>"38% · 3.212 ficheros · 12,4 GB · restante 00:02:10" (§2.1).</summary>
    public string GeneralText
    {
        get
        {
            var m = Devices.Where(d => d.Marked).ToList();
            if (m.Count == 0) return "marca los destinos y pulsa Copiar";
            return $"{GeneralPercent:0} %  ·  {Human.Numero(Ficheros)} ficheros  ·  " +
                   $"{Human.Size(Bytes)}  ·  restante {Human.Time(Restante)}";
        }
    }

    /// <summary>Lo que pone el botón Copiar: qué se va a hacer, o qué falta.</summary>
    public string ResumenCopiar
    {
        get
        {
            if (!HayOrigen) return "elige la carpeta de origen";
            if (Marcados == 0) return "marca al menos un destino";
            return $"{Marcados} destino{(Marcados == 1 ? "" : "s")} desde {EtiquetaDeOrigen()}";
        }
    }

    private string EtiquetaDeOrigen() => Origen is null
        ? "—"
        : Origen.Path.TrimEnd('\\').Split('\\').LastOrDefault() ?? Origen.Path;

    private string _statusLine = "Listo.";
    public string StatusLine
    {
        get => _statusLine;
        set => Set(ref _statusLine, value);
    }

    // ---------- lectura automatica de dispositivos ----------

    /// <summary>
    /// PLAN.md: "Refresco de dispositivos cada 1 s (no 1 ms: evita robar CPU al
    /// motor)". Añade lo que se acaba de enchufar y quita lo que se ha
    /// desconectado, sin perder las casillas marcadas ni las copias en curso:
    /// se empareja por Id, estable tambien en "\\?\Volume{...}".
    /// </summary>
    public void RefrescarDispositivos()
    {
        var vistos = new DeviceManager().Scan();
        var actuales = vistos.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = Devices.Count - 1; i >= 0; i--)
        {
            var vm = Devices[i];
            if (actuales.Contains(vm.Info.Id)) continue;
            if (vm.CopyActive) continue;                  // no se toca en caliente
            Devices.RemoveAt(i);
        }

        foreach (var info in vistos)
        {
            if (Devices.Any(d => string.Equals(d.Info.Id, info.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            var nuevo = new DeviceViewModel(info);
            var siguiente = Devices.FirstOrDefault(d => Orden(d.Info) > Orden(info));
            if (siguiente is null) Devices.Add(nuevo);
            else Devices.Insert(Devices.IndexOf(siguiente), nuevo);
        }

        foreach (var info in vistos)
        {
            var vm = Devices.FirstOrDefault(d =>
                string.Equals(d.Info.Id, info.Id, StringComparison.OrdinalIgnoreCase));
            vm?.Actualizar(info);
        }

        // El arbol de la izquierda tambien se queda al dia.
        Explorador.Cargar();
        foreach (var nodo in Explorador.Children)
        {
            if (!actuales.Contains(nodo.Path)) continue;
            nodo.IsExpanded = false;
            nodo.Cargar();
        }

        Raise(nameof(DestinosText));
        Raise(nameof(ResumenDispositivos));
    }

    private static int Orden(DeviceInfo d) =>
        d.IsUsb ? 0 : d.IsNetwork ? 1 : d.IsExternal ? 2 : 3;

    public string ResumenDispositivos =>
        Devices.Count == 0
            ? "sin dispositivos"
            : $"{Devices.Count} dispositivo{(Devices.Count == 1 ? "" : "s")}";

    public bool Copiando => Devices.Any(d => d.CopyActive);
    public bool PuedeRefrescar => !Copiando;

    private void ReloadDevices()
    {
        Devices.Clear();
        foreach (var d in new DeviceManager().Scan()) Devices.Add(new DeviceViewModel(d));
        Raise(nameof(DestinosText));
    }

    // ---------------- motor de copia (F1) ----------------

    private CancellationTokenSource? _cts;

    /// <summary>True mientras hay una copia en marcha.</summary>
    public bool EnMarcha { get; private set; }

    /// <summary>Ultimo resultado: alimenta el resumen de §2.8 y la pestaña Errores.</summary>
    public CopyResult? UltimoResultado { get; private set; }

    // Guardado de la ultima operacion, para poder reintentar solo lo que fallo (F5).
    private List<FileEntry> _ultimosFicheros = new();
    private List<Destino> _ultimosDestinos = new();
    private CopyOptions? _ultimasOpciones;

    /// <summary>Detiene la copia en curso (boton Parar).</summary>
    public void Parar() => _cts?.Cancel();

    /// <summary>
    /// Copia (o mueve) lo que hay en el origen a las unidades marcadas. Usa el
    /// motor de F1: lee una vez y escribe a N, verifica por hash, conserva fechas
    /// y atributos, y deja las barras con datos reales.
    /// </summary>
    public async Task CopiarAsync(bool mover, bool espejo)
    {
        if (EnMarcha) { StatusLine = "ya hay una copia en marcha"; return; }
        if (!HayOrigen) { StatusLine = "elige la carpeta de origen"; return; }

        var marcados = Devices.Where(d => d.Marked).ToList();
        if (marcados.Count == 0) { StatusLine = "marca al menos un destino"; return; }

        var opciones = new CopyOptions
        {
            Move = mover,
            Mirror = espejo,
            Verify = true,
            CopyTimestamps = true,
            CopyAttributes = true,
            ParallelFiles = Math.Max(1, Math.Min(marcados.Count, 4)),
            DateMode = FechaDesde is null && FechaHasta is null
                ? DateMode.Todas
                : FechaHasta is not null ? DateMode.Entre : DateMode.MasNuevoQue,
            DateFrom = FechaDesde,
            DateTo = FechaHasta
        };

        var motor = new CopyEngine(opciones);

        EnMarcha = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Raise(nameof(EnMarcha));
        Raise(nameof(PuedeRefrescar));

        try
        {
            StatusLine = "contando ficheros...";
            var raiz = Origen!.Path;
            var ficheros = await Task.Run(() => motor.Enumerar(raiz, token), token);

            if (ficheros.Count == 0)
            {
                StatusLine = "el origen no tiene ficheros que copiar";
                return;
            }

            var total = ficheros.Sum(f => f.Length);

            var destinos = marcados.Select(d => new Destino
            {
                Raiz = d.Info.Root,
                Etiqueta = d.Label,
                Info = d.Info,
                Estructura = Estructura switch
                {
                    ModoEstructura.Hdd => StructureMode.PorCarpetaOrigen,
                    ModoEstructura.Flash => StructureMode.PorTipo,
                    _ => EstructuraAuto.Para(d.Info)
                }
            }).ToList();

            // Se guarda el plan para poder reintentar solo lo que falle (F5).
            _ultimosFicheros = ficheros;
            _ultimosDestinos = destinos;
            _ultimasOpciones = opciones;

            foreach (var d in marcados) d.PrepararCopia(total);

            var reloj = Stopwatch.StartNew();
            var porRaiz = new Dictionary<string, DeviceViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in marcados) porRaiz[d.Info.Root] = d;

            var progreso = new Progress<CopyProgress>(p =>
            {
                GeneralPercent = p.Percent;
                Ficheros = p.FilesDone;
                Bytes = p.BytesDone;
                Restante = p.Restante(reloj.Elapsed.TotalSeconds);
                if (p.FicheroActual.Length > 0) StatusLine = "copiando " + p.FicheroActual;
            });

            var resultado = await motor.CopiarAsync(
                ficheros, destinos, progreso,
                avanceDestino: (raizDestino, bytes) => Dispatcher.UIThread.Post(() =>
                {
                    if (porRaiz.TryGetValue(raizDestino, out var vm)) vm.SumarBytes(bytes);
                }),
                ct: token);

            UltimoResultado = resultado;

            // Estadisticas reales y centro de errores (§2.4 y §2.5).
            global::QbaswingMultiTask.Stats.StatsStore.Anadir(resultado, raiz, mover ? "mover" : "copiar");
            Stats.Refrescar();
            Errores.Refrescar();

            var fallos = 0;
            foreach (var d in marcados)
            {
                var tieneError = resultado.Errors.Any(e =>
                    string.Equals(e.Destino, d.Info.Root, StringComparison.OrdinalIgnoreCase));
                d.HasError = tieneError;
                d.CopyActive = false;
                d.CopyPercent = tieneError ? d.CopyPercent : 100;
                if (tieneError) fallos++;
            }

            if (!resultado.Cancelado)
            {
                GeneralPercent = 100;
                Ficheros = resultado.FilesCopied;
                Bytes = resultado.BytesCopied;
                Restante = 0;
            }

            StatusLine = resultado.Cancelado
                ? $"copia cancelada: {Human.Numero(resultado.FilesCopied)} ficheros copiados"
                : resultado.Success
                    ? $"listo: {Human.Numero(resultado.FilesCopied)} ficheros · " +
                      $"{Human.Size(resultado.BytesCopied)} · {Human.Time(resultado.Duracion.TotalSeconds)}"
                    : $"termino con {resultado.Errors.Count} problema(s): {fallos} destino(s) con fallos";
        }
        catch (OperationCanceledException)
        {
            StatusLine = "copia cancelada";
        }
        catch (Exception ex)
        {
            StatusLine = "la copia se interrumpio: " + CopyEngine.Motivo(ex);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            EnMarcha = false;
            foreach (var d in Devices) d.CopyActive = false;
            Raise(nameof(EnMarcha));
            Raise(nameof(PuedeRefrescar));
            Raise(nameof(ResumenCopiar));
        }
    }

    /// <summary>
    /// Solo verificar: vuelve a leer los destinos marcados y comprueba que su
    /// contenido cuadra con el origen (boton Verificar de §2.1).
    /// </summary>
    public async Task VerificarAsync()
    {
        if (EnMarcha) { StatusLine = "ya hay una operacion en marcha"; return; }
        if (!HayOrigen) { StatusLine = "elige la carpeta de origen"; return; }

        var marcados = Devices.Where(d => d.Marked).ToList();
        if (marcados.Count == 0) { StatusLine = "marca al menos un destino"; return; }

        var opciones = new CopyOptions { Verify = true, Structure = StructureMode.Plana };
        var motor = new CopyEngine(opciones);

        EnMarcha = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        StatusLine = "verificando...";

        try
        {
            var raiz = Origen!.Path;
            var ficheros = await Task.Run(() => motor.Enumerar(raiz, token), token);
            var reloj = Stopwatch.StartNew();
            var destinos = marcados.Select(d => new Destino
            {
                Raiz = d.Info.Root, Etiqueta = d.Label, Info = d.Info,
                Estructura = StructureMode.PorCarpetaOrigen
            }).ToList();

            // Se copia con Sobrescribir desactivado y Verify: al estar ya iguales,
            // el motor los salta tras comparar los hashes. Eso es verificar.
            var resultado = await motor.CopiarAsync(ficheros, destinos, null, null, null, token);

            StatusLine = resultado.Success
                ? $"verificado: {Human.Numero(ficheros.Count)} ficheros cuadran ({Human.Time(reloj.Elapsed.TotalSeconds)})"
                : $"la verificacion encontro {resultado.Errors.Count} diferencia(s)";
        }
        catch (OperationCanceledException) { StatusLine = "verificacion cancelada"; }
        catch (Exception ex) { StatusLine = "no se pudo verificar: " + CopyEngine.Motivo(ex); }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            EnMarcha = false;
            Raise(nameof(EnMarcha));
            Raise(nameof(PuedeRefrescar));
        }
    }

    /// <summary>
    /// Aplica un perfil (§2.6) a esta pestana: origen, destinos, estructura y
    /// filtros de fecha. Lo llama la pestana Perfiles con el boton "Aplicar".
    /// </summary>
    public void AplicarPerfil(Profile p)
    {
        if (p.Origen.Length > 0)
        {
            var nombre = p.Origen.TrimEnd('\\', '/').Split('\\', '/').LastOrDefault() ?? p.Origen;
            Origen = new FolderNode(nombre, p.Origen);
        }

        var deseados = p.Destinos.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deseados.Count > 0)
            foreach (var d in Devices)
                d.Marked = deseados.Contains(d.Info.Root) ||
                           (d.Info.DriveLetter is { } l && deseados.Contains(l));

        Estructura = p.Estructura switch
        {
            "hdd" => ModoEstructura.Hdd,
            "flash" => ModoEstructura.Flash,
            _ => ModoEstructura.Auto
        };
        FechaDesde = p.Desde;
        FechaHasta = p.Hasta;

        Raise(nameof(Marcados));
        Raise(nameof(DestinosText));
        Raise(nameof(ResumenCopiar));
        StatusLine = $"perfil aplicado: {p.Nombre}";
    }

    /// <summary>
    /// Reintenta solo lo que fallo en la ultima operacion (F5). Vuelve a lanzar
    /// el motor con las mismas opciones y destinos, pero unicamente con los
    /// ficheros que aparecen en la lista de errores.
    /// </summary>
    public async Task ReintentarFallidosAsync()
    {
        if (EnMarcha) { StatusLine = "ya hay una operacion en marcha"; return; }
        if (_ultimasOpciones is null || UltimoResultado is null || _ultimosDestinos.Count == 0)
        {
            StatusLine = "no hay nada que reintentar";
            return;
        }

        var fallidos = UltimoResultado.Errors.Select(e => e.Fichero)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fuente = _ultimosFicheros
            .Where(f => fallidos.Contains(f.Nombre) || fallidos.Contains(f.RelativePath) ||
                        fallidos.Contains(f.FullPath))
            .ToList();

        if (fuente.Count == 0) { StatusLine = "los ficheros con error ya no estan"; return; }

        var motor = new CopyEngine(_ultimasOpciones);
        EnMarcha = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Raise(nameof(EnMarcha));
        Raise(nameof(PuedeRefrescar));
        StatusLine = $"reintentando {fuente.Count} fichero(s)...";

        try
        {
            var resultado = await motor.CopiarAsync(
                fuente, _ultimosDestinos,
                new Progress<CopyProgress>(pr =>
                {
                    GeneralPercent = pr.Percent;
                    if (pr.FicheroActual.Length > 0) StatusLine = "reintentando " + pr.FicheroActual;
                }),
                null, null, token);

            UltimoResultado = resultado;
            global::QbaswingMultiTask.Stats.StatsStore.Anadir(resultado, Origen?.Path ?? "", "reintento");
            Stats.Refrescar();

            StatusLine = resultado.Success
                ? $"reintento correcto: {Human.Numero(resultado.FilesCopied)} fichero(s)"
                : $"siguen fallando {resultado.Errors.Count} fichero(s)";
        }
        catch (OperationCanceledException) { StatusLine = "reintento cancelado"; }
        catch (Exception ex) { StatusLine = "el reintento fallo: " + CopyEngine.Motivo(ex); }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            EnMarcha = false;
            Raise(nameof(EnMarcha));
            Raise(nameof(PuedeRefrescar));
        }
    }
}