using System;
using System.IO;
using System.Text;

namespace QbaswingMultiTask.Util;

/// <summary>
/// Comparador de nombres con comodines de verdad. PLAN.md §3 marca como hueco
/// que el motor usaba "Contains", asi que "cap" casaba "capacitacion" o
/// "recapados". Aqui <c>*</c> es cualquier cosa, <c>?</c> un caracter, y
/// <c>[abc]</c> uno de esos. Todo lo demas es literal.
/// </summary>
public static class Matcher
{
    /// <summary>True si el nombre case el patron, sin distinguir mayusculas.</summary>
    public static bool Like(string nombre, string patron)
    {
        if (string.IsNullOrEmpty(patron)) return true;
        return Like(nombre.AsSpan(), patron.AsSpan());
    }

    private static bool Like(ReadOnlySpan<char> s, ReadOnlySpan<char> p)
    {
        int si = 0, pi = 0;
        int estrella = -1, marca = 0;

        while (si < s.Length)
        {
            if (pi < p.Length && (p[pi] == '?' || Coincide(s[si], p[pi])))
            {
                si++; pi++;
            }
            else if (pi < p.Length && p[pi] == '[')
            {
                var fin = p[pi..].IndexOf(']');
                if (fin < 0) { if (p[pi] != s[si]) return false; si++; pi++; continue; }
                var clase = p.Slice(pi + 1, fin - 1);
                var niega = clase.Length > 0 && (clase[0] == '!' || clase[0] == '^');
                if (niega) clase = clase[1..];
                var dentro = EnClase(s[si], clase);
                if (niega) dentro = !dentro;
                if (!dentro) return false;
                si++; pi += fin + 1;
            }
            else if (pi < p.Length && p[pi] == '*')
            {
                estrella = pi++; marca = si;
            }
            else if (estrella >= 0)
            {
                pi = estrella + 1; si = ++marca;
            }
            else return false;
        }

        while (pi < p.Length && p[pi] == '*') pi++;
        return pi == p.Length;
    }

    private static bool EnClase(char c, ReadOnlySpan<char> clase)
    {
        for (var i = 0; i < clase.Length; i++)
        {
            if (i + 2 < clase.Length && clase[i + 1] == '-')
            {
                if (c >= char.ToUpperInvariant(clase[i]) && c <= char.ToUpperInvariant(clase[i + 2])) return true;
                i += 2;
            }
            else if (char.ToUpperInvariant(clase[i]) == char.ToUpperInvariant(c)) return true;
        }
        return false;
    }

    private static bool Coincide(char a, char b) => char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

    /// <summary>
    /// Pasa un filtro de exclusion tipo "*.tmp;*.bak;thumbs.db" a patrones sueltos.
    /// Se acepta el separador ';' o salto de linea.
    /// </summary>
    public static string[] Patrones(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? Array.Empty<string>()
            : texto.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(t => t.Trim())
                   .Where(t => t.Length > 0)
                   .ToArray();

    /// <summary>True si el nombre case alguno de los patrones.</summary>
    public static bool LikeAny(string nombre, string[] patrones)
    {
        foreach (var p in patrones) if (Like(nombre, p)) return true;
        return false;
    }
}

/// <summary>
/// Extension en minusculas sin el punto, o "" si el fichero no tiene extension.
/// Es la base del modo de estructura "por extension".
/// </summary>
public static class Extensiones
{
    public static string De(string nombre)
    {
        var ultimoPunto = nombre.LastIndexOf('.');
        if (ultimoPunto < 0 || ultimoPunto == nombre.Length - 1) return "";
        return nombre[(ultimoPunto + 1)..].ToLowerInvariant();
    }
}