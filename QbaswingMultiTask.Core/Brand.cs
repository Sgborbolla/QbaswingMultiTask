namespace QbaswingMultiTask;

/// <summary>Identidad del producto. PLAN.md seccion 1.</summary>
public static class Brand
{
    public const string Name = "QbaswingMultiTask";
    public const string Tagline = "Copia, sincroniza, escanea. Todo en uno.";
    public const string Copyright = "© 2026 QBasWinG";

    /// <summary>
    /// Version del producto, leida del propio ensamblado. PLAN.md §8.3 anota que
    /// el numero estaba escrito a mano en cinco sitios: aqui se lee de uno solo,
    /// el que fija Directory.Build.props.
    /// </summary>
    public static string Version { get; } =
        typeof(Brand).Assembly.GetName().Version is { } v && v > new Version(0, 0, 0)
            ? v.ToString(3)
            : "1.0.0";

    /// <summary>Marca de agua de credito, como en el original.</summary>
    public const string Credit = "Freeman y Diseños";
}

/// <summary>
/// Paleta bandera del PLAN.md seccion 1. Estos son los unicos colores que
/// deben usarse: estan aqui para que el XAML los referencie por nombre
/// (DynamicResource) y no Written a mano en cada vista.
/// </summary>
public static class Palette
{
    // --- La bandera ---
    public const string Celeste = "#41A5F5"; // progreso, resaltado, iconos, check
    public const string Azul = "#0E4FA6"; // barras llenas, marcos de destino, acentos
    public const string Rojo = "#C8102E"; // punta de progreso, errores fuertes
    public const string Blanco = "#FFFFFF"; // texto principal

    // --- Los fondos ---
    public const string Navy = "#0B1626"; // fondo general de ventana
    public const string Tarjeta = "#15233B"; // fondo de cada rectangulo
    public const string Borde = "#243452"; // contorno de rectangulo inactivo
    public const string Silver = "#7A8AA6"; // texto secundario

    // --- Estados ---
    public const string Ok = "#2ECC71";
    public const string Aviso = "#F5B041";
}