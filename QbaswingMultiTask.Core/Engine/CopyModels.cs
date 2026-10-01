using System;
using System.Collections.Generic;
using System.IO;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Engine;

/// <summary>
/// Modo de estructura del destino. PLAN.md §3 dice que el original tenia 7 modos;
/// los siete estan aqui, y ademas el modo de estructura segun el tipo de unidad
/// (HDD vs FLASH) que pide §3 apartado "Nuevo".
/// </summary>
public enum StructureMode
{
    /// <summary>Todo suelto en la raiz del destino.</summary>
    Plana = 0,
    /// <summary>Una carpeta por extension: MP4, MKV, PDF...</summary>
    PorExtension = 1,
    /// <summary>Musica / Videos / Fotos / Documentos / Instaladores.</summary>
    PorTipo = 2,
    /// <summary>Ano y mes: 2026\10.</summary>
    PorFecha = 3,
    /// <summary>Con la misma forma que tenia en el origen.</summary>
    PorCarpetaOrigen = 4,
    /// <summary>Pequenos / Medianos / Grandes.</summary>
    PorTamano = 5,
    /// <summary>Un fichero de 0 KB por cada uno de verdad: sirve de inventario rapido.</summary>
    SoloNombres = 6
}

/// <summary>Como se decide el modo de estructura cuando esta en automatico (§3).</summary>
public static class EstructuraAuto
{
    /// <summary>USB/memoria: por tipo. Disco duro/red: por carpeta de origen.</summary>
    public static StructureMode Para(Devices.DeviceInfo? d) => d?.Kind switch
    {
        Devices.DeviceKind.UsbFlash => StructureMode.PorTipo,
        Devices.DeviceKind.UsbExternal => StructureMode.PorTipo,
        Devices.DeviceKind.NetworkShare => StructureMode.PorCarpetaOrigen,
        Devices.DeviceKind.SystemDisk => StructureMode.PorCarpetaOrigen,
        Devices.DeviceKind.Internal => StructureMode.PorCarpetaOrigen,
        _ => StructureMode.Plana
    };
}

/// <summary>Modo de fecha de §3: las tres formas que ya tenia el original.</summary>
public enum DateMode
{
    /// <summary>Todo.</summary>
    Todas = 0,
    /// <summary>Solo lo modificado despues de una fecha.</summary>
    MasNuevoQue = 1,
    /// <summary>Solo lo que este entre dos fechas.</summary>
    Entre = 2,
    /// <summary>Solo lo de esa fecha exacta.</summary>
    Igual = 3
}

/// <summary>Que hacer cuando el fichero ya existe en el destino.</summary>
public enum Conflict
{
    /// <summary>Reanudar: si el contenido ya cuadra, no se copia otra vez.</summary>
    SaltarSiIgual = 0,
    /// <summary>Sobrescribir siempre.</summary>
    Sobrescribir = 1,
    /// <summary>Dejar lo que habia.</summary>
    Saltar = 2,
    /// <summary>Guardar el nuevo al lado con "(2)".</summary>
    Renombrar = 3
}

/// <summary>Opciones del motor. Todo lo que estaba declarado y sin usar en §3 esta aqui y se usa.</summary>
public sealed class CopyOptions
{
    // --- copia ---
    /// <summary>Tamano de bloque. 0 = adaptativo segun el destino (§3).</summary>
    public int BufferSize { get; set; }

    /// <summary>Verificar por SHA-256 al terminar cada destino (§3).</summary>
    public bool Verify { get; set; } = true;

    /// <summary>Copiar fecha de modificacion y de creacion (§3: estaba declarado y no se aplicaba).</summary>
    public bool CopyTimestamps { get; set; } = true;

    /// <summary>Copiar atributos normales, saltando los del sistema (§3).</summary>
    public bool CopyAttributes { get; set; } = true;

    /// <summary>Espacio libre que hay que dejar en el destino, en bytes (§3: declarado y nunca comprobado).</summary>
    public long MinFreeSpace { get; set; }

    /// <summary>Ficheros a la vez, cada uno escribiendo a todos los destinos (§3).</summary>
    public int ParallelFiles { get; set; } = 1;

    /// <summary>Reintentos por fichero, con espera creciente (§3).</summary>
    public int Retries { get; set; } = 3;

    // --- que se copia ---
    public DateMode DateMode { get; set; } = DateMode.Todas;
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    /// <summary>Si se indica, solo entran los nombres que casen estos comodines.</summary>
    public string? IncludePatterns { get; set; }

    /// <summary>Los nombres que casen estos comodines no entran.</summary>
    public string? ExcludePatterns { get; set; }

    /// <summary>Carpetas que no se recorren ni se borran nunca (plan 8.1).</summary>
    public List<string> ExcludeDirectories { get; } = new()
    {
        "System Volume Information", "$RECYCLE.BIN", "$Recycle.Bin", "Recovery",
        "Config.Msi", "MSOCache"
    };

    // --- destino ---
    public StructureMode Structure { get; set; } = StructureMode.Plana;

    /// <summary>Si el destino ya tiene el fichero.</summary>
    public Conflict Conflict { get; set; } = Conflict.SaltarSiIgual;

    /// <summary>Mover: borra el origen, pero solo tras cuadrar el hash (§3 y §8.1).</summary>
    public bool Move { get; set; }

    /// <summary>Espejo: borra del destino lo que no este en el origen. Solo si no hubo fallos (§8.1).</summary>
    public bool Mirror { get; set; }

    /// <summary>Elimina las carpetas que quedaron vacias en el origen tras mover (§3).</summary>
    public bool CleanupEmptySources { get; set; } = true;

    /// <summary>Tamano de bloque ya resuelto para un destino concreto.</summary>
    public int BufferFor(Devices.DeviceInfo? d)
    {
        if (BufferSize > 0) return BufferSize;
        return d?.Kind switch
        {
            Devices.DeviceKind.UsbFlash => 1 << 20,          // 1 MB: en memorias lentas, bloques grandes empeoran
            Devices.DeviceKind.UsbExternal => 2 << 20,       // 2 MB
            Devices.DeviceKind.NetworkShare => 4 << 20,      // 4 MB
            Devices.DeviceKind.SystemDisk => 4 << 20,        // 4 MB
            _ => 4 << 20
        };
    }
}

/// <summary>Un fichero que va a entrar en la copia.</summary>
public sealed class FileEntry
{
    public required string FullPath { get; init; }
    /// <summary>Ruta relativa a la raiz de origen, que es lo que da la estructura.</summary>
    public required string RelativePath { get; init; }
    public long Length { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public DateTime CreationUtc { get; init; }
    public FileAttributes Attributes { get; init; }
    public string Nombre => Path.GetFileName(FullPath);
}

/// <summary>Lo que ha pasado con un fichero.</summary>
public enum FileOutcome
{
    Copiado,
    Saltado,
    Verificado,
    Movido,
    Imposible,
    Error
}

/// <summary>Un fallo, con el motivo en claro. PLAN.md 2.5 y 2.8 muestran estos datos.</summary>
public sealed class CopyError
{
    public string Fichero { get; init; } = "";
    public string Origen { get; init; } = "";
    public string Destino { get; init; } = "";
    public string Motivo { get; init; } = "";
    public DateTime Hora { get; init; } = DateTime.Now;
    /// <summary>True si volver a intentarlo tiene sentido (se desconecto un USB, etc.).</summary>
    public bool Reintentable { get; init; }
}

/// <summary>Cuenta por destino, para el resumen de §2.8.</summary>
public sealed class DestinoResumen
{
    public string Etiqueta { get; set; } = "";
    public string Raiz { get; set; } = "";
    public long Ficheros { get; set; }
    public long Bytes { get; set; }
    public long Errores { get; set; }
    public double Segundos { get; set; }
    public double Velocidad => Segundos > 0 ? Bytes / Segundos : 0;
}

/// <summary>Resultado de una operacion completa.</summary>
public sealed class CopyResult
{
    public long FilesTotal { get; set; }
    public long FilesCopied { get; set; }
    public long FilesSkipped { get; set; }
    public long FilesVerified { get; set; }
    public long FilesFailed { get; set; }
    public long BytesCopied { get; set; }
    public DateTime Inicio { get; set; } = DateTime.Now;
    public TimeSpan Duracion { get; set; }
    public bool Cancelado { get; set; }
    public List<CopyError> Errors { get; } = new();
    public List<DestinoResumen> Destinos { get; } = new();
    public List<string> OrigenesVacios { get; } = new();

    public bool Success => !Cancelado && FilesFailed == 0 && Errors.Count == 0;

    public double VelocidadMedia => Duracion.TotalSeconds > 0
        ? BytesCopied / Duracion.TotalSeconds : 0;
}

/// <summary>Progreso. Se empuja a la ventana cada pocos cientos de ms (§3).</summary>
public sealed class CopyProgress
{
    public long FilesTotal { get; init; }
    public long FilesDone { get; set; }
    public long BytesTotal { get; init; }
    public long BytesDone { get; set; }
    public string FicheroActual { get; set; } = "";

    public double Percent => BytesTotal > 0 ? Math.Clamp(BytesDone * 100.0 / BytesTotal, 0, 100) : 0;

    /// <summary>
    /// Tiempo que queda. PLAN.md §3: "La GUI no muestra el tiempo restante".
    /// Se calcula con la velocidad real de esta copia, no con una estimacion fija.
    /// </summary>
    public double Restante(double segundosTranscurridos)
    {
        if (BytesDone <= 0 || segundosTranscurridos <= 0) return double.NaN;
        var porSegundo = BytesDone / segundosTranscurridos;
        if (porSegundo <= 0) return double.NaN;
        return (BytesTotal - BytesDone) / porSegundo;
    }

    /// <summary>Velocidad real medida, en bytes por segundo.</summary>
    public double Velocidad(double segundosTranscurridos) =>
        segundosTranscurridos > 0 ? BytesDone / segundosTranscurridos : 0;
}