using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using QbaswingMultiTask.Devices;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Desktop;

/// <summary>
/// Un rectangulo de la derecha de la pestana Copiar/Mover. Es GRANDE: por eso
/// lleva su casilla, su barra de capacidad y su barra de copia propias.
/// </summary>
public sealed class DeviceViewModel : Vm
{
    private DeviceInfo _info;

    public DeviceViewModel(DeviceInfo d)
    {
        _info = d;
        Marked = false;
        CopyPercent = 0;
        CopyActive = false;
        HasError = false;
    }

    public DeviceInfo Info => _info;

    /// <summary>
    /// Refresca los datos del dispositivo (espacio libre, etiqueta) sin perder
    /// lo que el usuario ya marco ni el progreso de la copia.
    /// </summary>
    public void Actualizar(DeviceInfo nuevo)
    {
        _info = nuevo;
        Raise(nameof(Info));
        Raise(nameof(Label));
        Raise(nameof(SubLabel));
        Raise(nameof(UsedPercent));
        Raise(nameof(CapacityText));
        Raise(nameof(SizeText));
        Raise(nameof(Fisico));
        Raise(nameof(TieneFisico));
        Raise(nameof(Cabecera));
    }

    /// <summary>True = es destino de la copia (la casilla de arriba).</summary>
    public bool Marked
    {
        get => _marked;
        set { if (Set(ref _marked, value)) { Raise(nameof(MarkGlyph)); Raise(nameof(SubLabel)); Raise(nameof(CopyText)); } }
    }
    private bool _marked;

    /// <summary>Esta unidad esta grabando ahora mismo: borde CELESTE (§1).</summary>
    public bool CopyActive
    {
        get => _copyActive;
        set => Set(ref _copyActive, value);
    }
    private bool _copyActive;

    /// <summary>Un fallo en esta unidad: borde ROJO (§1).</summary>
    public bool HasError
    {
        get => _hasError;
        set => Set(ref _hasError, value);
    }
    private bool _hasError;

    /// <summary>0..100 de la copia hacia ESTA unidad.</summary>
    public double CopyPercent
    {
        get => _copyPercent;
        set { if (Set(ref _copyPercent, System.Math.Clamp(value, 0, 100))) Raise(nameof(CopyText)); }
    }
    private double _copyPercent;

    /// <summary>Bytes que ya han entrado en ESTA unidad, medidos por el motor.</summary>
    public long CopyDoneBytes { get; private set; }

    /// <summary>Bytes que le tocan a ESTA unidad (suma de los ficheros de la copia).</summary>
    public long CopyTotalBytes { get; private set; }

    /// <summary>
    /// El motor avisa de cada fichero que acaba de entrar en esta unidad. Con eso
    /// la barra propia de cada rectangulo avanza con datos reales, no estimados.
    /// </summary>
    public void SumarBytes(long bytes)
    {
        CopyDoneBytes += bytes;
        if (CopyTotalBytes > 0)
            CopyPercent = CopyDoneBytes * 100.0 / CopyTotalBytes;
    }

    /// <summary>Prepara la unidad para una copia nueva.</summary>
    public void PrepararCopia(long totalBytes)
    {
        CopyDoneBytes = 0;
        CopyTotalBytes = totalBytes;
        CopyPercent = 0;
        HasError = false;
        CopyActive = Marked;
        Raise(nameof(CopyText));
    }

    /// <summary>Texto de la barra de copia: el porcentaje o el estado.</summary>
    public string CopyText => !Marked ? "sin marcar"
        : CopyActive ? $"{CopyPercent:0} %"
        : "en espera";

    /// <summary>
    /// Nombre corto del dispositivo, como en el boceto de §2.1: "PENDRA" si
    /// tiene etiqueta, y solo la letra ("C") si no la tiene.
    /// </summary>
    public string Label => Info.Label is { Length: > 0 } l
        ? l
        : Info.DriveLetter is { Length: > 0 } k
            ? k.TrimEnd('\\')
            : Info.Name;

    /// <summary>Lo que va en el recuadro grande de arriba: la letra o la inicial.</summary>
    public string Cabecera => Info.DriveLetter is { Length: > 0 } k
        ? k.TrimEnd('\\')
        : Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";

    /// <summary>El check de la casilla: vacio si no esta marcada.</summary>
    public string MarkGlyph => Marked ? "✔" : "";

    /// <summary>Segunda linea: tipo de unidad y espacio libre.</summary>
    public string SubLabel => Marked
        ? $"{Info.KindLabel} · destino"
        : $"{Info.KindLabel} · {Human.Size(Info.FreeSpace)} libres";

    /// <summary>Datos fisicos (F2): "SATA · 6000 Mbps · SSD · SAMSUNG ...".</summary>
    public string Fisico => Info.Fisico;

    /// <summary>Hay datos fisicos que enseñar.</summary>
    public bool TieneFisico => Fisico.Length > 0;

    /// <summary>
    /// Linea de tamaño del rectangulo, tal cual el boceto de §2.1: "64–22%",
    /// es decir capacidad real de la unidad y porcentaje realmente ocupado.
    /// Los dos numeros salen de DriveInfo (TotalSize / AvailableFreeSpace).
    /// </summary>
    public string SizeText => $"{Human.Size(Info.Capacity)}–{Info.UsedPercent:0}%";

    /// <summary>"180 GB libres · 57,8 GB usados": los dos datos reales.</summary>
    public string CapacityText =>
        $"{Human.Size(Info.FreeSpace)} libres · {Human.Size(Info.UsedSpace)} usados";

    /// <summary>Porcentaje de ocupacion, para la barra de capacidad (que nunca se mueve).</summary>
    public double UsedPercent => Info.UsedPercent;

    /// <summary>Contenido de la unidad, para el mini-explorador del rectangulo.</summary>
    public ObservableCollection<FolderNode> Contenido { get; } = new();

    /// <summary>Titulo del mini-explorador cuando se despliega un rectangulo.</summary>
    public string TituloContenido => $"{Label} — contenido";

    /// <summary>Carga el contenido del rectangulo (una sola vez por refresco).</summary>
    public void CargarContenido()
    {
        Contenido.Clear();
        try
        {
            var dir = new DirectoryInfo(Info.Root);
            if (!dir.Exists)
            {
                var aviso = new FolderNode("(no disponible)", Info.Root);
                Contenido.Add(aviso);
                return;
            }
            foreach (var d in dir.GetDirectories().OrderBy(d => d.Name))
                Contenido.Add(new FolderNode(d.Name, d.FullName));
            foreach (var f in dir.GetFiles().Take(500).OrderBy(f => f.Name))
                Contenido.Add(new FolderNode(f.Name, f.FullName) { EsFichero = true });
        }
        catch (System.Exception)
        {
            Contenido.Add(new FolderNode("(sin permiso)", Info.Root));
        }
    }

    public void ResetCopyState()
    {
        CopyActive = false;
        CopyPercent = 0;
        HasError = false;
    }
}