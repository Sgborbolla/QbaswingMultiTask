using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using QbaswingMultiTask.App;
using QbaswingMultiTask.Devices;
using QbaswingMultiTask.Engine;
using QbaswingMultiTask.Sync;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>Una fila de la comparacion, para pintarla.</summary>
public sealed class SyncFila
{
    public string Marca { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string TamTexto { get; init; } = "";
    public string? Pareja { get; init; }
    public bool EsNuevo { get; init; }
}

/// <summary>
/// Pestana Sincronizador (PLAN.md §2.2). Dos paneles tipo Total Commander,
/// mini-discos arriba, arbol expandible, marcas ✓/●/= y las teclas del M2
/// (Ctrl+A/S/U/E/T y Entrar). El motor es el de F1: leer una vez, escribir a N,
/// verificar por hash.
/// </summary>
public sealed class SincronizadorViewModel : Vm
{
    private readonly List<SyncItem> _izq = new();
    private readonly List<SyncItem> _der = new();
    private SyncPlan? _plan;

    public SincronizadorViewModel()
    {
        Discos = new ObservableCollection<DeviceInfo>();
        Filas = new ObservableCollection<SyncFila>();

        AlinearCommand = new RelayCommand(() => Revisar());
        DesalinearCommand = new RelayCommand(DesalinearTodo);
        RevisarCommand = new RelayCommand(Revisar);
        AplicarCommand = new RelayCommand(() => _ = AplicarAsync());
        RefrescarDiscosCommand = new RelayCommand(CargarDiscos);

        CargarDiscos();
        var discos = Discos.Where(d => d.IsReady).ToList();
        if (discos.Count > 0)
        {
            // Los dos paneles tienen que mostrar SIEMPRE una raiz. Si solo hay una
            // unidad lista, el panel derecho usa la misma: antes se quedaba vacio
            // porque solo se rellenaba cuando habia un segundo disco.
            Izquierda = discos[0].Root;
            Derecha = discos.Count > 1 ? discos[1].Root : discos[0].Root;
        }
    }

    public ObservableCollection<DeviceInfo> Discos { get; }
    public ObservableCollection<SyncFila> Filas { get; }
    public ObservableCollection<FolderNode> ArbolIzquierda { get; } = new();
    public ObservableCollection<FolderNode> ArbolDerecha { get; } = new();

    public RelayCommand AlinearCommand { get; }
    public RelayCommand DesalinearCommand { get; }
    public RelayCommand RevisarCommand { get; }
    public RelayCommand AplicarCommand { get; }
    public RelayCommand RefrescarDiscosCommand { get; }

    private string _izquierda = "";
    public string Izquierda
    {
        get => _izquierda;
        set { if (Set(ref _izquierda, value)) { RaisesDePaneles(); CargarArbol(Izquierda, ArbolIzquierda); } }
    }

    private string _derecha = "";
    public string Derecha
    {
        get => _derecha;
        set { if (Set(ref _derecha, value)) { RaisesDePaneles(); CargarArbol(Derecha, ArbolDerecha); } }
    }

    private double _umbral = 75;
    public double Umbral
    {
        get => _umbral;
        set { if (Set(ref _umbral, value)) Raise(nameof(UmbralTexto)); }
    }
    public string UmbralTexto => $"{Umbral:0} %";

    private double _progreso;
    public double Progreso { get => _progreso; set => Set(ref _progreso, value); }

    private string _vista = "pulsa Revisar para comparar los dos paneles";
    public string Vista { get => _vista; set => Set(ref _vista, value); }

    private string _estado = "Listo.";
    public string Estado { get => _estado; set => Set(ref _estado, value); }

    private bool _ocupado;
    public bool Ocupado { get => _ocupado; set { if (Set(ref _ocupado, value)) Raise(nameof(PuedeAplicar)); } }
    public bool PuedeAplicar => !Ocupado && _plan is { Nuevos: > 0 };

    public string NuevosTexto => _plan is null ? "0" : _plan.Nuevos.ToString();
    public string IgualesTexto => _plan is null ? "0" : _plan.Iguales.ToString();
    public string RepetidosTexto => _plan is null ? "0" : _plan.Repetidos.ToString();

    private void RaisesDePaneles()
    {
        Raise(nameof(Izquierda));
        Raise(nameof(Derecha));
    }

    /// <summary>Cambia el panel izquierdo a la unidad pulsada (mini-discos, estilo TC).</summary>
    public void IzquierdaDe(DeviceInfo d)
    {
        Izquierda = d.Root;
        Estado = $"panel izquierdo: {d.Display}";
    }

    public void DerechaDe(DeviceInfo d)
    {
        Derecha = d.Root;
        Estado = $"panel derecho: {d.Display}";
    }

    private void CargarDiscos()
    {
        Discos.Clear();
        foreach (var d in new DeviceManager().Scan(includeSystem: true)) Discos.Add(d);
    }

    private static void CargarArbol(string raiz, ObservableCollection<FolderNode> destino)
    {
        destino.Clear();
        if (string.IsNullOrWhiteSpace(raiz)) return;
        var nodo = new FolderNode(raiz, raiz, raiz: true);
        nodo.Cargar();
        destino.Add(nodo);
    }

    /// <summary>Compara los dos paneles con el umbral actual (tecla Ctrl+A / boton Revisar).</summary>
    public void Revisar()
    {
        if (string.IsNullOrWhiteSpace(Izquierda) || string.IsNullOrWhiteSpace(Derecha))
        {
            Estado = "elige una carpeta en cada panel";
            return;
        }

        try
        {
            var umbral = Math.Clamp(Umbral, 10, 100) / 100.0;
            _plan = Synchronizer.Comparar(Izquierda, Derecha, umbral,
                new Progress<double>(p => Dispatcher.UIThread.Post(() => Progreso = p)));

            _izq.Clear(); _izq.AddRange(_plan.ItemsIzquierda);
            _der.Clear(); _der.AddRange(_plan.ItemsDerecha);

            Filas.Clear();
            foreach (var i in _izq.Where(x => !x.EsCarpeta)
                         .OrderBy(x => x.Estado).ThenBy(x => x.Nombre).Take(400))
                Filas.Add(new SyncFila
                {
                    Marca = i.Marca, Nombre = i.Nombre, TamTexto = i.TamTexto,
                    Pareja = i.Pareja, EsNuevo = i.Estado == SyncEstado.Nuevo
                });

            Raise(nameof(NuevosTexto));
            Raise(nameof(IgualesTexto));
            Raise(nameof(RepetidosTexto));
            Raise(nameof(PuedeAplicar));

            Vista = _plan.Nuevos == 0
                ? $"nada nuevo: {_plan.Iguales} ya coinciden, {_plan.Repetidos} repetidos"
                : $"se copiaran {_plan.Nuevos} fichero(s) · {Human.Size(_plan.BytesACopiar)}";
            Estado = $"comparado: {_plan.ItemsIzquierda.Count} izq · {_plan.ItemsDerecha.Count} der";
        }
        catch (Exception ex)
        {
            Estado = "no se pudo comparar: " + ex.Message;
        }
    }

    /// <summary>Desalinear todos (Ctrl+T): marca todo lo de la derecha como nuevo.</summary>
    public void DesalinearTodo()
    {
        foreach (var j in _der.Where(x => !x.EsCarpeta))
        {
            j.Pareja = null;
            j.Estado = SyncEstado.Nuevo;
        }
        Filas.Clear();
        foreach (var j in _der.Where(x => !x.EsCarpeta).Take(400))
            Filas.Add(new SyncFila
            {
                Marca = j.Marca, Nombre = j.Nombre, TamTexto = j.TamTexto, EsNuevo = true
            });

        if (_plan is not null)
        {
            Vista = $"se copiaran {_plan.ItemsDerecha.Count(x => x.Estado == SyncEstado.Nuevo)} fichero(s)";
            Raise(nameof(NuevosTexto));
            Raise(nameof(PuedeAplicar));
        }
        Estado = "desalineado: todo lo de la derecha cuenta como nuevo";
    }

    /// <summary>Aplica el plan: copia los nuevos del panel derecho al izquierdo (tecla Entrar).</summary>
    public async Task AplicarAsync()
    {
        if (Ocupado) return;
        if (_plan is null || _plan.Nuevos == 0) { Estado = "no hay nada que aplicar"; return; }
        if (string.IsNullOrWhiteSpace(Izquierda) || string.IsNullOrWhiteSpace(Derecha))
        {
            Estado = "los paneles no estan listos";
            return;
        }

        var s = SettingsStore.Actual;
        var opciones = new CopyOptions
        {
            Verify = s.Verificar,
            CopyTimestamps = s.CopiarFechas,
            CopyAttributes = s.CopiarAtributos,
            MinFreeSpace = s.MinLibreMb * 1024L * 1024L,
            Structure = StructureMode.PorCarpetaOrigen,
            Conflict = Conflict.SaltarSiIgual
        };

        Ocupado = true;
        Raise(nameof(PuedeAplicar));
        try
        {
            var progreso = new Progress<CopyProgress>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    Progreso = p.Percent;
                    if (p.FicheroActual.Length > 0) Estado = "copiando " + p.FicheroActual;
                }));

            var resultado = await Synchronizer.AplicarAsync(_plan, true, opciones, progreso);
            Stats.StatsStore.Anadir(resultado, Derecha, "sincronizar");

            Estado = resultado.Success
                ? $"sincronizado: {Human.Numero(resultado.FilesCopied)} fichero(s) · " +
                  $"{Human.Size(resultado.BytesCopied)} · {Human.Time(resultado.Duracion.TotalSeconds)}"
                : $"termino con {resultado.Errors.Count} problema(s)";
            Vista = "se copiaron los nuevos del paquete entrante";
            Revisar();
        }
        catch (Exception ex)
        {
            Estado = "la sincronizacion fallo: " + CopyEngine.Motivo(ex);
        }
        finally
        {
            Ocupado = false;
            Raise(nameof(PuedeAplicar));
        }
    }
}
