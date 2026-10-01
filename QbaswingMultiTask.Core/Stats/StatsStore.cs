using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QbaswingMultiTask.Engine;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Stats;

/// <summary>Una operacion terminada, tal cual se graba. PLAN.md §2.4.</summary>
public sealed class StatRecord
{
    public DateTime Cuando { get; set; } = DateTime.Now;
    public string Operacion { get; set; } = "copiar";
    public string Origen { get; set; } = "";
    public string Dispositivo { get; set; } = "";
    public long Ficheros { get; set; }
    public long Bytes { get; set; }
    public double Segundos { get; set; }
    /// <summary>Velocidad contando todo el tiempo, esperas incluidas.</summary>
    public double VelMedia { get; set; }
    /// <summary>Velocidad real mientras se escribia (§2.4: "vel real" de verdad, no la media).</summary>
    public double VelReal { get; set; }
    public long Errores { get; set; }
    public bool Cancelado { get; set; }
}

/// <summary>Lo que enseñan las cuatro tarjetas de §2.4.</summary>
public sealed class StatsResumen
{
    public long Operaciones { get; init; }
    public long Ficheros { get; init; }
    public long Bytes { get; init; }
    public double Segundos { get; init; }
    public long Errores { get; init; }
    public double VelMedia => Segundos > 0 ? Bytes / Segundos : 0;
    public double VelReal { get; init; }
}

/// <summary>
/// Estadisticas reales. PLAN.md §3 anota que las del original eran falsas
/// ("files:0, avg=real, wait=0"): aqui se graba lo que de verdad hizo el motor
/// —ficheros, bytes, segundos, velocidad media y velocidad real— y se puede
/// filtrar por periodo y por dispositivo. Se guarda como JSONL en
/// %AppData%\QbaswingMultiTask\estadisticas (la base SQLite no se pudo restaurar
/// sin red, ver PROGRESO §desvios: mismo contenido, formato abierto).
/// </summary>
public static class StatsStore
{
    private static readonly object Candado = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    private static string Ruta => Path.Combine(AppPaths.StatsDir, "estadisticas.jsonl");

    /// <summary>Graba una operacion. Nunca lanza: si falla el disco, no tumba la copia.</summary>
    public static void Anadir(StatRecord r)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
                File.AppendAllText(Ruta, JsonSerializer.Serialize(r, Json) + Environment.NewLine);
            }
        }
        catch { }
    }

    /// <summary>Graba el resultado de una copia. Una fila por destino (§2.8).</summary>
    public static void Anadir(CopyResult res, string origen, string operacion)
    {
        if (res.Destinos.Count == 0)
        {
            Anadir(new StatRecord
            {
                Operacion = operacion, Origen = origen, Dispositivo = "",
                Ficheros = res.FilesCopied, Bytes = res.BytesCopied,
                Segundos = res.Duracion.TotalSeconds, VelMedia = res.VelocidadMedia,
                VelReal = res.VelocidadMedia, Errores = res.Errors.Count, Cancelado = res.Cancelado
            });
            return;
        }

        foreach (var d in res.Destinos)
        {
            Anadir(new StatRecord
            {
                Operacion = operacion,
                Origen = origen,
                Dispositivo = d.Etiqueta,
                Ficheros = d.Ficheros,
                Bytes = d.Bytes,
                Segundos = d.Segundos,
                VelMedia = res.Duracion.TotalSeconds > 0 ? d.Bytes / res.Duracion.TotalSeconds : 0,
                VelReal = d.Velocidad,
                Errores = d.Errores,
                Cancelado = res.Cancelado
            });
        }
    }

    public static IReadOnlyList<StatRecord> Leer(DateTime? desde = null, DateTime? hasta = null,
        string? dispositivo = null)
    {
        var lista = new List<StatRecord>();
        try
        {
            lock (Candado)
            {
                if (!File.Exists(Ruta)) return lista;
                foreach (var linea in File.ReadLines(Ruta))
                {
                    if (linea.Length == 0) continue;
                    StatRecord? r = null;
                    try { r = JsonSerializer.Deserialize<StatRecord>(linea, Json); } catch { }
                    if (r is null) continue;
                    if (desde is { } d0 && r.Cuando < d0) continue;
                    if (hasta is { } d1 && r.Cuando > d1) continue;
                    if (!string.IsNullOrWhiteSpace(dispositivo) &&
                        !string.Equals(r.Dispositivo, dispositivo, StringComparison.OrdinalIgnoreCase))
                        continue;
                    lista.Add(r);
                }
            }
        }
        catch { }
        return lista;
    }

    public static StatsResumen Resumir(DateTime? desde = null, DateTime? hasta = null,
        string? dispositivo = null)
    {
        var filas = Leer(desde, hasta, dispositivo);
        var bytes = filas.Sum(f => f.Bytes);
        var segundos = filas.Sum(f => f.Segundos);
        // Velocidad real: peso por bytes de la velocidad real de cada destino.
        var real = bytes > 0 ? filas.Sum(f => f.VelReal * f.Bytes) / bytes : 0;
        return new StatsResumen
        {
            Operaciones = filas.Count,
            Ficheros = filas.Sum(f => f.Ficheros),
            Bytes = bytes,
            Segundos = segundos,
            Errores = filas.Sum(f => f.Errores),
            VelReal = real
        };
    }

    public static IReadOnlyList<(DateTime Dia, long Bytes, long Ficheros)> PorDia(
        DateTime? desde = null, DateTime? hasta = null, string? dispositivo = null)
    {
        return Leer(desde, hasta, dispositivo)
            .GroupBy(f => f.Cuando.Date)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(x => x.Bytes), g.Sum(x => x.Ficheros)))
            .ToList();
    }

    /// <summary>Exporta a CSV (con comillas y separador ";" para Excel espanol).</summary>
    public static string ExportarCsv(DateTime? desde = null, DateTime? hasta = null, string? dispositivo = null)
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine("fecha;hora;operacion;origen;dispositivo;ficheros;bytes;MB;segundos;vel media MB/s;vel real MB/s;errores");
        foreach (var f in Leer(desde, hasta, dispositivo))
        {
            sb.Append(f.Cuando.ToString("yyyy-MM-dd", es)).Append(';')
              .Append(f.Cuando.ToString("HH:mm:ss", es)).Append(';')
              .Append(Campo(f.Operacion)).Append(';')
              .Append(Campo(f.Origen)).Append(';')
              .Append(Campo(f.Dispositivo)).Append(';')
              .Append(f.Ficheros.ToString(es)).Append(';')
              .Append(f.Bytes.ToString(es)).Append(';')
              .Append((f.Bytes / 1024.0 / 1024.0).ToString("0.##", es)).Append(';')
              .Append(f.Segundos.ToString("0.##", es)).Append(';')
              .Append((f.VelMedia / 1024.0 / 1024.0).ToString("0.##", es)).Append(';')
              .Append((f.VelReal / 1024.0 / 1024.0).ToString("0.##", es)).Append(';')
              .Append(f.Errores.ToString(es)).AppendLine();
        }
        return sb.ToString();
    }

    private static string Campo(string t)
    {
        t ??= "";
        return t.Contains(';') || t.Contains('"') || t.Contains('\n')
            ? '"' + t.Replace("\"", "\"\"") + '"'
            : t;
    }
}
