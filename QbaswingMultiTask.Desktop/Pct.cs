using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace QbaswingMultiTask.Desktop;

/// <summary>
/// Texto del filtro de fechas de §2.1 ("Fecha __→__"). Acepta lo que se escribe
/// a mano y lo que devuelve el calendario: dd/MM/aaaa, aaaa-mm-dd, dd-mm-aa.
/// Si no hay nada escrito, la fecha queda en null (sin filtro por ese lado).
/// </summary>
public sealed class FechaTexto : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime d) return d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        if (string.IsNullOrEmpty(s)) return null;

        if (DateTime.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var es))
            return es;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var iso))
            return iso;
        if (DateTime.TryParseExact(s, "dd-MM-yy", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var corto))
            return corto;
        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var libre))
            return libre;

        return Avalonia.Data.BindingOperations.DoNothing;   // no es una fecha: no se toca
    }
}

/// <summary>Registros de los conversores, para poder usarlos desde el XAML.</summary>
public static class Fechas
{
    public static readonly FechaTexto Txt = new();
}

/// <summary>
/// Convierte un porcentaje (0..100) en píxeles de ancho para las barras.
/// Las barras del PLAN.md se dibujan con un relleno de ancho fijo, no con un
/// Control de progreso: asi el relleno es CELESTE y la punta ROJA se puede
/// colocar encima (§1).
/// </summary>
public sealed class PercentToWidth : IValueConverter
{
    /// <summary>Ancho total de la barra, en píxeles lógicos.</summary>
    public double Total { get; set; } = 200;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var pct = value switch
        {
            double d => d,
            int i => i,
            float f => f,
            decimal m => (double)m,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0
        };
        pct = Math.Clamp(pct, 0, 100);
        return Total * pct / 100.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

/// <summary>Registros de los conversores, para poder usarlos desde el XAML.</summary>
public static class Pct
{
    /// <summary>Barras del rectangulo: 242 de ancho menos 24 de padding = 218.</summary>
    public static readonly PercentToWidth Width = new() { Total = 218 };

    /// <summary>Barra general de la franja de abajo.</summary>
    public static readonly PercentToWidth General = new() { Total = 230 };
}