namespace QbaswingMultiTask.Util;

/// <summary>Carpetas de datos del programa. PLAN.md seccion 1: %AppData%\QbaswingMultiTask.</summary>
public static class AppPaths
{
    private static string Root =>
#if ANDROID
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "qbaswing-multitask");
#else
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QbaswingMultiTask");
#endif

    public static string DataDir => Ensure(Root);
    public static string LogsDir => Ensure(Path.Combine(Root, "logs"));
    public static string ProfilesDir => Ensure(Path.Combine(Root, "perfiles"));
    public static string StatsDir => Ensure(Path.Combine(Root, "estadisticas"));
    public static string IndexDb => Path.Combine(DataDir, "indice.db");
    public static string StatsDb => Path.Combine(StatsDir, "estadisticas.db");
    public static string SettingsFile => Path.Combine(DataDir, "ajustes.json");
    public static string LogFile => Path.Combine(LogsDir, "qbaswing.log");

    /// <summary>Carpeta antigua, para migrar datos (PLAN.md seccion 4).</summary>
    public static string LegacyRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QBasPaquete");

    public static bool HayDatosAntiguos() =>
        Directory.Exists(LegacyRoot) &&
        !string.Equals(LegacyRoot, Root, StringComparison.OrdinalIgnoreCase);

    /// <summary>Carpetas que no se recorren ni se borran nunca (plan 8.1).</summary>
    private static readonly string[] CarpetasSistema =
    {
        "System Volume Information", "$RECYCLE.BIN", "$Recycle.Bin", "Recovery",
        "Config.Msi", "MSOCache"
    };

    public static bool EsCarpetaSistema(string nombre) =>
        CarpetasSistema.Contains(nombre, StringComparer.OrdinalIgnoreCase);

    private static string Ensure(string p)
    {
        try { Directory.CreateDirectory(p); } catch { }
        return p;
    }
}

/// <summary>
/// Formato de tamano en espanol, como el boceto de PLAN.md 2.1: coma decimal y
/// punto para los miles ("12,4 GB", "3.212 ficheros"). Se fija la cultura a
/// es-ES a proposito: en un Windows en ingles saldria "12.4 GB" y no es eso.
/// </summary>
public static class Human
{
    private static readonly System.Globalization.CultureInfo Es =
        System.Globalization.CultureInfo.GetCultureInfo("es-ES");

    public static string Size(long b)
    {
        if (b < 0) b = 0;
        double v = b;
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        if (i == 0) return $"{b} {u[i]}";
        var fmt = v >= 100 ? "0" : v >= 10 ? "0.#" : "0.##";
        return $"{v.ToString(fmt, Es)} {u[i]}";
    }

    /// <summary>Numero con separador de miles espanol: 3212 -> "3.212".</summary>
    public static string Numero(long n) => n.ToString("N0", Es);

    public static string Time(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) return "--:--";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }
}