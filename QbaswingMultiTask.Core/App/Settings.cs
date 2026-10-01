using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QbaswingMultiTask.App;

/// <summary>
/// Ajustes de la pestana Ajustes (PLAN.md §2.7). Se guardan en un solo JSON
/// (%AppData%\QbaswingMultiTask\ajustes.json) para que "cambiar el tema" sea
/// escribir un valor y repintar, sin reiniciar.
/// </summary>
public sealed class Settings
{
    public string Tema { get; set; } = "oscuro";       // "oscuro" | "claro"
    public int TamanoLetra { get; set; } = 13;
    public string Idioma { get; set; } = "es";         // "es" | "en"
    public int BufferMb { get; set; } = 0;             // 0 = adaptativo por unidad (§3)
    public bool Verificar { get; set; } = true;
    public long MinLibreMb { get; set; } = 50;
    public double UmbralLentoMb { get; set; } = 5;
    public int IndiceMinutos { get; set; } = 30;       // 0 = solo a mano
    public bool CopiarFechas { get; set; } = true;
    public bool CopiarAtributos { get; set; } = true;
    public int Paralelo { get; set; } = 1;
    public bool TeclasM2 { get; set; } = true;
    public double UmbralSeries { get; set; } = 0.75;
    public bool ConfirmarPegadoMasivo { get; set; } = true;
    public List<string> Exclusiones { get; set; } = new()
    {
        "System Volume Information", "$RECYCLE.BIN", "$Recycle.Bin", "Recovery",
        "Config.Msi", "MSOCache", "pagefile.sys", "hiberfil.sys", "swapfile.sys"
    };

    public Settings Copia() => new()
    {
        Tema = Tema, TamanoLetra = TamanoLetra, Idioma = Idioma, BufferMb = BufferMb,
        Verificar = Verificar, MinLibreMb = MinLibreMb, UmbralLentoMb = UmbralLentoMb,
        IndiceMinutos = IndiceMinutos, CopiarFechas = CopiarFechas,
        CopiarAtributos = CopiarAtributos, Paralelo = Paralelo, TeclasM2 = TeclasM2,
        UmbralSeries = UmbralSeries, ConfirmarPegadoMasivo = ConfirmarPegadoMasivo,
        Exclusiones = new List<string>(Exclusiones)
    };
}

/// <summary>Lee y escribe los ajustes, y avisa cuando cambian.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private static Settings _actual = CargarDeDisco();

    public static Settings Actual => _actual;

    public static event Action<Settings>? Cambiados;

    public static void Recargar() => _actual = CargarDeDisco();

    public static void Guardar(Settings s)
    {
        _actual = s;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Util.AppPaths.SettingsFile)!);
            File.WriteAllText(Util.AppPaths.SettingsFile, JsonSerializer.Serialize(s, Json));
        }
        catch { /* sin permisos no se puede guardar; los ajustes siguen en memoria */ }
        Cambiados?.Invoke(s);
    }

    private static Settings CargarDeDisco()
    {
        try
        {
            var ruta = Util.AppPaths.SettingsFile;
            if (File.Exists(ruta))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(ruta), Json) ?? new Settings();
        }
        catch { }
        return new Settings();
    }
}
