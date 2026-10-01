namespace QbaswingMultiTask.Devices;

public enum DeviceKind
{
    SystemDisk, Internal, UsbExternal, UsbFlash, NetworkShare, Optical, Removable, Unknown,
    /// <summary>Memoria USB que va lenta de verdad (§3): USB 1.1 o medida por debajo del umbral.</summary>
    UsbFlashSlow,
    /// <summary>Disco USB que va lento de verdad (§3).</summary>
    UsbExternalSlow
}

/// <summary>Un dispositivo de almacenamiento. Alimenta los rectangulos de la pestana Copiar/Mover.</summary>
public sealed class DeviceInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Label { get; init; }
    /// <summary>Raiz completa (C:\, \\?\Volume{...}, \\servidor\recurso).</summary>
    public string Root { get; init; } = "";
    public string? DriveLetter { get; init; }
    public DeviceKind Kind { get; init; }
    public long Capacity { get; init; }
    public long FreeSpace { get; init; }
    public string FileSystem { get; init; } = "";
    public bool IsReady { get; init; }
    public bool IsSystem { get; init; }

    // --- datos fisicos (F2, DeviceProbe) ---
    /// <summary>Bus del enlace: USB, SATA, NVMe...</summary>
    public string Bus { get; set; } = "—";
    /// <summary>Techo del enlace en Mbps (0 = no se sabe).</summary>
    public double MaxMbps { get; set; }
    /// <summary>true = SSD, false = mecanico, null = no se pudo saber.</summary>
    public bool? IsSsd { get; set; }
    public string? Marca { get; set; }
    public string? Modelo { get; set; }
    /// <summary>Va lento: USB 1.1 o medido por debajo del umbral.</summary>
    public bool EsLento { get; set; }
    /// <summary>Velocidad real medida durante la copia, en bytes/s (0 = aun no se ha medido).</summary>
    public double VelocidadMedida { get; set; }

    /// <summary>Solo USB/memoria/red: son los que se usan como destino.</summary>
    public bool IsExternal => Kind is DeviceKind.UsbFlash or DeviceKind.UsbExternal or DeviceKind.NetworkShare
                              or DeviceKind.UsbFlashSlow or DeviceKind.UsbExternalSlow;
    public bool IsUsb => Kind is DeviceKind.UsbFlash or DeviceKind.UsbExternal
                         or DeviceKind.UsbFlashSlow or DeviceKind.UsbExternalSlow;
    public bool IsNetwork => Kind == DeviceKind.NetworkShare;
    public bool HasLetter => DriveLetter != null;

    public long UsedSpace => Math.Max(0, Capacity - FreeSpace);
    public double UsedPercent => Capacity > 0 ? UsedSpace * 100.0 / Capacity : 0;

    /// <summary>Etiqueta corta del tipo, para el rectangulo.</summary>
    public string KindLabel => Kind switch
    {
        DeviceKind.SystemDisk => "Disco del sistema",
        DeviceKind.Internal => "Disco interno",
        DeviceKind.UsbExternal => "USB externo",
        DeviceKind.UsbFlash => "USB",
        DeviceKind.UsbFlashSlow => "USB lenta",
        DeviceKind.UsbExternalSlow => "USB externo lento",
        DeviceKind.NetworkShare => "Unidad de red",
        DeviceKind.Optical => "Unidad optica",
        DeviceKind.Removable => "Removible",
        _ => "Dispositivo"
    };

    /// <summary>"USB · 480 Mbps · SSD · Kingston": los datos fisicos en una linea (F2).</summary>
    public string Fisico
    {
        get
        {
            var partes = new List<string>();
            if (Bus is { Length: > 0 } && Bus != "—") partes.Add(Bus);
            if (MaxMbps > 0) partes.Add($"{MaxMbps:0} Mbps");
            if (IsSsd is true) partes.Add("SSD");
            else if (IsSsd is false) partes.Add("mecanico");
            if (VelocidadMedida > 0) partes.Add($"{Util.Human.Size((long)VelocidadMedida)}/s medidos");
            else if (EsLento) partes.Add("lento");
            var nombre = string.Join(" ", new[] { Marca, Modelo }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (nombre.Length > 0) partes.Add(nombre);
            return partes.Count == 0 ? "" : string.Join(" · ", partes);
        }
    }

    /// <summary>Copia con los datos fisicos ya rellenados (F2).</summary>
    public DeviceInfo ConFisico(DevicePhysical f) => new()
    {
        Id = Id, Name = Name, Label = Label, Root = Root, DriveLetter = DriveLetter,
        Kind = Kind, Capacity = Capacity, FreeSpace = FreeSpace, FileSystem = FileSystem,
        IsReady = IsReady, IsSystem = IsSystem,
        Bus = f.Bus, MaxMbps = f.MaxMbps, IsSsd = f.IsSsd, Marca = f.Marca,
        Modelo = f.Modelo, EsLento = f.EsLento, VelocidadMedida = VelocidadMedida
    };

    /// <summary>Cambia el tipo (por ejemplo a "lenta") sin tocar lo demas.</summary>
    public DeviceInfo ConKind(DeviceKind kind, bool lento, double velocidadMedida) => new()
    {
        Id = Id, Name = Name, Label = Label, Root = Root, DriveLetter = DriveLetter,
        Kind = kind, Capacity = Capacity, FreeSpace = FreeSpace, FileSystem = FileSystem,
        IsReady = IsReady, IsSystem = IsSystem,
        Bus = Bus, MaxMbps = MaxMbps, IsSsd = IsSsd, Marca = Marca,
        Modelo = Modelo, EsLento = lento, VelocidadMedida = velocidadMedida
    };

    /// <summary>Estructura de carpetas segun el tipo de unidad (PLAN.md F2).</summary>
    public IReadOnlyList<string> Structure() => Kind switch
    {
        DeviceKind.SystemDisk or DeviceKind.Internal =>
            new[] { "Documentos", "Musica", "Videos", "Fotos", "Instaladores", "Backup" },
        DeviceKind.UsbExternal or DeviceKind.UsbExternalSlow =>
            new[] { "Compartido", "Documentos", "Musica", "Videos", "Fotos" },
        DeviceKind.UsbFlash or DeviceKind.UsbFlashSlow =>
            new[] { "Documentos", "Musica", "Videos" },
        DeviceKind.NetworkShare => new[] { "Datos", "Documentos", "Musica", "Videos", "Backup" },
        _ => new[] { "Documentos", "Fotos" }
    };

    public string Display => Label is { Length: > 0 } l
        ? $"{l} ({DriveLetter ?? "sin letra"})"
        : Name;

    public override string ToString() =>
        $"{Display} — {Util.Human.Size(FreeSpace)} libres de {Util.Human.Size(Capacity)}";
}

/// <summary>Descubre los dispositivos montados. Windows ya incluye los volumenes SIN letra
/// en DriveInfo.GetDrives(), asi que el caso \\?\Volume{GUID} se resuelve sin P/Invoke.</summary>
public sealed class DeviceManager
{
    /// <summary>
    /// Descubre los volumenes montados y devuelve los que pueden ser destino
    /// de copia. PLAN.md seccion 2.1 dibuja tambien el disco del sistema como
    /// rectangulo ("| ▉ C|"), asi que por defecto entra: si se filtrara, en un
    /// equipo sin USB la pestana se quedaria sin ningun rectangulo.
    /// </summary>
    public IReadOnlyList<DeviceInfo> Scan(bool includeSystem = true, bool includeOptical = false)
    {
        var result = new List<DeviceInfo>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); } catch { return result; }

        foreach (var d in drives)
        {
            var kind = Classify(d, out var isSystem);
            if (isSystem && !includeSystem) continue;
            if (kind == DeviceKind.Optical && !includeOptical) continue;

            var info = Describe(d, kind, isSystem);
            if (info == null) continue;
            if (!info.IsReady) continue;              // vacio, sin medio: no es destino
            if (result.Any(x => string.Equals(x.Id, info.Id, StringComparison.OrdinalIgnoreCase)))
                continue;                              // mismo volumen duplicado

            // F2: datos fisicos (bus, SSD/mecanico, marca) y lentitud.
            info = info.ConFisico(DeviceProbe.Probe(info.Root, info.Id));
            info = AplicarLentitud(info);
            result.Add(info);
        }

        // Orden del boceto de PLAN.md 2.1: primero USB/memoria, luego red,
        // y el disco del sistema al final.
        return result
            .OrderByDescending(d => d.IsUsb)
            .ThenByDescending(d => d.IsNetwork)
            .ThenByDescending(d => d.IsExternal)
            .ThenByDescending(d => d.IsSystem)
            .ThenBy(d => d.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Velocidades reales medidas por el motor, por Id de dispositivo. PLAN.md §3:
    /// "ademas auto-marca desde la velocidad real medida". El umbral es 5 MB/s
    /// sostenidos: por debajo, una memoria USB va lenta de verdad.
    /// </summary>
    private static readonly Dictionary<string, double> _medidas =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Por debajo de esto (5 MB/s) la unidad se considera lenta.</summary>
    public const double UmbralLentoBytes = 5.0 * 1024 * 1024;

    /// <summary>El motor informa de la velocidad real de una unidad.</summary>
    public static void MedirVelocidad(string id, double bytesPorSegundo)
    {
        if (string.IsNullOrEmpty(id) || bytesPorSegundo <= 0) return;
        lock (_medidas) _medidas[id] = bytesPorSegundo;
    }

    /// <summary>Velocidad medida de una unidad, o 0 si no se ha medido.</summary>
    public static double VelocidadMedida(string id)
    {
        lock (_medidas) return _medidas.TryGetValue(id, out var v) ? v : 0;
    }

    /// <summary>Borra las medidas (al empezar una copia nueva).</summary>
    public static void OlvidarMedidas()
    {
        lock (_medidas) _medidas.Clear();
    }

    /// <summary>
    /// Decide si la unidad va lenta: USB 1.1 detectado, o velocidad real medida
    /// por debajo del umbral. En ese caso cambia el tipo a la variante "lenta",
    /// que ademas usa un buffer mas pequeno (1 MB) al copiar.
    /// </summary>
    private static DeviceInfo AplicarLentitud(DeviceInfo info)
    {
        var medida = VelocidadMedida(info.Id);
        var lento = info.EsLento ||
                    (info.IsUsb && medida > 0 && medida < UmbralLentoBytes);

        var kind = info.Kind;
        if (lento)
        {
            if (kind == DeviceKind.UsbFlash) kind = DeviceKind.UsbFlashSlow;
            else if (kind == DeviceKind.UsbExternal) kind = DeviceKind.UsbExternalSlow;
        }
        else
        {
            // Se recupera el tipo normal si ya no procede la marca.
            if (kind == DeviceKind.UsbFlashSlow) kind = DeviceKind.UsbFlash;
            else if (kind == DeviceKind.UsbExternalSlow) kind = DeviceKind.UsbExternal;
        }

        return info.ConKind(kind, lento, medida);
    }

    private static DeviceKind Classify(DriveInfo d, out bool isSystem)
    {
        isSystem = false;
        DriveType t;
        try { t = d.DriveType; } catch { return DeviceKind.Unknown; }

        switch (t)
        {
            case DriveType.CDRom:
            case DriveType.Ram:
                return DeviceKind.Optical;
            case DriveType.Network:
                return DeviceKind.NetworkShare;
            case DriveType.Removable:
                return DeviceKind.UsbFlash;
            case DriveType.Fixed:
            default:
                if (!d.IsReady) return DeviceKind.Removable;
                isSystem = IsSystemVolume(d.Name);
                return isSystem ? DeviceKind.SystemDisk : DeviceKind.Internal;
        }
    }

    private static bool IsSystemVolume(string name)
    {
        try
        {
            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var root = Path.GetPathRoot(win);
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(name, root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }
        // Un volumen SIN letra no es el del sistema por ser sin letra.
        return false;
    }

    private static DeviceInfo? Describe(DriveInfo d, DeviceKind kind, bool isSystem)
    {
        string name;
        try { name = d.Name; } catch { return null; }

        string? label = null, letter = null;
        long cap = 0, free = 0;
        var fs = "";
        var ready = false;
        try
        {
            ready = d.IsReady;
            if (ready)
            {
                cap = d.TotalSize;
                free = d.AvailableFreeSpace;
                label = d.VolumeLabel;
                fs = d.DriveFormat;
            }
        }
        catch { }

        // "\\?\Volume{GUID}\" -> volumen sin letra
        var root = name;                      // la raiz real, para copiar dentro
        var noLetter = name.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase);
        if (noLetter)
        {
            var guid = name.TrimEnd('\\');
            var ini = guid.IndexOf('{');
            var fin = guid.IndexOf('}');
            if (ini >= 0 && fin > ini)
                guid = guid[(ini + 1)..fin];
            if (!string.IsNullOrEmpty(label) && kind == DeviceKind.Unknown)
                kind = DeviceKind.UsbExternal;
            name = string.IsNullOrEmpty(label)
                ? $"Volumen sin letra {guid[..Math.Min(8, guid.Length)]}"
                : label;
        }
        else if (name.Length <= 3)
        {
            letter = name;                     // "C:\"
        }

        if (label is { Length: > 0 } && !noLetter) name = $"{label} ({letter ?? name})";

        return new DeviceInfo
        {
            Id = BuildId(d),
            Name = name,
            Label = label,
            Root = root,                        // "C:\" o "\\?\Volume{...}\", no el texto
            DriveLetter = letter,
            Kind = isSystem ? DeviceKind.SystemDisk : kind,
            Capacity = cap,
            FreeSpace = free,
            FileSystem = fs,
            IsReady = ready,
            IsSystem = isSystem
        };
    }

    private static string BuildId(DriveInfo d)
    {
        try
        {
            var name = d.Name;
            if (name.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                return name; // el GUID es estable y unico
            return $"{name}|{d.DriveFormat}|{d.VolumeLabel}";
        }
        catch { return d.Name; }
    }
}