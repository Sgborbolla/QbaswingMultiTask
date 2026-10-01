using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QbaswingMultiTask.Util;

namespace QbaswingMultiTask.Profiles;

/// <summary>Cuando se lanza un perfil solo (§2.6 y §3 "Programacion horaria").</summary>
public sealed class Horario
{
    public bool Activo { get; set; }
    /// <summary>Hora del dia "HH:mm".</summary>
    public string Hora { get; set; } = "19:00";
    /// <summary>Dias de la semana; vacio = todos.</summary>
    public List<DayOfWeek> Dias { get; set; } = new();
    /// <summary>Se lanza al conectar una unidad (§3 "auto por insercion").</summary>
    public bool PorInsercion { get; set; }
    public List<string> EtiquetasInsercion { get; set; } = new();

    public bool TocaAhora(DateTime ahora)
    {
        if (!Activo) return false;
        if (Dias.Count > 0 && !Dias.Contains(ahora.DayOfWeek)) return false;
        if (!TimeOnly.TryParse(Hora, out var h)) return false;
        return ahora.Hour == h.Hour && ahora.Minute == h.Minute;
    }
}

/// <summary>Un perfil de copia (§2.6). Sustituye a las matrices .xcm.</summary>
public sealed class Profile
{
    public string Nombre { get; set; } = "Nuevo perfil";
    public string Origen { get; set; } = "";
    public List<string> Destinos { get; set; } = new();
    public string Estructura { get; set; } = "auto";     // auto | hdd | flash
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public string Incluir { get; set; } = "";
    public string Excluir { get; set; } = "";
    /// <summary>Expresiones tipo §3: "nombre contiene cap Y tamano > 500MB".</summary>
    public string Expresion { get; set; } = "";
    public bool Mover { get; set; }
    public bool Espejo { get; set; }
    public Horario Horario { get; set; } = new();

    public string DestinosTexto => Destinos.Count == 0 ? "—" : string.Join(" ", Destinos);
    public string AutoTexto => Horario.Activo
        ? $"{Horario.Hora}{(Horario.PorInsercion ? " · inserción" : "")}"
        : "—";
    public string EstructuraTexto => Estructura switch
    {
        "hdd" => "HDD",
        "flash" => "FLASH",
        _ => "auto"
    };
}

/// <summary>Guarda y lee los perfiles en %AppData%\QbaswingMultiTask\perfiles.</summary>
public static class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static string Ruta => Path.Combine(AppPaths.ProfilesDir, "perfiles.json");

    public static List<Profile> Leer()
    {
        try
        {
            if (File.Exists(Ruta))
                return JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(Ruta), Json) ?? new();
        }
        catch { }
        return new List<Profile>();
    }

    public static void Guardar(IEnumerable<Profile> perfiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(perfiles.ToList(), Json));
        }
        catch { }
    }
}
