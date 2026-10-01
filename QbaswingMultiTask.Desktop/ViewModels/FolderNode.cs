using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace QbaswingMultiTask.Desktop;

/// <summary>
/// Nodo del arbol del explorador. Los hijos se cargan solo cuando se despliega,
/// porque un arbol de todo el disco con miles de carpetas ralentizaria la ventana.
/// PLAN.md 2.1: la izquierda es el Explorador de Windows, con "Este equipo" como
/// raiz y los discos como nodos dentro.
/// </summary>
public sealed class FolderNode : Vm
{
    private readonly bool _raiz;
    private bool _cargado;
    private bool _cargando;
    private string? _error;

    public FolderNode(string nombre, string ruta, bool raiz = false, bool esFichero = false,
                      bool esProvisional = false)
    {
        Name = nombre;
        Path = ruta;
        _raiz = raiz;
        EsFichero = esFichero;
        EsProvisional = esProvisional;
        Children = new ObservableCollection<FolderNode>();
        IsExpanded = false;

        // Un nodo provisional para que el arbol dibuje la flecha: sin hijos,
        // Avalonia no pone la flecha y la carpeta pareceria vacia hasta abrirla.
        if (!raiz && !esFichero && !esProvisional)
            Children.Add(new FolderNode("cargando...", "", esProvisional: true));
    }

    /// <summary>Nodo de relleno, no una carpeta de verdad.</summary>
    public bool EsProvisional { get; }

    public string Name { get; }
    public string Path { get; }

    /// <summary>Carpeta de la que cuelga este nodo.</summary>
    public FolderNode? Parent { get; internal set; }

    public ObservableCollection<FolderNode> Children { get; }

    /// <summary>Un disco sin medio no se puede abrir: sale vacio, con su aviso.</summary>
    public bool IsEmpty => _error is not null && Children.Count == 0;

    public string? Error => _error;

    /// <summary>Icono segun el tipo: PALETA de §1, el icono es CELESTE.</summary>
    public string Glyph => _raiz ? "▣" : IsEmpty ? "⊘" : "▸";

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value)) return;
            if (value) Cargar();
        }
    }

    /// <summary>
    /// Marca de hijo provisional: al desplegar sale un nodo "cargando" para que
    /// se vea que hay algo mas. Se quita en cuanto se lee el directorio real.
    /// </summary>
    public bool IsCargando => _cargando;

    public void Cargar()
    {
        if (_cargado || _cargando) return;
        _cargado = true;

        Children.Clear();
        _error = null;

        // La raiz "Este equipo" lleva dentro los dispositivos (PLAN.md 2.1).
        if (_raiz)
        {
            foreach (var d in new QbaswingMultiTask.Devices.DeviceManager().Scan())
                Add(new FolderNode(d.Display, d.Root) { Parent = this });
            Raise(nameof(Glyph));
            return;
        }

        _cargando = true;
        Raise(nameof(IsCargando));

        try
        {
            var dir = new DirectoryInfo(Path);
            if (!dir.Exists) { _error = "no disponible"; return; }

            // Primero las carpetas, despues los ficheros. Los ocultos del
            // sistema no se muestran, que si no el arbol es inservible.
            var sub = dir.GetDirectories()
                        .Where(d => !EsDeSistema(d))
                        .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var d in sub) Add(new FolderNode(d.Name, d.FullName) { Parent = this });

            var arch = dir.GetFiles()
                        .Where(f => !EsDeSistema(f))
                        .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                        .Take(400);                       // el listado completo en el panel derecho
            foreach (var f in arch) Add(new FolderNode(f.Name, f.FullName) { Parent = this, EsFichero = true });

            if (Children.Count == 0) _error = "vacia";
        }
        catch (UnauthorizedAccessException) { _error = "sin permiso"; }
        catch (IOException) { _error = "no disponible"; }
        catch (Exception) { _error = "no disponible"; }
        finally
        {
            _cargando = false;
            Raise(nameof(IsCargando));
            Raise(nameof(Glyph));
            Raise(nameof(IsEmpty));
        }
    }

    public bool EsFichero { get; init; }

    private void Add(FolderNode n) => Children.Add(n);

    /// <summary>Carpeta oculta o del sistema: no se lista en el arbol.</summary>
    private static bool EsDeSistema(DirectoryInfo d)
    {
        var n = d.Name;
        if (n.StartsWith('$')) return true;                       // $Recycle.Bin
        if (n.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) return true;
        try { return (d.Attributes & FileAttributes.Hidden) != 0; } catch { return false; }
    }

    private static bool EsDeSistema(FileInfo f)
    {
        try { return (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
        catch { return false; }
    }

    /// <summary>Ruta completa para mostrarla al elegir un nodo.</summary>
    public override string ToString() => Path;
}