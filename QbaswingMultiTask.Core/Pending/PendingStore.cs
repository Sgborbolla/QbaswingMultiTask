using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using QbaswingMultiTask.Engine;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Pending;

/// <summary>
/// Cola de pendientes que sobrevive al cierre del programa (PLAN.md §2.5: "cola
/// persistente" y "reintentar lo fallado"). El original solo guardaba la lista
/// de errores de una sesion y al cerrar se perdia. Aqui se guarda, ademas de los
/// errores, el plan completo (ficheros y destinos) para poder reintentar de
/// verdad tras abrir el programa otro dia.
/// </summary>
public static class PendingStore
{
    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true };

    public static string Ruta => Path.Combine(AppPaths.StatsDir, "pendientes.json");

    public static void Guardar(PlanPendiente plan)
    {
        try { File.WriteAllText(Ruta, JsonSerializer.Serialize(plan, Formato)); }
        catch { /* si no se puede guardar, el reintento sigue vivo en memoria */ }
    }

    public static PlanPendiente? Leer()
    {
        try
        {
            if (!File.Exists(Ruta)) return null;
            var plan = JsonSerializer.Deserialize<PlanPendiente>(File.ReadAllText(Ruta));
            return plan is { Ficheros.Count: > 0, Destinos.Count: > 0 } ? plan : null;
        }
        catch { return null; }
    }

    public static void Borrar()
    {
        try { if (File.Exists(Ruta)) File.Delete(Ruta); } catch { }
    }

    // ---------- conversiones ----------

    public static FicheroPendiente De(FileEntry f) => new()
    {
        FullPath = f.FullPath,
        RelativePath = f.RelativePath,
        Length = f.Length,
        LastWriteUtc = f.LastWriteUtc,
        CreationUtc = f.CreationUtc,
        Attributes = (int)f.Attributes
    };

    public static FileEntry A(FicheroPendiente f) => new()
    {
        FullPath = f.FullPath,
        RelativePath = f.RelativePath,
        Length = f.Length,
        LastWriteUtc = f.LastWriteUtc,
        CreationUtc = f.CreationUtc,
        Attributes = (FileAttributes)f.Attributes
    };

    public static OpcionesPendientes De(CopyOptions o)
    {
        var r = new OpcionesPendientes
        {
            BufferSize = o.BufferSize,
            Verify = o.Verify,
            CopyTimestamps = o.CopyTimestamps,
            CopyAttributes = o.CopyAttributes,
            MinFreeSpace = o.MinFreeSpace,
            ParallelFiles = o.ParallelFiles,
            Retries = o.Retries,
            DateMode = o.DateMode,
            DateFrom = o.DateFrom,
            DateTo = o.DateTo,
            IncludePatterns = o.IncludePatterns,
            ExcludePatterns = o.ExcludePatterns,
            Structure = o.Structure,
            Conflict = o.Conflict,
            Move = o.Move,
            Mirror = o.Mirror,
            CleanupEmptySources = o.CleanupEmptySources
        };
        r.ExcludeDirectories.AddRange(o.ExcludeDirectories);
        return r;
    }

    public static CopyOptions A(OpcionesPendientes o)
    {
        var c = new CopyOptions
        {
            BufferSize = o.BufferSize,
            Verify = o.Verify,
            CopyTimestamps = o.CopyTimestamps,
            CopyAttributes = o.CopyAttributes,
            MinFreeSpace = o.MinFreeSpace,
            ParallelFiles = o.ParallelFiles,
            Retries = o.Retries,
            DateMode = o.DateMode,
            DateFrom = o.DateFrom,
            DateTo = o.DateTo,
            IncludePatterns = o.IncludePatterns,
            ExcludePatterns = o.ExcludePatterns,
            Structure = o.Structure,
            Conflict = o.Conflict,
            Move = o.Move,
            Mirror = o.Mirror,
            CleanupEmptySources = o.CleanupEmptySources
        };
        foreach (var d in o.ExcludeDirectories)
            if (!c.ExcludeDirectories.Contains(d, StringComparer.OrdinalIgnoreCase))
                c.ExcludeDirectories.Add(d);
        return c;
    }
}

/// <summary>Un fichero que quedo pendiente, plano para poder guardarlo.</summary>
public sealed class FicheroPendiente
{
    public string FullPath { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long Length { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public DateTime CreationUtc { get; set; }
    public int Attributes { get; set; }
}

/// <summary>Un destino del reintento. La ficha del dispositivo no se guarda:
/// se vuelve a mirar al reintentar, porque pudo reconectarse en otro puerto.</summary>
public sealed class DestinoPendiente
{
    public string Raiz { get; set; } = "";
    public string Etiqueta { get; set; } = "";
    public StructureMode Estructura { get; set; }
}

/// <summary>Las opciones del motor, planas para poder guardarlas.</summary>
public sealed class OpcionesPendientes
{
    public int BufferSize { get; set; }
    public bool Verify { get; set; } = true;
    public bool CopyTimestamps { get; set; } = true;
    public bool CopyAttributes { get; set; } = true;
    public long MinFreeSpace { get; set; }
    public int ParallelFiles { get; set; } = 1;
    public int Retries { get; set; } = 3;
    public DateMode DateMode { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public string? IncludePatterns { get; set; }
    public string? ExcludePatterns { get; set; }
    public StructureMode Structure { get; set; }
    public Conflict Conflict { get; set; }
    public bool Move { get; set; }
    public bool Mirror { get; set; }
    public bool CleanupEmptySources { get; set; } = true;
    public List<string> ExcludeDirectories { get; set; } = new();
}

/// <summary>Un fallo guardado, con lo mismo que muestra la pestana Errores.</summary>
public sealed class ErrorPendiente
{
    public string Fichero { get; set; } = "";
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public string Motivo { get; set; } = "";
    public DateTime Hora { get; set; } = DateTime.Now;
    public bool Reintentable { get; set; }
}

/// <summary>El plan de reintento completo.</summary>
public sealed class PlanPendiente
{
    public DateTime Guardado { get; set; } = DateTime.Now;
    public string Origen { get; set; } = "";
    public List<FicheroPendiente> Ficheros { get; set; } = new();
    public List<DestinoPendiente> Destinos { get; set; } = new();
    public OpcionesPendientes Opciones { get; set; } = new();
    public List<ErrorPendiente> Errores { get; set; } = new();
}
