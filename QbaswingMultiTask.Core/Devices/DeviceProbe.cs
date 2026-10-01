using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QbaswingMultiTask.Devices;

/// <summary>
/// Datos fisicos de una unidad. PLAN.md §3 (apartado "Deteccion fisica del
/// dispositivo") pide saber el tipo, la marca y el modelo, si es SSD o mecanico
/// y la velocidad maxima del enlace. Todo lo que no se pueda leer se deja a null
/// o 0: nunca se inventa.
/// </summary>
public sealed class DevicePhysical
{
    /// <summary>"USB", "SATA", "NVMe", "Red"...</summary>
    public string Bus { get; init; } = "—";

    /// <summary>Velocidad maxima del enlace en Mbps, o 0 si no se sabe.</summary>
    public double MaxMbps { get; init; }

    /// <summary>true = SSD (no penaliza el acceso aleatorio), false = mecanico, null = no se sabe.</summary>
    public bool? IsSsd { get; init; }

    public string? Marca { get; init; }
    public string? Modelo { get; init; }

    /// <summary>La unidad va lenta de verdad (USB 1.1 o medida por debajo del umbral).</summary>
    public bool EsLento { get; init; }

    /// <summary>"USB · 480 Mbps · SSD · Kingston DataTraveler": lo que se enseña.</summary>
    public string Resumen
    {
        get
        {
            var partes = new List<string>();
            if (Bus is { Length: > 0 } && Bus != "—") partes.Add(Bus);
            if (MaxMbps > 0) partes.Add($"{MaxMbps:0} Mbps");
            if (IsSsd is true) partes.Add("SSD");
            else if (IsSsd is false) partes.Add("mecanico");
            var nombre = string.Join(" ", new[] { Marca, Modelo }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (nombre.Length > 0) partes.Add(nombre);
            if (EsLento) partes.Add("lento");
            return partes.Count == 0 ? "sin datos fisicos" : string.Join(" · ", partes);
        }
    }
}

/// <summary>
/// Lee los datos fisicos que Windows expone por IOCTL_STORAGE_QUERY_PROPERTY.
/// PLAN.md §8.1 documenta los fallos reales de la version anterior (PropertyId
/// equivocado que invertia SSD/HDD, offsets mal leidos, GENERIC_READ solo): aqui
/// van corregidos: PropertyId 7 para el seek-penalty, el descriptor con su
/// layout real (vendor 12, product 16, bus 28) y cadenas acotadas a su tamano.
/// </summary>
public static class DeviceProbe
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;

    private const uint StorageDeviceProperty = 0;
    private const uint StorageSeekPenaltyProperty = 7;
    private const uint PropertyStandardQuery = 0;

    private static readonly Dictionary<string, DevicePhysical> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Datos fisicos de la raiz ("C:\" o "\\?\Volume{...}\"). Se cachean por Id.</summary>
    public static DevicePhysical Probe(string raiz, string id)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(id, out var guardado)) return guardado;
        }

        var datos = Leer(raiz);
        lock (_cache)
        {
            _cache[id] = datos;
        }
        return datos;
    }

    /// <summary>Olvida la cache: al renovar dispositivos se vuelve a preguntar.</summary>
    public static void Olvidar(string? id = null)
    {
        lock (_cache)
        {
            if (id is null) _cache.Clear();
            else _cache.Remove(id);
        }
    }

    private static DevicePhysical Leer(string raiz)
    {
        if (!OperatingSystem.IsWindows()) return LeerNoWindows(raiz);

        var ruta = RaizADispositivo(raiz);
        if (ruta is null) return new DevicePhysical();

        // IOCTL_STORAGE_QUERY_PROPERTY es FILE_ANY_ACCESS: se puede preguntar con
        // acceso nulo, sin ser administrador. Se prueba eso primero y, si el
        // sistema lo rechaza, se reintenta con GENERIC_READ.
        var h = CreateFileW(ruta, 0,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (h.IsInvalid)
        {
            h = CreateFileW(ruta, GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        }
        using var _ = h;
        if (h.IsInvalid) return new DevicePhysical();

        string bus = "—";
        var mbps = 0.0;
        string? marca = null, modelo = null;
        bool? ssd = null;

        // --- descriptor del dispositivo (bus, fabricante, modelo) ---
        var salida = new byte[1024];
        if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY,
                Query(StorageDeviceProperty), 12, salida, (uint)salida.Length, out var devuelto, IntPtr.Zero)
            && devuelto >= 36)
        {
            var busType = BitConverter.ToUInt32(salida, 28);
            bus = NombreBus(busType);
            mbps = MbpsDe(busType);

            var offVendor = BitConverter.ToUInt32(salida, 12);
            var offProduct = BitConverter.ToUInt32(salida, 16);
            marca = Cadena(salida, offVendor, devuelto);
            modelo = Cadena(salida, offProduct, devuelto);
        }

        // --- penalizacion de acceso aleatorio: SSD o mecanico ---
        var penalty = new byte[12];
        if (DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY,
                Query(StorageSeekPenaltyProperty), 12, penalty, (uint)penalty.Length, out var devuelto2, IntPtr.Zero)
            && devuelto2 >= 9)
        {
            ssd = penalty[8] == 0;      // IncursSeekPenalty == false -> SSD
        }

        var lento = bus.Equals("USB", StringComparison.OrdinalIgnoreCase) && mbps is > 0 and <= 12;
        return new DevicePhysical
        {
            Bus = bus,
            MaxMbps = mbps,
            IsSsd = ssd,
            Marca = marca,
            Modelo = modelo,
            EsLento = lento
        };
    }

    private static DevicePhysical LeerNoWindows(string raiz)
    {
        // Linux: /sys/block/<dev>/queue/rotational (0 = SSD, 1 = mecanico).
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var nombre = new System.IO.DriveInfo(raiz).Name.TrimEnd('/');
                var dev = System.IO.Path.GetFileName(nombre);
                var ruta = $"/sys/block/{dev}/queue/rotational";
                if (System.IO.File.Exists(ruta))
                {
                    var texto = System.IO.File.ReadAllText(ruta).Trim();
                    return new DevicePhysical
                    {
                        Bus = "—",
                        IsSsd = texto == "0"
                    };
                }
            }
        }
        catch { }
        return new DevicePhysical();
    }

    private static byte[] Query(uint propiedad)
    {
        var q = new byte[12];
        BitConverter.GetBytes(propiedad).CopyTo(q, 0);
        BitConverter.GetBytes(PropertyStandardQuery).CopyTo(q, 4);
        return q;
    }

    private static string? Cadena(byte[] bufer, uint offset, uint tamano)
    {
        if (offset == 0 || offset >= tamano) return null;
        var fin = (int)offset;
        while (fin < tamano && bufer[fin] != 0) fin++;
        if (fin <= offset) return null;
        var texto = System.Text.Encoding.ASCII.GetString(bufer, (int)offset, fin - (int)offset).Trim();
        return texto.Length == 0 ? null : texto;
    }

    /// <summary>"C:\" -> "\\.\C:"; un volumen sin letra no se puede abrir asi.</summary>
    private static string? RaizADispositivo(string raiz)
    {
        if (raiz.Length >= 2 && raiz[1] == ':') return @"\\.\" + raiz[..2];
        return null;
    }

    /// <summary>STORAGE_BUS_TYPE de Windows.</summary>
    private static string NombreBus(uint tipo) => tipo switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "1394",
        5 => "SSA",
        6 => "Fibre",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 => "Virtual",
        15 => "Virtual",
        16 => "Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "—"
    };

    /// <summary>Velocidad maxima teorica del enlace. Es un techo, no una medida.</summary>
    private static double MbpsDe(uint tipo) => tipo switch
    {
        3 => 133,          // ATA
        4 => 400,          // 1394
        6 => 8000,         // Fibre
        7 => 480,          // USB (2.0 como base; se sube al medir)
        9 => 1000,         // iSCSI
        10 => 6000,        // SAS
        11 => 6000,        // SATA
        17 => 32000,       // NVMe
        14 or 15 or 16 => 32000,  // virtual
        _ => 0
    };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string nombre, uint acceso, uint compartir, IntPtr seguridad,
        uint creacion, uint atributos, IntPtr plantilla);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle h, uint codigo, byte[] entrada, uint tamEntrada,
        byte[] salida, uint tamSalida, out uint devuelto, IntPtr solapado);
}
